using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Application.Evaluation;

public static class ClauseMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(25);
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new(StringComparer.Ordinal);

    public static bool Matches(Clause clause, EvaluationContext context)
    {
        if (!context.TryGetValues(clause.Attribute, out var actualValues) || actualValues.Count == 0)
        {
            // Missing attributes never match, regardless of negation. Otherwise "country not in [BR]"
            // would match every anonymous user that simply has no country.
            return false;
        }

        var matched = false;
        foreach (var actual in actualValues)
        {
            foreach (var expected in clause.Values)
            {
                if (MatchValue(clause.Operator, actual, expected))
                {
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                break;
            }
        }

        return clause.Negate ? !matched : matched;
    }

    internal static bool MatchValue(ClauseOperator op, string actual, string expected) => op switch
    {
        ClauseOperator.In => string.Equals(actual, expected, StringComparison.Ordinal),
        ClauseOperator.Contains => actual.Contains(expected, StringComparison.Ordinal),
        ClauseOperator.StartsWith => actual.StartsWith(expected, StringComparison.Ordinal),
        ClauseOperator.EndsWith => actual.EndsWith(expected, StringComparison.Ordinal),
        ClauseOperator.Matches => MatchRegex(actual, expected),
        ClauseOperator.LessThan => CompareNumbers(actual, expected, c => c < 0),
        ClauseOperator.LessThanOrEqual => CompareNumbers(actual, expected, c => c <= 0),
        ClauseOperator.GreaterThan => CompareNumbers(actual, expected, c => c > 0),
        ClauseOperator.GreaterThanOrEqual => CompareNumbers(actual, expected, c => c >= 0),
        ClauseOperator.SemVerEqual => CompareSemVer(actual, expected, c => c == 0),
        ClauseOperator.SemVerLessThan => CompareSemVer(actual, expected, c => c < 0),
        ClauseOperator.SemVerGreaterThan => CompareSemVer(actual, expected, c => c > 0),
        _ => false,
    };

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

        if (regex is null)
        {
            return false;
        }

        try
        {
            return regex.IsMatch(actual);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool CompareNumbers(string actual, string expected, Func<int, bool> predicate)
    {
        if (!double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ||
            !double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        return predicate(a.CompareTo(b));
    }

    private static bool CompareSemVer(string actual, string expected, Func<int, bool> predicate) =>
        SemVer.TryParse(actual, out var a) && SemVer.TryParse(expected, out var b) && predicate(a.CompareTo(b));
}
