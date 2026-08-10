using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Common;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Application.Flags;

public sealed class FlagService(
    IFlagRepository flags,
    ISegmentRepository segments,
    IUnitOfWork unitOfWork,
    IChangePublisher publisher)
{
    public Task<IReadOnlyList<FeatureFlag>> ListAsync(CancellationToken ct = default) => flags.ListAsync(ct);

    public async Task<FeatureFlag> GetAsync(string key, CancellationToken ct = default) =>
        await flags.GetByKeyAsync(key, ct) ?? throw new NotFoundException("Flag", key);

    public async Task<FeatureFlag> CreateAsync(CreateFlagCommand command, CancellationToken ct = default)
    {
        if (await flags.ExistsAsync(command.Key, ct))
        {
            throw new ConflictException($"Flag '{command.Key}' already exists.");
        }

        var rules = command.Rules ?? [];
        await EnsureSegmentsExistAsync(command.Key, rules, ct);

        var flag = FeatureFlag.Create(
            command.Key,
            command.Name,
            command.Description,
            command.Type,
            command.Variations,
            command.OffVariation,
            command.Fallthrough,
            rules);

        if (command.Enabled)
        {
            flag.SetEnabled(true);
        }

        flags.Add(flag);
        await unitOfWork.SaveChangesAsync(ct);
        await publisher.FlagUpdatedAsync(flag, ct);
        return flag;
    }

    public async Task<FeatureFlag> UpdateAsync(UpdateFlagCommand command, CancellationToken ct = default)
    {
        var flag = await GetAsync(command.Key, ct);
        EnsureVersion(flag, command.ExpectedVersion);

        var rules = command.Rules ?? [];
        await EnsureSegmentsExistAsync(flag.Key, rules, ct);

        flag.Reconfigure(
            command.Name,
            command.Description,
            command.Variations,
            command.OffVariation,
            command.Fallthrough,
            rules);

        await unitOfWork.SaveChangesAsync(ct);
        await publisher.FlagUpdatedAsync(flag, ct);
        return flag;
    }

    public async Task<FeatureFlag> ToggleAsync(ToggleFlagCommand command, CancellationToken ct = default)
    {
        var flag = await GetAsync(command.Key, ct);
        EnsureVersion(flag, command.ExpectedVersion);

        flag.SetEnabled(command.Enabled);
        await unitOfWork.SaveChangesAsync(ct);
        await publisher.FlagUpdatedAsync(flag, ct);
        return flag;
    }

    public async Task DeleteAsync(DeleteFlagCommand command, CancellationToken ct = default)
    {
        var flag = await GetAsync(command.Key, ct);
        flags.Remove(flag);
        await unitOfWork.SaveChangesAsync(ct);
        await publisher.FlagDeletedAsync(flag.Key, flag.Version + 1, ct);
    }

    private async Task EnsureSegmentsExistAsync(string flagKey, IEnumerable<TargetingRule> rules, CancellationToken ct)
    {
        var referenced = FeatureFlag.ReferencedSegments(rules.Where(r => r.Clauses is not null));
        if (referenced.Count == 0)
        {
            return;
        }

        var missing = await segments.FindMissingKeysAsync(referenced, ct);
        if (missing.Count > 0)
        {
            throw new ConflictException($"Flag '{flagKey}' references unknown segments: {string.Join(", ", missing)}.");
        }
    }

    private static void EnsureVersion(FeatureFlag flag, int? expected)
    {
        if (expected is not null && expected != flag.Version)
        {
            throw new ConcurrencyException("Flag", flag.Key, expected, flag.Version);
        }
    }
}
