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
/// serialised and copy-on-write. Versioned upserts ignore stale data so a slow poll response can never
/// roll back a newer streamed change.
/// </summary>
internal sealed class ConfigStore(TimeProvider clock)
{
    private readonly Lock _writeLock = new();
    private volatile ConfigSnapshot _snapshot = ConfigSnapshot.Empty;
    private long _updatedAtTicks;
    private volatile bool _initialized;

    public ConfigSnapshot Current => _snapshot;

    public bool IsInitialized => _initialized;

    public bool IsStale(TimeSpan ttl) =>
        !_initialized || clock.GetUtcNow().UtcTicks - Interlocked.Read(ref _updatedAtTicks) > ttl.Ticks;

    /// <summary>Replaces everything and returns the keys of flags that changed or disappeared.</summary>
    public IReadOnlyList<string> ReplaceAll(SdkConfig config)
    {
        lock (_writeLock)
        {
            var previous = _snapshot;
            var next = ConfigSnapshot.From(config);

            var changed = next.Flags
                .Where(kv => !previous.Flags.TryGetValue(kv.Key, out var old) || old.Version != kv.Value.Version)
                .Select(kv => kv.Key)
                .Concat(previous.Flags.Keys.Where(k => !next.Flags.ContainsKey(k)))
                .ToList();

            Publish(next);
            _initialized = true;
            return changed;
        }
    }

    public bool Upsert(FlagConfig flag) =>
        Mutate(s => TryUpsert(s.Flags, flag.Key, flag, flag.Version, f => f.Version) is { } d ? s.With(flags: d) : null);

    public bool Upsert(SegmentConfig segment) =>
        Mutate(s => TryUpsert(s.Segments, segment.Key, segment, segment.Version, x => x.Version) is { } d ? s.With(segments: d) : null);

    public bool Upsert(ExperimentConfig experiment) =>
        Mutate(s => TryUpsert(s.Experiments, experiment.Key, experiment, experiment.Version, x => x.Version) is { } d ? s.With(experiments: d) : null);

    public bool DeleteFlag(Deletion deletion) =>
        Mutate(s => TryRemove(s.Flags, deletion, f => f.Version) is { } d ? s.With(flags: d) : null);

    public bool DeleteSegment(Deletion deletion) =>
        Mutate(s => TryRemove(s.Segments, deletion, x => x.Version) is { } d ? s.With(segments: d) : null);

    public bool DeleteExperiment(Deletion deletion) =>
        Mutate(s => TryRemove(s.Experiments, deletion, x => x.Version) is { } d ? s.With(experiments: d) : null);

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

    private static ImmutableDictionary<string, T>? TryUpsert<T>(
        ImmutableDictionary<string, T> map, string key, T item, int version, Func<T, int> versionOf) =>
        map.TryGetValue(key, out var current) && versionOf(current) >= version ? null : map.SetItem(key, item);

    private static ImmutableDictionary<string, T>? TryRemove<T>(
        ImmutableDictionary<string, T> map, Deletion deletion, Func<T, int> versionOf) =>
        map.TryGetValue(deletion.Key, out var current) && versionOf(current) < deletion.Version
            ? map.Remove(deletion.Key)
            : null;
}
