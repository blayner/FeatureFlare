using FeatureFlare.Application.Abstractions;

namespace FeatureFlare.Application.Sdk;

public sealed class SdkConfigService(
    IFlagRepository flags,
    ISegmentRepository segments,
    IExperimentRepository experiments,
    TimeProvider clock)
{
    public async Task<SdkConfig> GetAsync(CancellationToken ct = default)
    {
        var allFlags = await flags.ListAsync(ct);
        var allSegments = await segments.ListAsync(ct);
        var allExperiments = await experiments.ListAsync(ct);

        return new SdkConfig(
            allFlags.Select(f => f.ToSdk()).ToList(),
            allSegments.Select(s => s.ToSdk()).ToList(),
            allExperiments.Select(e => e.ToSdk()).ToList(),
            clock.GetUtcNow());
    }
}
