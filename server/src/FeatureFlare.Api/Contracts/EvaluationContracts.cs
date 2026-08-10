using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FeatureFlare.Application.Serialization;
using FeatureFlare.Domain.Evaluation;

namespace FeatureFlare.Api.Contracts;

public sealed record EvaluationContextDto([Required] string Key, Dictionary<string, JsonElement>? Attributes)
{
    public EvaluationContext ToDomain() => new(
        Key,
        Attributes?.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal));
}

public sealed record FlagEvaluationDto(
    string? Variation,
    JsonElement? Value,
    EvaluationReason Reason,
    string? RuleId,
    string? Error)
{
    public static FlagEvaluationDto From(FlagEvaluation evaluation) => new(
        evaluation.VariationKey,
        RawJson.ToElementOrNull(evaluation.Value),
        evaluation.Reason,
        evaluation.RuleId,
        evaluation.Error);
}

public sealed record EvaluateAllResponse(IReadOnlyDictionary<string, FlagEvaluationDto> Flags);
