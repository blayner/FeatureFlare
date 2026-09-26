using FeatureFlare.Domain.Common;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Domain.Tests;

public sealed class FeatureFlagTests
{
    [Fact]
    public void New_flags_start_disabled_with_version_1()
    {
        var flag = FeatureFlag.CreateBoolean("dark-mode", "Dark mode");

        flag.Enabled.ShouldBeFalse();
        flag.Version.ShouldBe(1);
        flag.Salt.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Dark-Mode")]
    [InlineData("-dark")]
    [InlineData("dark mode")]
    [InlineData("")]
    public void Rejects_invalid_keys(string key) =>
        Should.Throw<DomainException>(() => FeatureFlag.CreateBoolean(key, "x"));

    [Fact]
    public void Boolean_flags_require_true_and_false_variations()
    {
        Should.Throw<DomainException>(() => FeatureFlag.Create(
            "f", "F", null, FlagType.Boolean,
            [new Variation("a", "true"), new Variation("b", "true")],
            "a", Serve.Fixed("a")));
    }

    [Fact]
    public void Variation_values_must_match_the_flag_type()
    {
        var ex = Should.Throw<DomainException>(() => FeatureFlag.Create(
            "limit", "Limit", null, FlagType.Number,
            [new Variation("low", "10"), new Variation("high", "\"100\"")],
            "low", Serve.Fixed("low")));

        ex.Message.ShouldContain("high");
    }

    [Fact]
    public void Rollout_weights_must_sum_to_100_percent()
    {
        Should.Throw<DomainException>(() => TestData.BooleanFlag(
            fallthrough: Serve.Split(new WeightedVariation("on", 5_000), new WeightedVariation("off", 4_000))));
    }

    [Fact]
    public void Serve_cannot_reference_unknown_variations()
    {
        Should.Throw<DomainException>(() => TestData.BooleanFlag(rules: [TestData.Rule("r1", "maybe", TestData.Clause("plan", ClauseOperator.In, "pro"))]));
    }

    [Fact]
    public void Invalid_regex_is_rejected_at_configuration_time()
    {
        Should.Throw<DomainException>(() => TestData.BooleanFlag(rules: [TestData.Rule("r1", "on", TestData.Clause("email", ClauseOperator.Matches, "(unclosed"))]));
    }

    [Fact]
    public void Rejected_reconfiguration_leaves_the_flag_untouched()
    {
        var flag = TestData.BooleanFlag();
        var version = flag.Version;

        Should.Throw<DomainException>(() => flag.Reconfigure(
            "Renamed", null,
            [new Variation("on", "true"), new Variation("off", "false")],
            "off",
            Serve.Fixed("missing"),
            []));

        flag.Name.ShouldBe("New checkout");
        flag.Fallthrough.ShouldBe(Serve.Fixed("off"));
        flag.Version.ShouldBe(version);
    }

    [Fact]
    public void Toggling_to_the_same_state_does_not_bump_the_version()
    {
        var flag = TestData.BooleanFlag(enabled: true);
        var version = flag.Version;

        flag.SetEnabled(true);

        flag.Version.ShouldBe(version);
    }

    [Fact]
    public void Referenced_segments_are_collected_from_all_rules()
    {
        var flag = TestData.BooleanFlag(rules:
        [
            TestData.Rule("r1", "on", Clause.InSegments("beta", "staff")),
            TestData.Rule("r2", "on", Clause.InSegments("beta")),
        ]);

        flag.ReferencedSegments().ShouldBe(["beta", "staff"], ignoreOrder: true);
    }
}
