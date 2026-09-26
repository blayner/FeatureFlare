using System.Net;

namespace FeatureFlare.Integration.Tests;

public sealed class FlagsApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _admin = factory.CreateClient(ApiFactory.AdminKey);
    private readonly HttpClient _sdk = factory.CreateClient(ApiFactory.SdkKey);

    private static object NewCheckoutFlag(string key) => new
    {
        key,
        name = "New checkout",
        type = "boolean",
        variations = new object[] { new { key = "on", value = true }, new { key = "off", value = false } },
        offVariation = "off",
        fallthrough = new { variation = "off" },
        rules = new object[]
        {
            new
            {
                id = "pro",
                clauses = new object[] { new { attribute = "plan", @operator = "in", values = new[] { "pro" } } },
                serve = new { variation = "on" },
            },
        },
        enabled = true,
    };

    [Fact]
    public async Task Requests_without_key_are_rejected()
    {
        var response = await factory.CreateClient(apiKey: null).GetAsync("/api/flags", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Sdk_keys_cannot_manage_flags()
    {
        var response = await _sdk.PostJson("/api/flags", NewCheckoutFlag("sdk-cannot-create"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Created_flag_is_evaluated_with_targeting_rules()
    {
        (await _admin.PostJson("/api/flags", NewCheckoutFlag("checkout-v2"))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var pro = await (await _sdk.PostJson("/api/evaluate/checkout-v2", new { key = "u1", attributes = new { plan = "pro" } })).ReadJson();
        var free = await (await _sdk.PostJson("/api/evaluate/checkout-v2", new { key = "u2", attributes = new { plan = "free" } })).ReadJson();

        pro.GetProperty("value").GetBoolean().ShouldBeTrue();
        pro.GetProperty("reason").GetString().ShouldBe("ruleMatch");
        free.GetProperty("value").GetBoolean().ShouldBeFalse();
        free.GetProperty("reason").GetString().ShouldBe("fallthrough");
    }

    [Fact]
    public async Task Duplicate_keys_conflict()
    {
        await _admin.PostJson("/api/flags", NewCheckoutFlag("dup-flag"));

        var response = await _admin.PostJson("/api/flags", NewCheckoutFlag("dup-flag"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invalid_configuration_returns_problem_details()
    {
        var response = await _admin.PostJson("/api/flags", new
        {
            key = "broken",
            name = "Broken",
            type = "boolean",
            variations = new object[] { new { key = "on", value = true }, new { key = "off", value = false } },
            offVariation = "off",
            fallthrough = new { variation = "nope" },
        });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadJson()).GetProperty("detail").GetString()!.ShouldContain("nope");
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected()
    {
        await _admin.PostJson("/api/flags", NewCheckoutFlag("versioned"));

        var response = await _admin.PatchAsync(
            "/api/flags/versioned/toggle",
            System.Net.Http.Json.JsonContent.Create(new { enabled = false, expectedVersion = 1 }),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task Sdk_config_contains_flags_with_raw_json_values()
    {
        await _admin.PostJson("/api/flags", NewCheckoutFlag("in-config"));

        var config = await (await _sdk.GetAsync("/api/sdk/config", TestContext.Current.CancellationToken)).ReadJson();
        var flag = config.GetProperty("flags").EnumerateArray().Single(f => f.GetProperty("key").GetString() == "in-config");

        flag.GetProperty("variations")[0].GetProperty("value").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.True);
        flag.GetProperty("salt").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Segments_in_use_cannot_be_deleted()
    {
        (await _admin.PostJson("/api/segments", new { key = "staff", name = "Staff", included = new[] { "u-staff" } }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await _admin.PostJson("/api/flags", new
        {
            key = "staff-only",
            name = "Staff only",
            type = "boolean",
            variations = new object[] { new { key = "on", value = true }, new { key = "off", value = false } },
            offVariation = "off",
            fallthrough = new { variation = "off" },
            rules = new object[]
            {
                new
                {
                    id = "staff",
                    clauses = new object[] { new { attribute = "key", @operator = "inSegment", values = new[] { "staff" } } },
                    serve = new { variation = "on" },
                },
            },
            enabled = true,
        });

        var evaluation = await (await _sdk.PostJson("/api/evaluate/staff-only", new { key = "u-staff" })).ReadJson();
        evaluation.GetProperty("value").GetBoolean().ShouldBeTrue();

        var delete = await _admin.DeleteAsync("/api/segments/staff", TestContext.Current.CancellationToken);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
