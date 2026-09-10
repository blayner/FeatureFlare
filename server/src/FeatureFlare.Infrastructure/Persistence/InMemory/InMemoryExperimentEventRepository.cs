using FeatureFlare.Application.Abstractions;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Infrastructure.Persistence.InMemory;

public sealed class InMemoryExperimentEventRepository : IExperimentEventRepository
{
    private readonly Lock _lock = new();
    private readonly List<ExperimentEvent> _events = [];

    public Task AddRangeAsync(IReadOnlyCollection<ExperimentEvent> events, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _events.AddRange(events);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VariantAggregate>> AggregateAsync(
        string experimentKey,
        string metric,
        CancellationToken cancellationToken = default)
    {
        ExperimentEvent[] snapshot;
        lock (_lock)
        {
            snapshot = _events.Where(e => e.ExperimentKey == experimentKey).ToArray();
        }

        var result = snapshot
            .GroupBy(e => e.Variant, StringComparer.Ordinal)
            .Select(group =>
            {
                var exposed = group
                    .Where(e => e.Kind == ExperimentEventKind.Exposure)
                    .Select(e => e.UserKey)
                    .ToHashSet(StringComparer.Ordinal);

                var conversions = group
                    .Where(e => e.Kind == ExperimentEventKind.Conversion && e.Metric == metric && exposed.Contains(e.UserKey))
                    .ToList();

                return new VariantAggregate(
                    group.Key,
                    exposed.Count,
                    conversions.Select(e => e.UserKey).Distinct(StringComparer.Ordinal).LongCount(),
                    conversions.Sum(e => e.Value ?? 0));
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<VariantAggregate>>(result);
    }
}
