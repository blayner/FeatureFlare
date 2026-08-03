using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FeatureFlare.Application.Flags;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Api.Contracts;

public sealed record VariationDto(string Key, JsonElement Value, string? Name = null)
{
    public Variation ToDomain() => new(Key, Value.GetRawText(), Name);

    public static VariationDto From(Variation variation) =>
        new(variation.Key, JsonValues.ToElement(variation.Value), variation.Name);
}

public sealed record CreateFlagRequest(
    [Required] string Key,
    [Required] string Name,
    string? Description,
    FlagType Type,
    [Required] IReadOnlyList<VariationDto> Variations,
    [Required] string OffVariation,
    [Required] Serve Fallthrough,
    IReadOnlyList<TargetingRule>? Rules,
    bool Enabled = false)
{
    public CreateFlagCommand ToCommand() => new(
        Key,
        Name,
        Description,
        Type,
        Variations.Select(v => v.ToDomain()).ToList(),
        OffVariation,
        Fallthrough,
        Rules ?? [],
        Enabled);
}

public sealed record UpdateFlagRequest(
    [Required] string Name,
    string? Description,
    [Required] IReadOnlyList<VariationDto> Variations,
    [Required] string OffVariation,
    [Required] Serve Fallthrough,
    IReadOnlyList<TargetingRule>? Rules,
    int? ExpectedVersion)
{
    public UpdateFlagCommand ToCommand(string key) => new(
        key,
        Name,
        Description,
        Variations.Select(v => v.ToDomain()).ToList(),
        OffVariation,
        Fallthrough,
        Rules ?? [],
        ExpectedVersion);
}

public sealed record ToggleFlagRequest(bool Enabled, int? ExpectedVersion);

public sealed record FlagResponse(
    string Key,
    string Name,
    string? Description,
    FlagType Type,
    bool Enabled,
    IReadOnlyList<VariationDto> Variations,
    string OffVariation,
    Serve Fallthrough,
    IReadOnlyList<TargetingRule> Rules,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FlagResponse From(FeatureFlag flag) => new(
        flag.Key,
        flag.Name,
        flag.Description,
        flag.Type,
        flag.Enabled,
        flag.Variations.Select(VariationDto.From).ToList(),
        flag.OffVariation,
        flag.Fallthrough,
        flag.Rules,
        flag.Version,
        flag.CreatedAt,
        flag.UpdatedAt);
}
