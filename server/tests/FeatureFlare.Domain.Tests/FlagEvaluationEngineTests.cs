using FeatureFlare.Application.Evaluation;
using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Domain.Tests;

public sealed class FlagEvaluationEngineTests
{
    private readonly FlagEvaluationEngine _engine = new();

    [Fact]
    public void Disabled_flag_serves_off_variation()
    {
        var flag = TestData.BooleanFlag(enabled: false, fallthrough: Serve.Fixed("on"));

        var result = _engine.Evaluate(flag, TestData.Context("u1"));

        result.Reason.ShouldBe(EvaluationReason.Off);
        result.VariationKey.ShouldBe("off");
        result.Value.ShouldBe("false");
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var flag = TestData.BooleanFlag(rules:
        [
            TestData.Rule("pro-users", "on", TestData.Clause("plan", ClauseOperator.In, "pro", "enterprise")),
            TestData.Rule("brazil", "off", TestData.Clause("country", ClauseOperator.In, "BR")),
        ]);

        var result = _engine.Evaluate(flag, TestData.Context("u1", ("plan", "pro"), ("country", "BR")));

        result.Reason.ShouldBe(EvaluationReason.RuleMatch);
        result.RuleId.ShouldBe("pro-users");
        result.VariationKey.ShouldBe("on");
    }

    [Fact]
    public void All_clauses_of_a_rule_must_match()
    {
        var flag = TestData.BooleanFlag(rules:
        [
            new TargetingRule("r", [
                TestData.Clause("plan", ClauseOperator.In, "pro"),
                TestData.Clause("app-version", ClauseOperator.SemVerGreaterThan, "2.0.0"),
            ], Serve.Fixed("on")),
        ]);

        _engine.Evaluate(flag, TestData.Context("u1", ("plan", "pro"), ("app-version", "1.9.9"))).Reason
            .ShouldBe(EvaluationReason.Fallthrough);
        _engine.Evaluate(flag, TestData.Context("u1", ("plan", "pro"), ("app-version", "v2.1"))).Reason
            .ShouldBe(EvaluationReason.RuleMatch);
    }

    [Fact]
    public void Percentage_rollout_is_deterministic_and_close_to_the_configured_split()
    {
        var flag = TestData.BooleanFlag(fallthrough: Serve.Split(new WeightedVariation("on", 2_000), new WeightedVariation("off", 8_000)));

        var first = Enumerable.Range(0, 20_000).Select(i => _engine.Evaluate(flag, TestData.Context($"user-{i}")).VariationKey).ToList();
        var second = Enumerable.Range(0, 20_000).Select(i => _engine.Evaluate(flag, TestData.Context($"user-{i}")).VariationKey).ToList();

        second.ShouldBe(first);
        var onShare = first.Count(v => v == "on") / 20_000.0;
        onShare.ShouldBeInRange(0.18, 0.22);
    }

    [Fact]
    public void Rollout_can_bucket_by_another_attribute()
    {
        var flag = TestData.BooleanFlag(fallthrough: new Serve(null, new Rollout(
            [new WeightedVariation("on", 5_000), new WeightedVariation("off", 5_000)], BucketBy: "companyId")));

        // Every user of the same company gets the same variation.
        var variations = Enumerable.Range(0, 50)
            .Select(i => _engine.Evaluate(flag, TestData.Context($"user-{i}", ("companyId", "acme"))).VariationKey)
            .Distinct()
            .ToList();

        variations.Count.ShouldBe(1);
    }

    [Fact]
    public void In_segment_rule_uses_segment_membership()
    {
        var segment = Segment.Create("beta", "Beta testers", null,
            included: ["vip-1"],
            excluded: ["banned-1"],
            rules: [new SegmentRule([TestData.Clause("email", ClauseOperator.EndsWith, "@featureflare.dev")])]);
        var segments = new Dictionary<string, Segment> { ["beta"] = segment };
        var flag = TestData.BooleanFlag(rules: [TestData.Rule("beta", "on", Clause.InSegments("beta"))]);

        _engine.Evaluate(flag, TestData.Context("vip-1"), segments).VariationKey.ShouldBe("on");
        _engine.Evaluate(flag, TestData.Context("someone", ("email", "ana@featureflare.dev")), segments).VariationKey.ShouldBe("on");
        _engine.Evaluate(flag, TestData.Context("banned-1", ("email", "x@featureflare.dev")), segments).VariationKey.ShouldBe("off");
        _engine.Evaluate(flag, TestData.Context("other"), segments).Reason.ShouldBe(EvaluationReason.Fallthrough);
    }

    [Fact]
    public void Unknown_segment_never_matches()
    {
        var flag = TestData.BooleanFlag(rules: [TestData.Rule("beta", "on", Clause.InSegments("ghost"))]);

        _engine.Evaluate(flag, TestData.Context("u1")).Reason.ShouldBe(EvaluationReason.Fallthrough);
    }
}
