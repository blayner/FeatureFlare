using FeatureFlare.Application.Serialization;
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
}
