using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Flags;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Evaluation;

/// <summary>
/// Pure, side-effect free flag evaluation:
/// <list type="number">
///   <item>disabled flag → off variation</item>
///   <item>first matching targeting rule → its serve</item>
///   <item>otherwise → fallthrough serve</item>
/// </list>
/// The algorithm is specified in spec/README.md and mirrored by the SDKs for local evaluation.
/// </summary>
public sealed class FlagEvaluationEngine
{
    private static readonly IReadOnlyDictionary<string, Segment> NoSegments = new Dictionary<string, Segment>();

    public FlagEvaluation Evaluate(
        FeatureFlag flag,
        EvaluationContext context,
        IReadOnlyDictionary<string, Segment>? segments = null)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(context);
        segments ??= NoSegments;

        if (!flag.Enabled)
        {
            return ServeVariation(flag, flag.OffVariation, EvaluationReason.Off);
        }

        foreach (var rule in flag.Rules)
        {
            if (rule.Clauses.All(clause => ClauseMatcher.Matches(clause, context, segments)))
            {
                return Resolve(flag, rule.Serve, context, EvaluationReason.RuleMatch, rule.Id);
            }
        }

        return Resolve(flag, flag.Fallthrough, context, EvaluationReason.Fallthrough);
    }

    private static FlagEvaluation Resolve(
        FeatureFlag flag,
        Serve serve,
        EvaluationContext context,
        EvaluationReason reason,
        string? ruleId = null)
    {
        if (serve.Variation is not null)
        {
            return ServeVariation(flag, serve.Variation, reason, ruleId);
        }

        if (serve.Rollout is { } rollout)
        {
            var bucketValue = context.GetBucketingValue(rollout.BucketBy);
            if (bucketValue is null)
            {
                // Can't bucket without the attribute: behave as if the user is in the first slot so the
                // result is still deterministic.
                return ServeVariation(flag, rollout.Variations[0].Variation, reason, ruleId);
            }

            var bucket = Bucketing.Bucket(flag.Key, flag.Salt, bucketValue);
            var index = Bucketing.Pick(rollout.Variations, v => v.Weight, bucket);
            return ServeVariation(flag, rollout.Variations[index].Variation, reason, ruleId);
        }

        return FlagEvaluation.Failed(flag.Key, "Serve has neither variation nor rollout.");
    }

    private static FlagEvaluation ServeVariation(FeatureFlag flag, string variationKey, EvaluationReason reason, string? ruleId = null)
    {
        var variation = flag.FindVariation(variationKey);
        return variation is null
            ? FlagEvaluation.Failed(flag.Key, $"Variation '{variationKey}' not found.")
            : new FlagEvaluation(flag.Key, variation.Key, variation.Value, reason, ruleId);
    }
}
