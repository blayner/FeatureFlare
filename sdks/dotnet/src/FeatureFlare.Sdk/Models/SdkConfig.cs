using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatureFlare.Sdk.Models;

// Mirrors the server's wire contract (see spec/README.md).

public enum FlagType
{
    Boolean,
    String,
    Number,
    Json,
}

public enum ClauseOperator
{
    In,
    Contains,
    StartsWith,
    EndsWith,
    Matches,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    SemVerEqual,
    SemVerLessThan,
    SemVerGreaterThan,
    InSegment,
}

public enum ExperimentStatus
{
    Draft,
    Running,
    Paused,
    Concluded,
}

public sealed record Clause
{
    public required string Attribute { get; init; }

    public required ClauseOperator Operator { get; init; }

    public IReadOnlyList<string> Values { get; init; } = [];

    public bool Negate { get; init; }
}

public sealed record WeightedVariation(string Variation, int Weight);

public sealed record Rollout
{
    public IReadOnlyList<WeightedVariation> Variations { get; init; } = [];

    public string? BucketBy { get; init; }
}

public sealed record Serve
{
    public string? Variation { get; init; }

    public Rollout? Rollout { get; init; }
}

public sealed record TargetingRule
{
    public required string Id { get; init; }

    public IReadOnlyList<Clause> Clauses { get; init; } = [];

    public required Serve Serve { get; init; }

    public string? Description { get; init; }
}

public sealed record Variation(string Key, JsonElement Value);

public sealed record FlagConfig
{
    public required string Key { get; init; }

    public FlagType Type { get; init; }

    public bool Enabled { get; init; }

    public required string Salt { get; init; }

    public IReadOnlyList<Variation> Variations { get; init; } = [];

    public required string OffVariation { get; init; }

    public required Serve Fallthrough { get; init; }

    public IReadOnlyList<TargetingRule> Rules { get; init; } = [];

    public int Version { get; init; }
}

public sealed record SegmentRule
{
    public IReadOnlyList<Clause> Clauses { get; init; } = [];
}

public sealed record SegmentConfig
{
    public required string Key { get; init; }

    public IReadOnlyList<string> Included { get; init; } = [];

    public IReadOnlyList<string> Excluded { get; init; } = [];

    public IReadOnlyList<SegmentRule> Rules { get; init; } = [];

    public int Version { get; init; }
}

public sealed record ExperimentVariant(string Key, int Weight, JsonElement? Payload = null);

public sealed record ExperimentConfig
{
    public required string Key { get; init; }

    public ExperimentStatus Status { get; init; }

    public int TrafficAllocation { get; init; }

    public required string Salt { get; init; }

    public string? ControlVariant { get; init; }

    public string? PrimaryMetric { get; init; }

    public IReadOnlyList<ExperimentVariant> Variants { get; init; } = [];

    public int Version { get; init; }
}

public sealed record SdkConfig
{
    public IReadOnlyList<FlagConfig> Flags { get; init; } = [];

    public IReadOnlyList<SegmentConfig> Segments { get; init; } = [];

    public IReadOnlyList<ExperimentConfig> Experiments { get; init; } = [];

    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record Deletion(string Key, int Version);

internal static class SdkJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
