using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Application.Abstractions;

/// <summary>Per-variant aggregates. Users are counted once per variant (distinct user keys).</summary>
public sealed record VariantAggregate(string Variant, long ExposedUsers, long ConvertedUsers, decimal TotalValue);

public interface IExperimentEventRepository
{
    Task AddRangeAsync(IReadOnlyCollection<ExperimentEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates exposures and conversions for <paramref name="metric"/>. Only conversions from users that
    /// were exposed to the same variant are counted, so stray conversion events can't inflate a variant.
    /// </summary>
    Task<IReadOnlyList<VariantAggregate>> AggregateAsync(
        string experimentKey,
        string metric,
        CancellationToken cancellationToken = default);
}
