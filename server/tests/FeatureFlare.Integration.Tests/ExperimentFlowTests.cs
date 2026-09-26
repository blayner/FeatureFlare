using System.Net;

namespace FeatureFlare.Integration.Tests;

public sealed class ExperimentFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _admin = factory.CreateClient(ApiFactory.AdminKey);
    private readonly HttpClient _sdk = factory.CreateClient(ApiFactory.SdkKey);

    [Fact]
    public async Task Exposures_and_conversions_produce_results_per_variant()
    {
        (await _admin.PostJson("/api/experiments", new
        {
            key = "pricing-page",
            name = "Pricing page",
            hypothesis = "Annual-first pricing increases upgrades",
            variants = new object[] { new { key = "control", weight = 5000 }, new { key = "annual-first", weight = 5000 } },
            controlVariant = "control",
            primaryMetric = "upgrade",
        })).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await _admin.PostAsync("/api/experiments/pricing-page/start", null, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var users = Enumerable.Range(0, 400).Select(i => $"user-{i}").ToList();
        var events = users
            .Select(u => (object)new { kind = "exposure", experimentKey = "pricing-page", userId = u })
            .Concat(users.Where((_, i) => i % 4 == 0)
                .Select(u => (object)new { kind = "conversion", experimentKey = "pricing-page", userId = u, value = 99.9m }))
            .ToList();

        var ingest = await _sdk.PostJson("/api/events", new { events });
        ingest.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var ingestion = await ingest.ReadJson();
        ingestion.GetProperty("accepted").GetInt32().ShouldBe(500);
        ingestion.GetProperty("rejected").GetInt32().ShouldBe(0);

        var results = await (await _admin.GetAsync("/api/experiments/pricing-page/results", TestContext.Current.CancellationToken)).ReadJson();
        var variants = results.GetProperty("variants").EnumerateArray().ToList();

        variants.Count.ShouldBe(2);
        variants.Sum(v => v.GetProperty("exposedUsers").GetInt64()).ShouldBe(400);
        variants.Sum(v => v.GetProperty("convertedUsers").GetInt64()).ShouldBe(100);
        variants.Single(v => v.GetProperty("isControl").GetBoolean()).GetProperty("versusControl").ValueKind
            .ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Events_for_unknown_or_draft_experiments_are_rejected_individually()
    {
        await _admin.PostJson("/api/experiments", new
        {
            key = "still-draft",
            name = "Draft",
            variants = new object[] { new { key = "a", weight = 5000 }, new { key = "b", weight = 5000 } },
            controlVariant = "a",
            primaryMetric = "click",
        });

        var response = await _sdk.PostJson("/api/events", new
        {
            events = new object[]
            {
                new { kind = "exposure", experimentKey = "still-draft", userId = "u1" },
                new { kind = "exposure", experimentKey = "ghost", userId = "u1" },
            },
        });

        var body = await response.ReadJson();
        body.GetProperty("accepted").GetInt32().ShouldBe(0);
        body.GetProperty("rejected").GetInt32().ShouldBe(2);
        body.GetProperty("errors").GetArrayLength().ShouldBe(2);
    }
}
