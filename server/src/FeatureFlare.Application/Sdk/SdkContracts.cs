using System.Text.Json;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Sdk;

// Wire contract consumed by the SDKs for local evaluation. Field names and semantics are documented in
// spec/README.md; changing them is a breaking change for every SDK.

public sealed record SdkVariation(string Key, JsonElement Value);

public sealed record SdkFlagConfig(
    string Key,
    FlagType Type,
    bool Enabled,
    string Salt,
    IReadOnlyList<SdkVariation> Variations,
    string OffVariation,
    Serve Fallthrough,
    IReadOnlyList<TargetingRule> Rules,
    int Version);

public sealed record SdkSegmentConfig(
    string Key,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<SegmentRule> Rules,
    int Version);

public sealed record SdkConfig(
    IReadOnlyList<SdkFlagConfig> Flags,
    IReadOnlyList<SdkSegmentConfig> Segments,
    DateTimeOffset GeneratedAt);

/// <summary>Payload of <c>*.deleted</c> stream events.</summary>
public sealed record SdkDeletion(string Key, int Version);
