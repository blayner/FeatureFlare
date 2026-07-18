namespace FeatureFlare.Domain.Evaluation;

public enum EvaluationReason
{
    Off,
    RuleMatch,
    Fallthrough,
    FlagNotFound,
    Error,
}

/// <summary>Result of evaluating one flag for one context. <see cref="Value"/> is raw JSON.</summary>
public sealed record FlagEvaluation(
    string FlagKey,
    string? VariationKey,
    string? Value,
    EvaluationReason Reason,
    string? RuleId = null,
    string? Error = null)
{
    public static FlagEvaluation NotFound(string flagKey) =>
        new(flagKey, null, null, EvaluationReason.FlagNotFound);

    public static FlagEvaluation Failed(string flagKey, string error) =>
        new(flagKey, null, null, EvaluationReason.Error, Error: error);
}
