using System.ComponentModel.DataAnnotations;
using FeatureFlare.Application.Segments;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Api.Contracts;

public sealed record CreateSegmentRequest(
    [Required] string Key,
    [Required] string Name,
    string? Description,
    IReadOnlyList<string>? Included,
    IReadOnlyList<string>? Excluded,
    IReadOnlyList<SegmentRule>? Rules)
{
    public CreateSegmentCommand ToCommand() => new(Key, Name, Description, Included, Excluded, Rules);
}

public sealed record UpdateSegmentRequest(
    [Required] string Name,
    string? Description,
    IReadOnlyList<string>? Included,
    IReadOnlyList<string>? Excluded,
    IReadOnlyList<SegmentRule>? Rules,
    int? ExpectedVersion)
{
    public UpdateSegmentCommand ToCommand(string key) =>
        new(key, Name, Description, Included ?? [], Excluded ?? [], Rules ?? [], ExpectedVersion);
}

public sealed record SegmentResponse(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<SegmentRule> Rules,
    int Version,
    DateTimeOffset UpdatedAt)
{
    public static SegmentResponse From(Segment segment) => new(
        segment.Key,
        segment.Name,
        segment.Description,
        segment.Included,
        segment.Excluded,
        segment.Rules,
        segment.Version,
        segment.UpdatedAt);
}
