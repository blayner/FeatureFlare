using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Application.Flags;

public sealed record CreateFlagCommand(
    string Key,
    string Name,
    string? Description,
    FlagType Type,
    IReadOnlyList<Variation> Variations,
    string OffVariation,
    Serve Fallthrough,
    IReadOnlyList<TargetingRule>? Rules,
    bool Enabled = false);

/// <summary>Full replacement of a flag's configuration (PUT semantics).</summary>
public sealed record UpdateFlagCommand(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<Variation> Variations,
    string OffVariation,
    Serve Fallthrough,
    IReadOnlyList<TargetingRule> Rules,
    int? ExpectedVersion);

public sealed record ToggleFlagCommand(string Key, bool Enabled, int? ExpectedVersion = null);

public sealed record DeleteFlagCommand(string Key);
