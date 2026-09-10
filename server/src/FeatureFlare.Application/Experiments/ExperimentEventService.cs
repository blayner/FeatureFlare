using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Evaluation;
using FeatureFlare.Domain.Common;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Application.Experiments;

public sealed record TrackEventInput(
    ExperimentEventKind Kind,
    string ExperimentKey,
    string? Variant,
    string UserId,
    string? Metric,
    decimal? Value,
    DateTimeOffset? Timestamp);

public sealed record IngestionResult(int Accepted, int Rejected, IReadOnlyList<string> Errors);

/// <summary>
/// Ingests exposure/conversion batches from SDKs. Events are validated individually so one bad event never
/// rejects a whole batch. The variant is re-computed server-side: the SDK value is only a hint, which keeps
/// tampered or stale clients from polluting results.
/// </summary>
public sealed class ExperimentEventService(
    IExperimentRepository experiments,
    IExperimentEventRepository events,
    TimeProvider clock)
{
    public const int MaxBatchSize = 1_000;
    private const int MaxReportedErrors = 20;

    public async Task<IngestionResult> IngestAsync(IReadOnlyList<TrackEventInput> batch, CancellationToken ct = default)
    {
        if (batch.Count > MaxBatchSize)
        {
            throw new DomainException($"A batch can contain at most {MaxBatchSize} events.");
        }

        var now = clock.GetUtcNow();
        var cache = new Dictionary<string, Experiment?>(StringComparer.Ordinal);
        var accepted = new List<ExperimentEvent>(batch.Count);
        var errors = new List<string>();
        var rejected = 0;

        foreach (var input in batch)
        {
            if (!cache.TryGetValue(input.ExperimentKey, out var experiment))
            {
                experiment = await experiments.GetByKeyAsync(input.ExperimentKey, ct);
                cache[input.ExperimentKey] = experiment;
            }

            var error = TryCreate(input, experiment, now, out var created);
            if (created is not null)
            {
                accepted.Add(created);
            }
            else
            {
                rejected++;
                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add(error!);
                }
            }
        }

        if (accepted.Count > 0)
        {
            await events.AddRangeAsync(accepted, ct);
        }

        return new IngestionResult(accepted.Count, rejected, errors);
    }

    private static string? TryCreate(TrackEventInput input, Experiment? experiment, DateTimeOffset now, out ExperimentEvent? created)
    {
        created = null;
        if (experiment is null)
        {
            return $"Experiment '{input.ExperimentKey}' not found.";
        }

        // Paused experiments still accept conversions from users exposed before the pause.
        var acceptsKind = experiment.Status == ExperimentStatus.Running ||
                          (experiment.Status == ExperimentStatus.Paused && input.Kind == ExperimentEventKind.Conversion);
        if (!acceptsKind)
        {
            return $"Experiment '{experiment.Key}' is {experiment.Status.ToString().ToLowerInvariant()}.";
        }

        var assignment = ExperimentAssigner.Assign(experiment, input.UserId);
        var variant = assignment.Variant ?? (experiment.Status == ExperimentStatus.Paused ? input.Variant : null);
        if (variant is null)
        {
            return $"User is not part of experiment '{experiment.Key}'.";
        }

        try
        {
            var occurredAt = input.Timestamp ?? now;
            created = input.Kind == ExperimentEventKind.Exposure
                ? ExperimentEvent.Exposure(experiment, variant, input.UserId, occurredAt, now)
                : ExperimentEvent.Conversion(experiment, variant, input.UserId, input.Metric, input.Value, occurredAt, now);
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }
}
