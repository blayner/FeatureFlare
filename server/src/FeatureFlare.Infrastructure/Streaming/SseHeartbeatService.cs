using Microsoft.Extensions.Hosting;

namespace FeatureFlare.Infrastructure.Streaming;

/// <summary>
/// Sends a comment line to every SSE connection periodically. Keeps idle connections alive through
/// proxies/load balancers and lets the server detect dead clients (the write fails).
/// </summary>
public sealed class SseHeartbeatService(ISseBroadcaster broadcaster, TimeProvider clock) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (broadcaster.SubscriberCount > 0)
                {
                    broadcaster.Publish(SseMessage.Heartbeat);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }
}
