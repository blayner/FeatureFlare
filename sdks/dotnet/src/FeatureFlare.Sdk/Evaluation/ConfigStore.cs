using System.Collections.Immutable;
using FeatureFlare.Sdk.Models;

namespace FeatureFlare.Sdk.Evaluation;

/// <summary>Immutable view of the configuration. An evaluation always sees one consistent snapshot.</summary>
public sealed class ConfigSnapshot
{
    public static readonly ConfigSnapshot Empty = new(
        ImmutableDictionary<string, FlagConfig>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableDictionary<string, SegmentConfig>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableDictionary<string, ExperimentConfig>.Empty.WithComparers(StringComparer.Ordinal));

    internal ConfigSnapshot(
        ImmutableDictionary<string, FlagConfig> flags,
        ImmutableDictionary<string, SegmentConfig> segments,
        ImmutableDictionary<string, ExperimentConfig> experiments)
    {
        Flags = flags;
        Segments = segments;
        Experiments = experiments;
    }

    public ImmutableDictionary<string, FlagConfig> Flags { get; }

    public ImmutableDictionary<string, SegmentConfig> Segments { get; }

    public ImmutableDictionary<string, ExperimentConfig> Experiments { get; }

    public static ConfigSnapshot From(SdkConfig config) => new(
        config.Flags.ToImmutableDictionary(f => f.Key, StringComparer.Ordinal),
        config.Segments.ToImmutableDictionary(s => s.Key, StringComparer.Ordinal),
        config.Experiments.ToImmutableDictionary(e => e.Key, StringComparer.Ordinal));

    internal ConfigSnapshot With(
        ImmutableDictionary<string, FlagConfig>? flags = null,
        ImmutableDictionary<string, SegmentConfig>? segments = null,
        ImmutableDictionary<string, ExperimentConfig>? experiments = null) =>
        new(flags ?? Flags, segments ?? Segments, experiments ?? Experiments);
}

/// <summary>
/// Thread-safe holder of the current snapshot. Reads are lock-free (a volatile reference); writes are
/// serialised and copy-on-write.
/// </summary>
/// <remarks>
/// The SSE stream and full config downloads race: a snapshot requested at time T can arrive after stream
/// events newer than T were applied. <see cref="ReplaceAll"/> therefore <b>merges</b> instead of replacing:
/// newer cached versions win, deletions are remembered as tombstones so a stale snapshot cannot resurrect
/// an item, and items the stream created after the request started are kept. Call <see cref="Mark"/> right
/// before requesting the snapshot and pass the value to <see cref="ReplaceAll"/>.
/// </remarks>
internal sealed class ConfigStore(TimeProvider clock)
{
    private readonly Lock _writeLock = new();
    private readonly Bookkeeping _flagBook = new();
    private readonly Bookkeeping _segmentBook = new();
    private readonly Bookkeeping _experimentBook = new();
    private volatile ConfigSnapshot _snapshot = ConfigSnapshot.Empty;
    private long _sequence;
    private long _updatedAtTicks;
    private volatile bool _initialized;

    public ConfigSnapshot Current => _snapshot;

    public bool IsInitialized => _initialized;

