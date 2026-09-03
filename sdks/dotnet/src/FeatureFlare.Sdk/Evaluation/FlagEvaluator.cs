using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FeatureFlare.Sdk.Hashing;
using FeatureFlare.Sdk.Models;

namespace FeatureFlare.Sdk.Evaluation;

/// <summary>
/// Local implementation of the evaluation algorithm (spec/README.md). Stateless and allocation-light:
/// safe to call on every request from any thread.
/// </summary>
public static class FlagEvaluator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(25);
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    public static EvaluationDetail Evaluate(ConfigSnapshot snapshot, string flagKey, EvaluationContext context)
    {
        if (!snapshot.Flags.TryGetValue(flagKey, out var flag))
        {
            return EvaluationDetail.Failed(flagKey, EvaluationReason.FlagNotFound, $"Flag '{flagKey}' not found.");
        }

        return Evaluate(snapshot, flag, context);
    }

    public static EvaluationDetail Evaluate(ConfigSnapshot snapshot, FlagConfig flag, EvaluationContext context)
    {
        if (!flag.Enabled)
        {
            return Variation(flag, flag.OffVariation, EvaluationReason.Off);
        }

        foreach (var rule in flag.Rules)
        {
            if (rule.Clauses.All(c => MatchClause(snapshot, c, context, allowSegments: true)))
            {
                return ResolveServe(flag, rule.Serve, context) is { } variation
                    ? Variation(flag, variation, EvaluationReason.RuleMatch, rule.Id)
                    : EvaluationDetail.Failed(flag.Key, EvaluationReason.Error, $"Rule '{rule.Id}' has an invalid serve.");
            }
        }

        return ResolveServe(flag, flag.Fallthrough, context) is { } fallthrough
            ? Variation(flag, fallthrough, EvaluationReason.Fallthrough)
            : EvaluationDetail.Failed(flag.Key, EvaluationReason.Error, "Fallthrough has an invalid serve.");
    }

    public static ExperimentAssignment Assign(ConfigSnapshot snapshot, string experimentKey, string userKey)
    {
        if (!snapshot.Experiments.TryGetValue(experimentKey, out var experiment))
        {
            return new ExperimentAssignment(experimentKey, null, AssignmentReason.ExperimentNotFound);
        }

        if (experiment.Status != ExperimentStatus.Running || experiment.Variants.Count == 0)
        {
            return new ExperimentAssignment(experimentKey, null, AssignmentReason.NotRunning);
        }

        if (Bucketing.Bucket(experiment.Key, experiment.Salt + ".traffic", userKey) >= experiment.TrafficAllocation)
        {
            return new ExperimentAssignment(experimentKey, null, AssignmentReason.NotInTraffic);
        }

        var index = Bucketing.Pick(experiment.Variants, v => v.Weight, Bucketing.Bucket(experiment.Key, experiment.Salt, userKey));
        return new ExperimentAssignment(experimentKey, experiment.Variants[index].Key, AssignmentReason.Assigned);
    }

    internal static bool IsInSegment(SegmentConfig segment, EvaluationContext context)
    {
        if (segment.Excluded.Contains(context.Key, StringComparer.Ordinal))
        {
            return false;
        }

        if (segment.Included.Contains(context.Key, StringComparer.Ordinal))
        {
            return true;
        }

        return segment.Rules.Any(r => r.Clauses.All(c => MatchClause(ConfigSnapshot.Empty, c, context, allowSegments: false)));
    }

    private static string? ResolveServe(FlagConfig flag, Serve serve, EvaluationContext context)
    {
        if (serve.Variation is not null)
        {
            return serve.Variation;
        }

        if (serve.Rollout is not { Variations.Count: > 0 } rollout)
        {
            return null;
        }

        var bucketValue = context.GetBucketingValue(rollout.BucketBy);
        if (bucketValue is null)
        {
            return rollout.Variations[0].Variation;
        }

        var index = Bucketing.Pick(rollout.Variations, v => v.Weight, Bucketing.Bucket(flag.Key, flag.Salt, bucketValue));
        return rollout.Variations[index].Variation;
    }

    private static bool MatchClause(ConfigSnapshot snapshot, Clause clause, EvaluationContext context, bool allowSegments)
    {
        if (clause.Operator == ClauseOperator.InSegment)
        {
            if (!allowSegments)
            {
                return false;
            }

            var member = clause.Values.Any(key => snapshot.Segments.TryGetValue(key, out var segment) && IsInSegment(segment, context));
            return clause.Negate ? !member : member;
        }

        // Missing attributes never match, even when negated (spec §3).
        if (!context.TryGetValues(clause.Attribute, out var actual) || actual.Count == 0)
        {
            return false;
        }

        var matched = actual.Any(a => clause.Values.Any(e => MatchValue(clause.Operator, a, e)));
        return clause.Negate ? !matched : matched;
    }

    internal static bool MatchValue(ClauseOperator op, string actual, string expected) => op switch
    {
        ClauseOperator.In => string.Equals(actual, expected, StringComparison.Ordinal),
        ClauseOperator.Contains => actual.Contains(expected, StringComparison.Ordinal),
        ClauseOperator.StartsWith => actual.StartsWith(expected, StringComparison.Ordinal),
        ClauseOperator.EndsWith => actual.EndsWith(expected, StringComparison.Ordinal),
        ClauseOperator.Matches => MatchRegex(actual, expected),
        ClauseOperator.LessThan => CompareNumbers(actual, expected) is < 0,
        ClauseOperator.LessThanOrEqual => CompareNumbers(actual, expected) is <= 0,
        ClauseOperator.GreaterThan => CompareNumbers(actual, expected) is > 0,
        ClauseOperator.GreaterThanOrEqual => CompareNumbers(actual, expected) is >= 0,
        ClauseOperator.SemVerEqual => CompareVersions(actual, expected) is 0,
        ClauseOperator.SemVerLessThan => CompareVersions(actual, expected) is < 0,
        ClauseOperator.SemVerGreaterThan => CompareVersions(actual, expected) is > 0,
        _ => false,
    };

    private static EvaluationDetail Variation(FlagConfig flag, string key, EvaluationReason reason, string? ruleId = null)
    {
        foreach (var variation in flag.Variations)
        {
            if (variation.Key == key)
            {
                return new EvaluationDetail(flag.Key, variation.Key, variation.Value, reason, ruleId);
            }
        }

        return EvaluationDetail.Failed(flag.Key, EvaluationReason.Error, $"Variation '{key}' not found.");
    }

    private static bool MatchRegex(string actual, string pattern)
    {
        var regex = RegexCache.GetOrAdd(pattern, static p =>
        {
            try
            {
                return new Regex(p, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return null;
            }
        });

        try
        {
            return regex?.IsMatch(actual) ?? false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>Returns the sign of the comparison, or <c>null</c> when either side is not a number.</summary>
    private static int? CompareNumbers(string actual, string expected) =>
        double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
        double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            ? a.CompareTo(b)
            : null;

    private static int? CompareVersions(string actual, string expected) =>
        SemVer.TryParse(actual, out var a) && SemVer.TryParse(expected, out var b) ? a.CompareTo(b) : null;
}
