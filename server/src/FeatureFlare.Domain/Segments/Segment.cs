using FeatureFlare.Domain.Common;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Domain.Segments;

/// <summary>A segment rule matches when all of its clauses match.</summary>
public sealed record SegmentRule(IReadOnlyList<Clause> Clauses, string? Description = null);

/// <summary>
/// A reusable, named group of contexts (e.g. "beta-testers", "enterprise-customers") that flag rules can
/// target with the <c>inSegment</c> operator. Membership: excluded → included → rules.
/// </summary>
public sealed class Segment : AggregateRoot
{
    public const int MaxExplicitKeys = 10_000;

    private List<string> _included = [];
    private List<string> _excluded = [];
    private List<SegmentRule> _rules = [];

    private Segment()
    {
    }

    public string Key { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    public IReadOnlyList<string> Included => _included;

    public IReadOnlyList<string> Excluded => _excluded;

    public IReadOnlyList<SegmentRule> Rules => _rules;

    // Lookup sets are built lazily: segments can hold thousands of explicit keys and are evaluated on the
    // hot path, so a linear scan per evaluation would not scale.
    private HashSet<string>? _includedLookup;
    private HashSet<string>? _excludedLookup;

    public bool IsExplicitlyIncluded(string contextKey) =>
        (_includedLookup ??= new HashSet<string>(_included, StringComparer.Ordinal)).Contains(contextKey);

    public bool IsExplicitlyExcluded(string contextKey) =>
        (_excludedLookup ??= new HashSet<string>(_excluded, StringComparer.Ordinal)).Contains(contextKey);

    public static Segment Create(
        string key,
        string name,
        string? description,
        IEnumerable<string>? included = null,
        IEnumerable<string>? excluded = null,
        IEnumerable<SegmentRule>? rules = null)
    {
        var segment = new Segment
        {
            Key = Guard.Key(key, "Segment"),
            Name = Guard.NotEmpty(name, "Segment name"),
            Description = Guard.Optional(description, "Segment description"),
        };

        segment.Apply(included ?? [], excluded ?? [], rules ?? []);
        return segment;
    }

    public void Update(
        string name,
        string? description,
        IEnumerable<string> included,
        IEnumerable<string> excluded,
        IEnumerable<SegmentRule> rules)
    {
        var validName = Guard.NotEmpty(name, "Segment name");
        var validDescription = Guard.Optional(description, "Segment description");
        Apply(included, excluded, rules);
        Name = validName;
        Description = validDescription;
        Touch();
    }

    private void Apply(IEnumerable<string> included, IEnumerable<string> excluded, IEnumerable<SegmentRule> rules)
    {
        var inc = Normalize(included, "included");
        var exc = Normalize(excluded, "excluded");

        var overlap = inc.Intersect(exc, StringComparer.Ordinal).FirstOrDefault();
        if (overlap is not null)
        {
            throw new DomainException($"Context '{overlap}' cannot be both included and excluded.");
        }

        var ruleList = rules.ToList();
        foreach (var rule in ruleList)
        {
            if (rule.Clauses is null || rule.Clauses.Count == 0)
            {
                throw new DomainException("Segment rules must have at least one clause.");
            }

            foreach (var clause in rule.Clauses)
            {
                if (clause.Operator == ClauseOperator.InSegment)
                {
                    throw new DomainException("Segment rules cannot reference other segments.");
                }

                ClauseValidator.Validate(clause, $"Segment '{Key}'");
            }
        }

        _included = inc;
        _excluded = exc;
        _rules = ruleList;
        _includedLookup = null;
        _excludedLookup = null;
    }

    private static List<string> Normalize(IEnumerable<string> keys, string what)
    {
        var list = keys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return list.Count > MaxExplicitKeys
            ? throw new DomainException($"A segment supports at most {MaxExplicitKeys} {what} keys; use rules instead.")
            : list;
    }
}
