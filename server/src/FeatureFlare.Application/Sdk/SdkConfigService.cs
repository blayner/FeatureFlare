using FeatureFlare.Application.Abstractions;

namespace FeatureFlare.Application.Sdk;

public sealed class SdkConfigService(IFlagRepository flags, ISegmentRepository segments, TimeProvider clock)
{
    public async Task<SdkConfig> GetAsync(CancellationToken ct = default)
    {
        var allFlags = await flags.ListAsync(ct);
        var allSegments = await segments.ListAsync(ct);

        return new SdkConfig(
            allFlags.Select(f => f.ToSdk()).ToList(),
            allSegments.Select(s => s.ToSdk()).ToList(),
            clock.GetUtcNow());
    }
}
