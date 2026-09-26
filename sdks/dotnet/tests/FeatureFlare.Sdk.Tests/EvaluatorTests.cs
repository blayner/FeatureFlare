using System.Text.Json;
using FeatureFlare.Sdk.Evaluation;
using FeatureFlare.Sdk.Models;

namespace FeatureFlare.Sdk.Tests;

public sealed class EvaluatorTests
{
    // Payload shaped exactly like GET /api/sdk/config.
    internal const string ConfigJson = """
    {
      "flags": [
        {
          "key": "new-ui", "type": "boolean", "enabled": true, "salt": "s1", "version": 3,
          "variations": [ { "key": "on", "value": true }, { "key": "off", "value": false } ],
          "offVariation": "off",
          "fallthrough": { "variation": null, "rollout": { "variations": [ { "variation": "on", "weight": 2500 }, { "variation": "off", "weight": 7500 } ], "bucketBy": null } },
          "rules": [
            { "id": "staff", "clauses": [ { "attribute": "key", "operator": "inSegment", "values": ["staff"], "negate": false } ], "serve": { "variation": "on" } },
            { "id": "old-app", "clauses": [ { "attribute": "appVersion", "operator": "semVerLessThan", "values": ["2.0.0"], "negate": false } ], "serve": { "variation": "off" } }
          ]
        },
        {
          "key": "max-items", "type": "number", "enabled": false, "salt": "s2", "version": 1,
          "variations": [ { "key": "low", "value": 10 }, { "key": "high", "value": 50 } ],
          "offVariation": "low", "fallthrough": { "variation": "high" }, "rules": []
        }
      ],
      "segments": [
        { "key": "staff", "included": ["ana"], "excluded": ["bob"], "version": 1,
          "rules": [ { "clauses": [ { "attribute": "email", "operator": "endsWith", "values": ["@featureflare.dev"] } ] } ] }
      ],
      "experiments": [
        { "key": "checkout", "status": "running", "trafficAllocation": 10000, "salt": "x", "controlVariant": "control",
          "primaryMetric": "purchase", "version": 2,
          "variants": [ { "key": "control", "weight": 5000 }, { "key": "one-page", "weight": 5000, "payload": { "steps": 1 } } ] }
      ],
      "generatedAt": "2026-09-01T12:00:00Z"
    }
    """;

    internal static ConfigSnapshot Snapshot() =>
        ConfigSnapshot.From(JsonSerializer.Deserialize<SdkConfig>(ConfigJson, SdkJson.Options)!);

    [Fact]
    public void Deserializes_the_wire_contract()
    {
        var snapshot = Snapshot();

        snapshot.Flags["new-ui"].Rules[0].Clauses[0].Operator.ShouldBe(ClauseOperator.InSegment);
        snapshot.Experiments["checkout"].Status.ShouldBe(ExperimentStatus.Running);
    }

    [Fact]
    public void Segment_rule_matches_included_and_rule_members_but_not_excluded()
    {
        var snapshot = Snapshot();

        FlagEvaluator.Evaluate(snapshot, "new-ui", new EvaluationContext("ana")).RuleId.ShouldBe("staff");
        FlagEvaluator.Evaluate(snapshot, "new-ui", new EvaluationContext("zoe") { { "email", "zoe@featureflare.dev" } }).RuleId.ShouldBe("staff");
        FlagEvaluator.Evaluate(snapshot, "new-ui", new EvaluationContext("bob") { { "email", "bob@featureflare.dev" } }).RuleId.ShouldNotBe("staff");
    }

    [Fact]
    public void Semver_rule_and_rollout_fallthrough()
    {
        var snapshot = Snapshot();

        var old = FlagEvaluator.Evaluate(snapshot, "new-ui", new EvaluationContext("u1") { { "appVersion", "1.9.0" } });
        old.Reason.ShouldBe(EvaluationReason.RuleMatch);
        old.Variation.ShouldBe("off");

        var share = Enumerable.Range(0, 10_000)
            .Count(i => FlagEvaluator.Evaluate(snapshot, "new-ui", new EvaluationContext($"user-{i}")).Variation == "on") / 10_000.0;
        share.ShouldBeInRange(0.22, 0.28);
    }

    [Fact]
    public void Disabled_flag_serves_off_variation_and_unknown_flag_reports_not_found()
    {
        var snapshot = Snapshot();

        FlagEvaluator.Evaluate(snapshot, "max-items", new EvaluationContext("u1")).Value!.Value.GetInt32().ShouldBe(10);
        FlagEvaluator.Evaluate(snapshot, "ghost", new EvaluationContext("u1")).Reason.ShouldBe(EvaluationReason.FlagNotFound);
    }

    [Fact]
    public void Experiment_assignment_is_sticky()
    {
        var snapshot = Snapshot();

        var first = FlagEvaluator.Assign(snapshot, "checkout", "user-42");
        var again = FlagEvaluator.Assign(snapshot, "checkout", "user-42");

        first.InExperiment.ShouldBeTrue();
        again.Variant.ShouldBe(first.Variant);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(2.0, "2")]
    [InlineData(1.5, "1.5")]
    [InlineData(42, "42")]
    public void Context_normalizes_attribute_values_like_the_server(object value, string expected)
    {
        var context = new EvaluationContext("u1") { { "attr", value } };

        context.TryGetValues("attr", out var values).ShouldBeTrue();
        values.ShouldBe([expected]);
    }
}