    public bool IsStale(TimeSpan ttl) =>
        !_initialized || clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _updatedAtTicks) > ttl.Ticks;

    public long Mark() => Interlocked.Read(ref _sequence);

    /// <summary>Merges a full snapshot and returns the keys of flags whose visible state changed.</summary>
    public IReadOnlyList<string> ReplaceAll(SdkConfig config, long? since = null)
    {
        lock (_writeLock)
        {
            var mark = since ?? _sequence;
            var current = _snapshot;

            var flags = Merge(current.Flags, _flagBook, config.Flags, f => f.Key, f => f.Version, mark, out var changed);
            var segments = Merge(current.Segments, _segmentBook, config.Segments, s => s.Key, s => s.Version, mark, out _);
            var experiments = Merge(current.Experiments, _experimentBook, config.Experiments, e => e.Key, e => e.Version, mark, out _);

            Publish(new ConfigSnapshot(flags, segments, experiments));
            _initialized = true;
            return changed;
        }
    }

    public bool Upsert(FlagConfig flag) =>
        Mutate(s => TryUpsert(s.Flags, _flagBook, flag.Key, flag, flag.Version, f => f.Version) is { } d ? s.With(flags: d) : null);

    public bool Upsert(SegmentConfig segment) =>
        Mutate(s => TryUpsert(s.Segments, _segmentBook, segment.Key, segment, segment.Version, x => x.Version) is { } d ? s.With(segments: d) : null);

    public bool Upsert(ExperimentConfig experiment) =>
        Mutate(s => TryUpsert(s.Experiments, _experimentBook, experiment.Key, experiment, experiment.Version, x => x.Version) is { } d ? s.With(experiments: d) : null);

    public bool DeleteFlag(Deletion deletion) =>
        Mutate(s => TryRemove(s.Flags, _flagBook, deletion, f => f.Version) is { } d ? s.With(flags: d) : null);

    public bool DeleteSegment(Deletion deletion) =>
        Mutate(s => TryRemove(s.Segments, _segmentBook, deletion, x => x.Version) is { } d ? s.With(segments: d) : null);

    public bool DeleteExperiment(Deletion deletion) =>
        Mutate(s => TryRemove(s.Experiments, _experimentBook, deletion, x => x.Version) is { } d ? s.With(experiments: d) : null);

    private bool Mutate(Func<ConfigSnapshot, ConfigSnapshot?> mutation)
    {
        lock (_writeLock)
        {
            var next = mutation(_snapshot);
            if (next is null)
            {
                return false;
            }

            Publish(next);
            return true;
        }
    }

    private void Publish(ConfigSnapshot next)
    {
        _snapshot = next;
        Interlocked.Exchange(ref _updatedAtTicks, clock.GetUtcNow().UtcTicks);
    }

    // The helpers below always run under _writeLock.

    private ImmutableDictionary<string, T>? TryUpsert<T>(
        ImmutableDictionary<string, T> map, Bookkeeping book, string key, T item, int version, Func<T, int> versionOf)
    {
        if (map.TryGetValue(key, out var current) && versionOf(current) >= version)
        {
            return null;
        }

        if (book.Tombstones.TryGetValue(key, out var tombstone) && tombstone.Version >= version)
        {
            return null;
        }

        book.Tombstones.Remove(key);
        book.Touched[key] = Interlocked.Increment(ref _sequence);
        return map.SetItem(key, item);
    }

    private ImmutableDictionary<string, T>? TryRemove<T>(
        ImmutableDictionary<string, T> map, Bookkeeping book, Deletion deletion, Func<T, int> versionOf)
    {
        var exists = map.TryGetValue(deletion.Key, out var current);
        if (exists && versionOf(current!) >= deletion.Version)
        {
            return null;
        }

        // Record the tombstone even when the item is not known yet: the deletion may have overtaken the
        // snapshot that still contains it.
        var seq = Interlocked.Increment(ref _sequence);
        if (!book.Tombstones.TryGetValue(deletion.Key, out var tombstone) || tombstone.Version < deletion.Version)
        {
            book.Tombstones[deletion.Key] = (deletion.Version, seq);
        }

        book.Touched[deletion.Key] = seq;
        return exists ? map.Remove(deletion.Key) : null;
    }

    private static ImmutableDictionary<string, T> Merge<T>(
        ImmutableDictionary<string, T> current,
        Bookkeeping book,
        IReadOnlyList<T> snapshot,
        Func<T, string> keyOf,
        Func<T, int> versionOf,
        long since,
        out IReadOnlyList<string> changed)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        var inSnapshot = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incoming in snapshot)
        {
            var key = keyOf(incoming);
            inSnapshot.Add(key);

            if (current.TryGetValue(key, out var existing) && versionOf(existing) > versionOf(incoming))
            {
                builder[key] = existing;
            }
            else if (!(book.Tombstones.TryGetValue(key, out var tombstone) && tombstone.Version >= versionOf(incoming)))
            {
                builder[key] = incoming;
            }
        }

        foreach (var (key, existing) in current)
        {
            // Created by the stream after the snapshot was requested.
            if (!inSnapshot.Contains(key) && book.Touched.GetValueOrDefault(key, -1) > since)
            {
                builder[key] = existing;
            }
        }

        var next = builder.ToImmutable();
        changed = next
            .Where(kv => !current.TryGetValue(kv.Key, out var old) || versionOf(old) != versionOf(kv.Value))
            .Select(kv => kv.Key)
            .Concat(current.Keys.Where(k => !next.ContainsKey(k)))
            .ToList();

        // Everything up to `since` is reflected by the snapshot now; drop the old bookkeeping.
        foreach (var key in book.Tombstones.Where(t => t.Value.Seq <= since).Select(t => t.Key).ToList())
        {
            book.Tombstones.Remove(key);
        }

        foreach (var key in book.Touched.Where(t => t.Value <= since).Select(t => t.Key).ToList())
        {
            book.Touched.Remove(key);
        }

        return next;
    }

    private sealed class Bookkeeping
    {
        public Dictionary<string, (int Version, long Seq)> Tombstones { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> Touched { get; } = new(StringComparer.Ordinal);
    }
}
