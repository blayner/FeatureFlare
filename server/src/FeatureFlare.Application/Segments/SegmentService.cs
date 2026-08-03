using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Common;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Segments;

public sealed record CreateSegmentCommand(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<string>? Included,
    IReadOnlyList<string>? Excluded,
    IReadOnlyList<SegmentRule>? Rules);

public sealed record UpdateSegmentCommand(
    string Key,
    string Name,
    string? Description,
    IReadOnlyList<string> Included,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<SegmentRule> Rules,
    int? ExpectedVersion);

public sealed class SegmentService(
    ISegmentRepository segments,
    IFlagRepository flags,
    IUnitOfWork unitOfWork)
{
    public Task<IReadOnlyList<Segment>> ListAsync(CancellationToken ct = default) => segments.ListAsync(ct);

    public async Task<Segment> GetAsync(string key, CancellationToken ct = default) =>
        await segments.GetByKeyAsync(key, ct) ?? throw new NotFoundException("Segment", key);

    public async Task<Segment> CreateAsync(CreateSegmentCommand command, CancellationToken ct = default)
    {
        if (await segments.GetByKeyAsync(command.Key, ct) is not null)
        {
            throw new ConflictException($"Segment '{command.Key}' already exists.");
        }

        var segment = Segment.Create(
            command.Key, command.Name, command.Description, command.Included, command.Excluded, command.Rules);

        segments.Add(segment);
        await unitOfWork.SaveChangesAsync(ct);
        return segment;
    }

    public async Task<Segment> UpdateAsync(UpdateSegmentCommand command, CancellationToken ct = default)
    {
        var segment = await GetAsync(command.Key, ct);
        if (command.ExpectedVersion is not null && command.ExpectedVersion != segment.Version)
        {
            throw new ConcurrencyException("Segment", segment.Key, command.ExpectedVersion, segment.Version);
        }

        segment.Update(command.Name, command.Description, command.Included, command.Excluded, command.Rules);
        await unitOfWork.SaveChangesAsync(ct);
        return segment;
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var segment = await GetAsync(key, ct);

        // Deleting a referenced segment would silently change who sees what; force the user to update
        // the flags first.
        var usedBy = (await flags.ListAsync(ct))
            .Where(f => f.ReferencedSegments().Contains(key))
            .Select(f => f.Key)
            .ToList();

        if (usedBy.Count > 0)
        {
            throw new ConflictException($"Segment '{key}' is used by flags: {string.Join(", ", usedBy)}.");
        }

        segments.Remove(segment);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
