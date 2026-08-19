using FeatureFlare.Application.Serialization;
using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Sdk;

public static class SdkConfigMapper
{
    public static SdkFlagConfig ToSdk(this FeatureFlag flag) => new(
        flag.Key,
        flag.Type,
        flag.Enabled,
        flag.Salt,
        flag.Variations.Select(v => new SdkVariation(v.Key, RawJson.ToElement(v.Value))).ToList(),
        flag.OffVariation,
        flag.Fallthrough,
        flag.Rules,
        flag.Version);

    public static SdkSegmentConfig ToSdk(this Segment segment) => new(
        segment.Key,
        segment.Included,
        segment.Excluded,
        segment.Rules,
        segment.Version);

    public static SdkExperimentConfig ToSdk(this Experiment experiment) => new(
        experiment.Key,
        experiment.Status,
        experiment.TrafficAllocation,
        experiment.Salt,
        experiment.ControlVariant,
        experiment.PrimaryMetric,
        experiment.Variants
            .Select(v => new SdkExperimentVariant(v.Key, v.Weight, RawJson.ToElementOrNull(v.Payload)))
            .ToList(),
        experiment.Version);
}
