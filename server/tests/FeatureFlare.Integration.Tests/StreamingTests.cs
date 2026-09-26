using System.Net;
using FeatureFlare.Api.Controllers;
using FeatureFlare.Infrastructure.Streaming;
using Microsoft.Extensions.Logging.Abstractions;

namespace FeatureFlare.Integration.Tests;

public sealed class SseBroadcasterTests
{
    [Fact]
    public async Task Published_messages_reach_every_subscriber_with_increasing_ids()
    {
        var broadcaster = new SseBroadcaster(NullLogger<SseBroadcaster>.Instance);
        using var a = broadcaster.Subscribe();
        using var b = broadcaster.Subscribe();

        broadcaster.Publish(new SseMessage("flag.updated", "{}"));
        broadcaster.Publish(new SseMessage("flag.deleted", "{}"));

        var ct = TestContext.Current.CancellationToken;
        var first = await a.Reader.ReadAsync(ct);
        var second = await a.Reader.ReadAsync(ct);
        (await b.Reader.ReadAsync(ct)).Event.ShouldBe("flag.updated");

        first.Id.ShouldBe("1");
        second.Id.ShouldBe("2");
    }

    [Fact]
    public async Task Slow_subscribers_are_disconnected_instead_of_losing_messages()
    {
        var broadcaster = new SseBroadcaster(NullLogger<SseBroadcaster>.Instance);
        using var slow = broadcaster.Subscribe();

        for (var i = 0; i <= SseBroadcaster.SubscriberBufferSize; i++)
        {
            broadcaster.Publish(new SseMessage("flag.updated", "{}"));
        }

        broadcaster.SubscriberCount.ShouldBe(0);

        // Everything that was buffered is still delivered, then the stream ends with an error so the
        // client reconnects and re-syncs instead of silently missing the overflowing message.
        var delivered = 0;
        await Should.ThrowAsync<SlowSubscriberException>(async () =>
        {
            await foreach (var _ in slow.Reader.ReadAllAsync(TestContext.Current.CancellationToken))
            {
                delivered++;
            }
        });

        delivered.ShouldBe(SseBroadcaster.SubscriberBufferSize);
    }

    [Fact]
    public void Disposing_a_subscription_unsubscribes()
    {
        var broadcaster = new SseBroadcaster(NullLogger<SseBroadcaster>.Instance);
        var subscription = broadcaster.Subscribe();

        subscription.Dispose();

        broadcaster.SubscriberCount.ShouldBe(0);
    }
}

public sealed class StreamEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Multi_line_payloads_are_split_into_data_fields()
    {
        var text = StreamController.Format(new SseMessage("flag.updated", "line1\nline2", "7"));

        text.ShouldBe("event: flag.updated\nid: 7\ndata: line1\ndata: line2\n\n");
    }

    [Fact]
    public void Heartbeats_are_comments()
    {
        StreamController.Format(SseMessage.Heartbeat).ShouldBe(": ping\n\n");
    }

    [Fact]
    public async Task Stream_accepts_sdk_key_in_query_string_but_not_admin_key()
    {
        using var client = factory.CreateClient(apiKey: null);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        using var sdkRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/stream?sdkKey={ApiFactory.SdkKey}");
        using var sdkResponse = await client.SendAsync(sdkRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        sdkResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        sdkResponse.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");

        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/stream?sdkKey={ApiFactory.AdminKey}");
        using var adminResponse = await client.SendAsync(adminRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
