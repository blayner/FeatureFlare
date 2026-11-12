using System.Net;
using System.Text;
using FeatureFlare.Sdk.Evaluation;
using FeatureFlare.Sdk.Models;
using FeatureFlare.Sdk.Streaming;
using Microsoft.Extensions.Options;

namespace FeatureFlare.Sdk.Tests;

public sealed class ClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(string Path, string Body)> Posts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/api/sdk/config")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EvaluatorTests.ConfigJson, Encoding.UTF8, "application/json"),
                };
            }

            if (request.Method == HttpMethod.Post)
            {
                Posts.Add((path, await request.Content!.ReadAsStringAsync(cancellationToken)));
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static (FeatureFlareClient Client, StubHandler Handler) CreateClient(bool trackExposures = true)
    {
        var handler = new StubHandler();
        var options = Options.Create(new FeatureFlareOptions
        {
            ApiKey = "sdk-test",
            BaseUrl = "http://featureflare.test",
            EnableStreaming = false,
            TrackExposures = trackExposures,
        });

        var client = new FeatureFlareClient(new HttpClient(handler), new HttpClient(handler), options);
        return (client, handler);
    }

    [Fact]
    public async Task Initialize_downloads_config_and_evaluates_locally()
    {
        var (client, _) = CreateClient();
        await using var _client = client;

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.IsReady.ShouldBeTrue();
        client.GetBooleanValue("new-ui", false, new EvaluationContext("ana")).ShouldBeTrue();
        client.GetNumberValue("max-items", 0, new EvaluationContext("u1")).ShouldBe(10);
    }

    [Fact]
    public async Task Wrong_type_and_unknown_flags_return_the_default()
    {
        var (client, _) = CreateClient();
        await using var _client = client;
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetStringValue("new-ui", "fallback", new EvaluationContext("ana")).ShouldBe("fallback");
        client.GetBooleanValue("ghost", true, new EvaluationContext("ana")).ShouldBeTrue();
    }

    [Fact]
    public async Task Exposures_are_deduplicated_and_conversions_are_sent_on_flush()
    {
        var (client, handler) = CreateClient();
        await using var _client = client;
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        var user = new EvaluationContext("user-7");

        var variant = client.GetExperimentVariant("checkout", user);
        client.GetExperimentVariant("checkout", user);
        client.TrackConversion("checkout", user, value: 59.90m);
        await client.FlushAsync(TestContext.Current.CancellationToken);

        variant.ShouldNotBeNull();
        var body = handler.Posts.ShouldHaveSingleItem().Body;
        body.Split("\"kind\":\"exposure\"").Length.ShouldBe(2);
        body.ShouldContain("\"kind\":\"conversion\"");
        body.ShouldContain($"\"variant\":\"{variant}\"");
    }

    [Fact]
    public async Task Stream_events_update_the_snapshot_and_ignore_stale_versions()
    {
        var (client, _) = CreateClient();
        await using var _client = client;
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        var changed = new List<string>();
        client.FlagChanged += (key, _) => changed.Add(key);

        const string disabled = """
            {"key":"new-ui","type":"boolean","enabled":false,"salt":"s1","version":4,
             "variations":[{"key":"on","value":true},{"key":"off","value":false}],
             "offVariation":"off","fallthrough":{"variation":"on"},"rules":[]}
            """;
        const string stale = """
            {"key":"new-ui","type":"boolean","enabled":true,"salt":"s1","version":2,
             "variations":[{"key":"on","value":true},{"key":"off","value":false}],
             "offVariation":"off","fallthrough":{"variation":"on"},"rules":[]}
            """;

        client.Apply(new SseMessage("flag.updated", disabled, "1"));
        client.Apply(new SseMessage("flag.updated", stale, "2"));
        client.Apply(new SseMessage("flag.deleted", """{"key":"max-items","version":2}""", "3"));

        client.GetBooleanValue("new-ui", true, new EvaluationContext("ana")).ShouldBeFalse();
        client.GetNumberValue("max-items", -1, new EvaluationContext("u1")).ShouldBe(-1);
        changed.ShouldBe(["new-ui", "max-items"]);
    }

    [Fact]
    public void Collection_initializer_builds_attributes()
    {
        var context = new EvaluationContext("user-123") { { "plan", "pro" }, { "seats", 5 } };

        context.Attributes.Count.ShouldBe(2);
        context.TryGetValues("key", out var key).ShouldBeTrue();
        key.ShouldBe(["user-123"]);
    }
}

