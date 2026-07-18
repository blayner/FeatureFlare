namespace FeatureFlare.Domain.Flags;

public enum ClauseOperator
{
    In,
    Contains,
    StartsWith,
    EndsWith,
    Matches,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    SemVerEqual,
    SemVerLessThan,
    SemVerGreaterThan,
}

/// <summary>
/// A single condition on a context attribute. A clause matches when the attribute matches <b>any</b> of
/// <see cref="Values"/>. A missing attribute never matches, even when <see cref="Negate"/> is set.
/// The reserved attribute <c>key</c> refers to the context key (usually the user id).
/// </summary>
public sealed record Clause(string Attribute, ClauseOperator Operator, IReadOnlyList<string> Values, bool Negate = false)
{
    public const string KeyAttribute = "key";
}
