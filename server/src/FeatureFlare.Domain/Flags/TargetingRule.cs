namespace FeatureFlare.Domain.Flags;

/// <summary>
/// A rule matches when <b>all</b> clauses match. Rules are evaluated in order; the first match wins.
/// </summary>
public sealed record TargetingRule(string Id, IReadOnlyList<Clause> Clauses, Serve Serve, string? Description = null);