public sealed class SseParserTests
{
    [Fact]
    public void Parses_events_ids_multiline_data_comments_and_retry()
    {
        var parser = new SseParser();
        var lines = new[] { "retry: 3000", ": ping", "", "event: flag.updated", "id: 9", "data: {\"a\":", "data: 1}", "" };

        var messages = lines.Select(parser.ProcessLine).Where(m => m is not null).ToList();

        messages.Count.ShouldBe(1);
        messages[0]!.Event.ShouldBe("flag.updated");
        messages[0]!.Data.ShouldBe("{\"a\":\n1}");
        parser.LastEventId.ShouldBe("9");
        parser.RetryMilliseconds.ShouldBe(3000);
    }
}

public sealed class ConfigStoreTests
{
    [Fact]
    public void Replace_all_reports_changed_and_removed_flags()
    {
        var store = new ConfigStore(TimeProvider.System);
        var config = System.Text.Json.JsonSerializer.Deserialize<SdkConfig>(EvaluatorTests.ConfigJson, SdkJson.Options)!;
        store.ReplaceAll(config);

        var changed = store.ReplaceAll(config with { Flags = [config.Flags[0] with { Version = 99 }] });

        changed.ShouldBe(["new-ui", "max-items"], ignoreOrder: true);
        store.IsStale(TimeSpan.FromMinutes(1)).ShouldBeFalse();
    }

    private static SdkConfig Config() =>
        System.Text.Json.JsonSerializer.Deserialize<SdkConfig>(EvaluatorTests.ConfigJson, SdkJson.Options)!;

    [Fact]
    public void Snapshot_does_not_roll_back_newer_streamed_versions_or_resurrect_deletions()
    {
        // The stream and the config download race; the snapshot (new-ui v3, max-items v1) arrives last.
        var store = new ConfigStore(TimeProvider.System);
        var config = Config();
        var since = store.Mark();

        store.Upsert(config.Flags[0] with { Version = 4, Enabled = false }).ShouldBeTrue();
        store.DeleteFlag(new Deletion("max-items", 2)).ShouldBeFalse(); // not known yet, tombstone only
        store.ReplaceAll(config, since);

        store.Current.Flags["new-ui"].Version.ShouldBe(4);
        store.Current.Flags["new-ui"].Enabled.ShouldBeFalse();
        store.Current.Flags.ContainsKey("max-items").ShouldBeFalse();
    }

    [Fact]
    public void Items_created_on_the_stream_after_the_request_survive_an_older_snapshot()
    {
        var store = new ConfigStore(TimeProvider.System);
        var config = Config();
        store.ReplaceAll(config);
        var since = store.Mark();

        store.Upsert(config.Flags[1] with { Key = "created-later", Version = 1 });
        store.ReplaceAll(config, since);

        store.Current.Flags.ContainsKey("created-later").ShouldBeTrue();
    }

    [Fact]
    public void Items_missing_from_a_newer_snapshot_are_removed()
    {
        var store = new ConfigStore(TimeProvider.System);
        var config = Config();
        store.ReplaceAll(config);

        var changed = store.ReplaceAll(config with { Flags = [config.Flags[0]] }, store.Mark());

        store.Current.Flags.ContainsKey("max-items").ShouldBeFalse();
        changed.ShouldBe(["max-items"]);
    }
}

public sealed class InitializationRaceTests
{
    [Fact]
    public async Task Stream_events_applied_before_the_first_config_download_are_kept()
    {
        var handler = new StaticConfigHandler(EvaluatorTests.ConfigJson);
        var options = Options.Create(new FeatureFlareOptions
        {
            ApiKey = "sdk-test",
            BaseUrl = "http://featureflare.test",
            EnableStreaming = false,
        });
        await using var client = new FeatureFlareClient(new HttpClient(handler), new HttpClient(handler), options);

        client.Apply(new SseMessage("flag.updated", """
            {"key":"new-ui","type":"boolean","enabled":false,"salt":"s1","version":4,
             "variations":[{"key":"on","value":true},{"key":"off","value":false}],
             "offVariation":"off","fallthrough":{"variation":"on"},"rules":[]}
            """, "1"));
        client.Apply(new SseMessage("flag.deleted", """{"key":"max-items","version":2}""", "2"));
        await client.InitializeAsync(TestContext.Current.CancellationToken);

        client.GetBooleanValue("new-ui", true, new EvaluationContext("ana")).ShouldBeFalse();
        client.GetNumberValue("max-items", -1, new EvaluationContext("u1")).ShouldBe(-1);
    }

    private sealed class StaticConfigHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/api/sdk/config"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.Accepted));
    }
}
