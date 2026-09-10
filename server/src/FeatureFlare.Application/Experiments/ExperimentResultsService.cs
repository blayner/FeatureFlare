using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Common;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Application.Experiments;

public sealed record VariantResult(
    string Variant,
    bool IsControl,
    long ExposedUsers,
    long ConvertedUsers,
    double ConversionRate,
    decimal TotalValue,
    decimal ValuePerUser,
    ProportionComparison? VersusControl,
    bool IsSignificant);

public sealed record ExperimentResults(
    string ExperimentKey,
    ExperimentStatus Status,
    string Metric,
    double SignificanceLevel,
    long TotalExposedUsers,
    IReadOnlyList<VariantResult> Variants,
    string? Leader);

public sealed class ExperimentResultsService(IExperimentRepository experiments, IExperimentEventRepository events)
{
    public const double DefaultSignificanceLevel = 0.05;

    public async Task<ExperimentResults> GetAsync(
        string experimentKey,
        string? metric = null,
        double significanceLevel = DefaultSignificanceLevel,
        CancellationToken ct = default)
    {
        var experiment = await experiments.GetByKeyAsync(experimentKey, ct)
                         ?? throw new NotFoundException("Experiment", experimentKey);

        var metricKey = metric ?? experiment.PrimaryMetric;
        var aggregates = (await events.AggregateAsync(experiment.Key, metricKey, ct))
            .ToDictionary(a => a.Variant, StringComparer.Ordinal);

        VariantAggregate For(string variant) =>
            aggregates.GetValueOrDefault(variant) ?? new VariantAggregate(variant, 0, 0, 0);

        var control = For(experiment.ControlVariant);
        var results = experiment.Variants
            .Select(v =>
            {
                var a = For(v.Key);
                var isControl = v.Key == experiment.ControlVariant;
                var comparison = isControl
                    ? null
                    : Statistics.CompareProportions(control.ExposedUsers, control.ConvertedUsers, a.ExposedUsers, a.ConvertedUsers);

                return new VariantResult(
                    v.Key,
                    isControl,
                    a.ExposedUsers,
                    a.ConvertedUsers,
                    a.ExposedUsers > 0 ? (double)a.ConvertedUsers / a.ExposedUsers : 0,
                    a.TotalValue,
                    a.ExposedUsers > 0 ? decimal.Round(a.TotalValue / a.ExposedUsers, 4) : 0,
                    comparison,
                    comparison is { PValue: var p } && p < significanceLevel);
            })
            .ToList();

        // Only name a leader when it significantly beats the control.
        var leader = results
            .Where(r => r is { IsControl: false, IsSignificant: true, VersusControl.AbsoluteDifference: > 0 })
            .OrderByDescending(r => r.ConversionRate)
            .Select(r => r.Variant)
            .FirstOrDefault();

        return new ExperimentResults(
            experiment.Key,
            experiment.Status,
            metricKey,
            significanceLevel,
            results.Sum(r => r.ExposedUsers),
            results,
            leader);
    }
}
