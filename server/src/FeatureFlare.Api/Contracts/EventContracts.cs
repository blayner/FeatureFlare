using System.ComponentModel.DataAnnotations;
using FeatureFlare.Application.Experiments;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Api.Contracts;

public sealed record TrackEventDto(
    ExperimentEventKind Kind,
    [Required] string ExperimentKey,
    string? Variant,
    [Required] string UserId,
    string? Metric,
    decimal? Value,
    DateTimeOffset? Timestamp)
{
    public TrackEventInput ToInput() => new(Kind, ExperimentKey, Variant, UserId, Metric, Value, Timestamp);
}

public sealed record TrackEventsRequest([Required, MaxLength(ExperimentEventService.MaxBatchSize)] IReadOnlyList<TrackEventDto> Events);
