using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Abstractions;

/// <summary>
/// Notifies connected SDKs about configuration changes. Called <b>after</b> a successful commit, so
/// subscribers never observe changes that were rolled back.
/// </summary>
public interface IChangePublisher
{
    ValueTask FlagUpdatedAsync(FeatureFlag flag, CancellationToken ct = default);

    ValueTask FlagDeletedAsync(string key, int version, CancellationToken ct = default);

    ValueTask SegmentUpdatedAsync(Segment segment, CancellationToken ct = default);

    ValueTask SegmentDeletedAsync(string key, int version, CancellationToken ct = default);
}
