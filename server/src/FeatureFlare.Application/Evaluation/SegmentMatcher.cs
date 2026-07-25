using FeatureFlare.Domain.Evaluation;
using FeatureFlare.Domain.Segments;

namespace FeatureFlare.Application.Evaluation;

public static class SegmentMatcher
{
    /// <summary>Excluded keys win over included keys, which win over rules.</summary>
    public static bool Contains(Segment segment, EvaluationContext context)
    {
        if (segment.IsExplicitlyExcluded(context.Key))
        {
            return false;
        }

        if (segment.IsExplicitlyIncluded(context.Key))
        {
            return true;
        }

        // Segment rules can't contain inSegment clauses (enforced by the aggregate), so no segment map is
        // passed and there is no risk of recursion.
        return segment.Rules.Any(rule => rule.Clauses.All(clause => ClauseMatcher.Matches(clause, context)));
    }
}
