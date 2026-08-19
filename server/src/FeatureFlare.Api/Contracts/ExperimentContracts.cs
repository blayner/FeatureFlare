using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FeatureFlare.Application.Experiments;
using FeatureFlare.Application.Serialization;
using FeatureFlare.Domain.Experiments;

namespace FeatureFlare.Api.Contracts;

public sealed record ExperimentVariantDto(string Key, int Weight, JsonElement? Payload = null)
{
    public ExperimentVariant ToDomain() => new(
        Key,
        Weight,
        Payload is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } p ? p.GetRawText() : null);

    public static ExperimentVariantDto From(ExperimentVariant variant) =>
        new(variant.Key, variant.Weight, RawJson.ToElementOrNull(variant.Payload));
}

public sealed record CreateExperimentRequest(
    [Required] string Key,
    [Required] string Name,
    string? Hypothesis,
    [Required] IReadOnlyList<ExperimentVariantDto> Variants,
    [Required] string ControlVariant,
    [Required] string PrimaryMetric,
    int TrafficAllocation = Experiment.FullTraffic)
{
    public CreateExperimentCommand ToCommand() => new(
        Key,
        Name,
        Hypothesis,
        Variants.Select(v => v.ToDomain()).ToList(),
        ControlVariant,
        PrimaryMetric,
        TrafficAllocation);
}

public sealed record RedesignExperimentRequest(
    [Required] string Name,
    string? Hypothesis,
    [Required] IReadOnlyList<ExperimentVariantDto> Variants,
    [Required] string ControlVariant,
    [Required] string PrimaryMetric,
    int? ExpectedVersion)
{
    public RedesignExperimentCommand ToCommand(string key) => new(
        key,
        Name,
        Hypothesis,
        Variants.Select(v => v.ToDomain()).ToList(),
        ControlVariant,
        PrimaryMetric,
        ExpectedVersion);
}

public sealed record SetTrafficRequest([Range(0, Experiment.FullTraffic)] int TrafficAllocation);

public sealed record ConcludeExperimentRequest(string? WinningVariant);

public sealed record ExperimentResponse(
    string Key,
    string Name,
    string? Hypothesis,
    ExperimentStatus Status,
    int TrafficAllocation,
    string ControlVariant,
    string PrimaryMetric,
    IReadOnlyList<ExperimentVariantDto> Variants,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ConcludedAt,
    string? WinningVariant,
    int Version)
{
    public static ExperimentResponse From(Experiment experiment) => new(
        experiment.Key,
        experiment.Name,
        experiment.Hypothesis,
        experiment.Status,
        experiment.TrafficAllocation,
        experiment.ControlVariant,
        experiment.PrimaryMetric,
        experiment.Variants.Select(ExperimentVariantDto.From).ToList(),
        experiment.StartedAt,
        experiment.ConcludedAt,
        experiment.WinningVariant,
        experiment.Version);
}
