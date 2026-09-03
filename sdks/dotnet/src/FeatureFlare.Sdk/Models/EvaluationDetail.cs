using System.Text.Json;

namespace FeatureFlare.Sdk.Models;

public enum EvaluationReason
{
    Off,
    RuleMatch,
    Fallthrough,
    FlagNotFound,
    WrongType,
    Error,
}

/// <summary>Explains a flag evaluation. <see cref="Value"/> is <c>null</c> when the default was served.</summary>
public sealed record EvaluationDetail(
    string FlagKey,
    string? Variation,
    JsonElement? Value,
    EvaluationReason Reason,
    string? RuleId = null,
    string? Error = null)
{
    public bool IsDefault => Variation is null;

    internal static EvaluationDetail Failed(string flagKey, EvaluationReason reason, string error) =>
        new(flagKey, null, null, reason, Error: error);
}

public enum AssignmentReason
{
    Assigned,
    NotRunning,
    NotInTraffic,
    ExperimentNotFound,
}

public sealed record ExperimentAssignment(string ExperimentKey, string? Variant, AssignmentReason Reason)
{
    public bool InExperiment => Reason == AssignmentReason.Assigned;
}

/// <summary>Exposure/conversion event sent to <c>POST /api/events</c>.</summary>
public sealed record TrackEvent(
    string Kind,
    string ExperimentKey,
    string? Variant,
    string UserId,
    string? Metric,
    decimal? Value,
    DateTimeOffset Timestamp)
{
    public const string Exposure = "exposure";
    public const string Conversion = "conversion";
}
