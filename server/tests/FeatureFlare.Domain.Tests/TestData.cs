using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Experiments;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Domain.Tests;

internal static class TestData
{
    public static FeatureFlag BooleanFlag(
        string key = "new-checkout",
        bool enabled = true,
        IEnumerable<TargetingRule>? rules = null,
        Serve? fallthrough = null)
    {
        var flag = FeatureFlag.Create(
            key,
            "New checkout",
            null,
            FlagType.Boolean,
            [new Variation("on", "true"), new Variation("off", "false")],
            offVariation: "off",
            fallthrough: fallthrough ?? Serve.Fixed("off"),
            rules: rules);

        flag.SetEnabled(enabled);
        return flag;
    }

    public static TargetingRule Rule(string id, string serve, params Clause[] clauses) =>
        new(id, clauses, Serve.Fixed(serve));

    public static Clause Clause(string attribute, ClauseOperator op, params string[] values) =>
        new(attribute, op, values);

    public static EvaluationContext Context(string key, params (string Name, object? Value)[] attributes) =>
        new(key, attributes.ToDictionary(a => a.Name, a => a.Value));

    public static Experiment RunningExperiment(int traffic = Experiment.FullTraffic, params (string Key, int Weight)[] variants)
    {
        var list = variants.Length > 0 ? variants : [("control", 5_000), ("treatment", 5_000)];
        var experiment = Experiment.Create(
            "checkout-flow",
            "Checkout flow",
            "One-page checkout increases conversion",
            list.Select(v => new ExperimentVariant(v.Key, v.Weight)),
            controlVariant: list[0].Key,
            primaryMetric: "purchase",
            trafficAllocation: traffic);

        experiment.Start(DateTimeOffset.UtcNow);
        return experiment;
    }
}
