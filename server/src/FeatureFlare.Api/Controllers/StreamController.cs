using System.Text;
using FeatureFlare.Api.Auth;
using FeatureFlare.Infrastructure.Streaming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFlare.Api.Controllers;

/// <summary>
/// Server-Sent Events stream of configuration changes.
/// <code>
/// retry: 3000
///
/// event: flag.updated
/// id: 42
/// data: {"key":"new-ui","enabled":true,...}
/// </code>
/// </summary>
[ApiController]
[Route("api/stream")]
[Authorize(Policy = Policies.Sdk)]
public sealed class StreamController(ISseBroadcaster broadcaster, ILogger<StreamController> logger) : ControllerBase
{
    private const int ReconnectDelayMs = 3000;

    [HttpGet]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx buffering
        HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var subscription = broadcaster.Subscribe();

        await WriteAsync($"retry: {ReconnectDelayMs}\n: connected\n\n", ct);

        try
        {
            await foreach (var message in subscription.Reader.ReadAllAsync(ct))
            {
                await WriteAsync(Format(message), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client went away.
        }
        catch (Exception ex) when (ex is SlowSubscriberException or System.Threading.Channels.ChannelClosedException)
        {
            logger.LogInformation("Closing SSE stream {SubscriptionId}: subscriber too slow", subscription.Id);
        }
    }

    internal static string Format(SseMessage message)
    {
        if (message.IsHeartbeat)
        {
            return ": ping\n\n";
        }

        var builder = new StringBuilder();
        builder.Append("event: ").Append(message.Event).Append('\n');
        if (message.Id is not null)
        {
            builder.Append("id: ").Append(message.Id).Append('\n');
        }

        // Per the SSE spec, multi-line payloads must be split into several data: fields.
        foreach (var line in message.Data.Split('\n'))
        {
            builder.Append("data: ").Append(line).Append('\n');
        }

        return builder.Append('\n').ToString();
    }

    private async Task WriteAsync(string text, CancellationToken ct)
    {
        await Response.WriteAsync(text, ct);
        await Response.Body.FlushAsync(ct);
    }
}
