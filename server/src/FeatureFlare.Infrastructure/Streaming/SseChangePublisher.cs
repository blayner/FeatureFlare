using System.Text.Json;
using FeatureFlare.Application.Abstractions;
using FeatureFlare.Application.Sdk;
using FeatureFlare.Application.Serialization;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Infrastructure.Streaming;

/// <summary>
/// Translates configuration changes into SSE events. Event names are part of the SDK contract:
/// <c>flag.updated</c>, <c>flag.deleted</c>, <c>segment.updated</c>, <c>segment.deleted</c>.
/// </summary>
public sealed class SseChangePublisher(ISseBroadcaster broadcaster) : IChangePublisher
{
    public static class Events
    {
        public const string FlagUpdated = "flag.updated";
        public const string FlagDeleted = "flag.deleted";
        public const string SegmentUpdated = "segment.updated";
        public const string SegmentDeleted = "segment.deleted";
    }

    public ValueTask FlagUpdatedAsync(FeatureFlag flag, CancellationToken ct = default) =>
        Publish(Events.FlagUpdated, flag.ToSdk());

    public ValueTask FlagDeletedAsync(string key, int version, CancellationToken ct = default) =>
        Publish(Events.FlagDeleted, new SdkDeletion(key, version));

    public ValueTask SegmentUpdatedAsync(Segment segment, CancellationToken ct = default) =>
        Publish(Events.SegmentUpdated, segment.ToSdk());

    public ValueTask SegmentDeletedAsync(string key, int version, CancellationToken ct = default) =>
        Publish(Events.SegmentDeleted, new SdkDeletion(key, version));

    private ValueTask Publish<T>(string eventName, T payload)
    {
        var data = JsonSerializer.Serialize(payload, FeatureFlareJson.Options);
        broadcaster.Publish(new SseMessage(eventName, data));
        return ValueTask.CompletedTask;
    }
}
