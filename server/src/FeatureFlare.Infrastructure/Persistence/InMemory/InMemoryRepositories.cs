using System.Collections.Concurrent;
using FeatureFlare.Application.Abstractions;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Infrastructure.Persistence.InMemory;

/// <summary>
/// Process-local storage used for development, tests and demos. Registered as singleton so data survives
/// across requests; aggregates validate before mutating, so a failed command cannot corrupt stored state.
/// </summary>
public sealed class InMemoryFlagRepository : IFlagRepository
{
    private readonly ConcurrentDictionary<string, FeatureFlag> _flags = new(StringComparer.Ordinal);

    public Task<FeatureFlag?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_flags.GetValueOrDefault(key));

    public Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FeatureFlag>>(_flags.Values.OrderBy(f => f.Key, StringComparer.Ordinal).ToList());

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_flags.ContainsKey(key));

    public void Add(FeatureFlag flag) => _flags[flag.Key] = flag;

    public void Remove(FeatureFlag flag) => _flags.TryRemove(flag.Key, out _);
}

public sealed class InMemorySegmentRepository : ISegmentRepository
{
    private readonly ConcurrentDictionary<string, Segment> _segments = new(StringComparer.Ordinal);

    public Task<Segment?> GetByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_segments.GetValueOrDefault(key));

    public Task<IReadOnlyList<Segment>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Segment>>(_segments.Values.OrderBy(s => s.Key, StringComparer.Ordinal).ToList());

    public Task<IReadOnlyList<string>> FindMissingKeysAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(keys.Where(k => !_segments.ContainsKey(k)).Distinct().ToList());

    public void Add(Segment segment) => _segments[segment.Key] = segment;

    public void Remove(Segment segment) => _segments.TryRemove(segment.Key, out _);
}

public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
