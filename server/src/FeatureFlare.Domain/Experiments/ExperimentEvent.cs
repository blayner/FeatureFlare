using FeatureFlare.Domain.Common;

namespace FeatureFlare.Domain.Experiments;

public enum ExperimentEventKind
{
    Exposure,
    Conversion,
}

/// <summary>Append-only analytics record. Not an aggregate: events are never modified after ingestion.</summary>
public sealed class ExperimentEvent
{
    private ExperimentEvent()
    {
    }

    public Guid Id { get; private set; }

    public string ExperimentKey { get; private set; } = default!;

    public string Variant { get; private set; } = default!;

    public string UserKey { get; private set; } = default!;

    public ExperimentEventKind Kind { get; private set; }

    /// <summary>Metric key for conversions; <c>null</c> for exposures.</summary>
    public string? Metric { get; private set; }

    /// <summary>Optional monetary/numeric value of a conversion (e.g. order total).</summary>
    public decimal? Value { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public static ExperimentEvent Exposure(
        Experiment experiment, string variant, string userKey, DateTimeOffset occurredAt, DateTimeOffset receivedAt) =>
        Create(experiment, ExperimentEventKind.Exposure, variant, userKey, null, null, occurredAt, receivedAt);

    public static ExperimentEvent Conversion(
        Experiment experiment,
        string variant,
        string userKey,
        string? metric,
        decimal? value,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt) =>
        Create(
            experiment,
            ExperimentEventKind.Conversion,
            variant,
            userKey,
            metric ?? experiment.PrimaryMetric,
            value,
            occurredAt,
            receivedAt);

    private static ExperimentEvent Create(
        Experiment experiment,
        ExperimentEventKind kind,
        string variant,
        string userKey,
        string? metric,
        decimal? value,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt)
    {
        if (experiment.FindVariant(variant) is null)
        {
            throw new DomainException($"Variant '{variant}' does not exist in experiment '{experiment.Key}'.");
        }

        if (string.IsNullOrWhiteSpace(userKey) || userKey.Length > 256)
        {
            throw new DomainException("User key is required and must have at most 256 characters.");
        }

        if (metric is not null)
        {
            Guard.Key(metric, "Metric");
        }

        // Clock skew protection: SDK clocks can't be trusted to be in the future.
        var occurred = (occurredAt > receivedAt ? receivedAt : occurredAt).ToUniversalTime();

        return new ExperimentEvent
        {
            Id = Guid.CreateVersion7(receivedAt),
            ExperimentKey = experiment.Key,
            Variant = variant,
            UserKey = userKey,
            Kind = kind,
            Metric = metric,
            Value = value,
            OccurredAt = occurred,
            ReceivedAt = receivedAt.ToUniversalTime(),
        };
    }
}
