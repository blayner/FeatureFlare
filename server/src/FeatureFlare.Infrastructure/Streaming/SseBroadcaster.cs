using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace FeatureFlare.Infrastructure.Streaming;

/// <summary>A server-sent event. <c>Event == null</c> with a comment produces a keep-alive line.</summary>
public sealed record SseMessage(string? Event, string Data, string? Id = null)
{
    public static readonly SseMessage Heartbeat = new(null, string.Empty);

    public bool IsHeartbeat => Event is null;
}

public interface ISseBroadcaster
{
    int SubscriberCount { get; }

    SseSubscription Subscribe();

    /// <summary>Fan-out to every subscriber. Never blocks on slow consumers.</summary>
    void Publish(SseMessage message);
}

public sealed class SseSubscription(Guid id, ChannelReader<SseMessage> reader, Action<Guid> unsubscribe) : IDisposable
{
    public Guid Id { get; } = id;

    public ChannelReader<SseMessage> Reader { get; } = reader;

    public void Dispose() => unsubscribe(Id);
}

/// <summary>
/// In-process fan-out of change events to SSE connections. Each subscriber gets a bounded channel; if a
/// client can't keep up its channel is completed (the connection is closed) instead of dropping individual
/// messages. SDKs re-download the full config on reconnect, so a disconnect never leaves them inconsistent,
/// whereas a silently dropped "flag.updated" would.
/// </summary>
/// <remarks>
/// Single-node by design. Running several API replicas requires a backplane (e.g. Postgres LISTEN/NOTIFY
/// or Redis pub/sub) feeding <see cref="Publish"/> on every node — see ADR-0001 in the README.
/// </remarks>
public sealed class SseBroadcaster(ILogger<SseBroadcaster> logger) : ISseBroadcaster
{
    public const int SubscriberBufferSize = 256;

    private readonly ConcurrentDictionary<Guid, Channel<SseMessage>> _subscribers = new();
    private long _sequence;

    public int SubscriberCount => _subscribers.Count;

    public SseSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<SseMessage>(new BoundedChannelOptions(SubscriberBufferSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        logger.LogDebug("SSE subscriber {SubscriberId} connected ({Count} total)", id, _subscribers.Count);
        return new SseSubscription(id, channel.Reader, Unsubscribe);
    }

    public void Publish(SseMessage message)
    {
        var stamped = message.IsHeartbeat
            ? message
            : message with { Id = Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture) };

        foreach (var (id, channel) in _subscribers)
        {
            if (channel.Writer.TryWrite(stamped))
            {
                continue;
            }

            // Buffer full: the consumer is too slow (or the TCP connection is stuck). Disconnect it.
            logger.LogWarning("SSE subscriber {SubscriberId} is too slow; disconnecting", id);
            if (_subscribers.TryRemove(id, out var removed))
            {
                removed.Writer.TryComplete(new SlowSubscriberException());
            }
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel))
        {
            channel.Writer.TryComplete();
            logger.LogDebug("SSE subscriber {SubscriberId} disconnected ({Count} remaining)", id, _subscribers.Count);
        }
    }
}

public sealed class SlowSubscriberException() : Exception("Subscriber could not keep up with the event stream.");
