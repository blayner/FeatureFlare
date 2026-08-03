using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FeatureFlare.Domain.Common;

namespace FeatureFlare.Domain.Flags;

/// <summary>
/// Feature flag aggregate. Every mutation validates the complete new configuration first and only then
/// assigns it, so a rejected change never leaves the aggregate half-updated.
/// </summary>
public sealed class FeatureFlag : AggregateRoot
{
    public const int MaxVariations = 50;
    public const int MaxRules = 200;

    private List<Variation> _variations = [];
    private List<TargetingRule> _rules = [];

    private FeatureFlag()
    {
    }

    public string Key { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Description { get; private set; }

    public FlagType Type { get; private set; }

    public bool Enabled { get; private set; }

    /// <summary>
    /// Mixed into the bucketing hash. Rotating the salt re-shuffles every percentage rollout of this flag
    /// without touching other flags.
    /// </summary>
    public string Salt { get; private set; } = default!;

    /// <summary>Served when the flag is disabled.</summary>
    public string OffVariation { get; private set; } = default!;

    /// <summary>Served when the flag is enabled and no rule matches.</summary>
    public Serve Fallthrough { get; private set; } = default!;

    public IReadOnlyList<Variation> Variations => _variations;

    public IReadOnlyList<TargetingRule> Rules => _rules;

    public static FeatureFlag Create(
        string key,
        string name,
        string? description,
        FlagType type,
        IEnumerable<Variation> variations,
        string offVariation,
        Serve fallthrough,
        IEnumerable<TargetingRule>? rules = null)
    {
        var validKey = Guard.Key(key, "Flag");
        var validName = Guard.NotEmpty(name, "Flag name");
        var validDescription = Guard.Optional(description, "Flag description");
        var config = Validate(type, variations, offVariation, fallthrough, rules ?? []);

        return new FeatureFlag
        {
            Key = validKey,
            Name = validName,
            Description = validDescription,
            Type = type,
            Enabled = false,
            Salt = Guid.NewGuid().ToString("N"),
            _variations = config.Variations,
            OffVariation = offVariation,
            Fallthrough = fallthrough,
            _rules = config.Rules,
        };
    }

    public static FeatureFlag CreateBoolean(string key, string name, string? description = null) =>
        Create(
            key,
            name,
            description,
            FlagType.Boolean,
            [new Variation("on", "true"), new Variation("off", "false")],
            offVariation: "off",
            fallthrough: Serve.Fixed("on"));

    /// <summary>Replaces the whole configuration atomically (one version bump).</summary>
    public void Reconfigure(
        string name,
        string? description,
        IEnumerable<Variation> variations,
        string offVariation,
        Serve fallthrough,
        IEnumerable<TargetingRule> rules)
    {
        var validName = Guard.NotEmpty(name, "Flag name");
        var validDescription = Guard.Optional(description, "Flag description");
        var config = Validate(Type, variations, offVariation, fallthrough, rules);

        Name = validName;
        Description = validDescription;
        _variations = config.Variations;
        OffVariation = offVariation;
        Fallthrough = fallthrough;
        _rules = config.Rules;
        Touch();
    }

    public void UpdateDetails(string name, string? description)
    {
        var validName = Guard.NotEmpty(name, "Flag name");
        Description = Guard.Optional(description, "Flag description");
        Name = validName;
        Touch();
    }

    public void ReplaceRules(IEnumerable<TargetingRule> rules)
    {
        _rules = ValidateRules(_variations, rules);
        Touch();
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
        {
            return;
        }

        Enabled = enabled;
        Touch();
    }

    public void RotateSalt()
    {
        Salt = Guid.NewGuid().ToString("N");
        Touch();
    }

    public Variation? FindVariation(string? key) => Find(_variations, key);

    /// <summary>Segment keys referenced by any rule, used to keep referential integrity on segment deletion.</summary>
    public IReadOnlySet<string> ReferencedSegments() => ReferencedSegments(_rules);

    public static IReadOnlySet<string> ReferencedSegments(IEnumerable<TargetingRule> rules) =>
        rules
            .SelectMany(r => r.Clauses)
            .Where(c => c.Operator == ClauseOperator.InSegment)
            .SelectMany(c => c.Values)
            .ToHashSet(StringComparer.Ordinal);

    private static Variation? Find(IReadOnlyList<Variation> variations, string? key) =>
        key is null ? null : variations.FirstOrDefault(v => v.Key == key);

    private static (List<Variation> Variations, List<TargetingRule> Rules) Validate(
        FlagType type,
        IEnumerable<Variation> variations,
        string offVariation,
        Serve fallthrough,
        IEnumerable<TargetingRule> rules)
    {
        var validVariations = ValidateVariations(type, variations);

        if (Find(validVariations, offVariation) is null)
        {
            throw new DomainException($"Off variation '{offVariation}' does not exist.");
        }

        ValidateServe(validVariations, fallthrough, "Fallthrough");
        var validRules = ValidateRules(validVariations, rules);
        return (validVariations, validRules);
    }

    private static List<Variation> ValidateVariations(FlagType type, IEnumerable<Variation> variations)
    {
        var list = variations?.ToList() ?? throw new DomainException("Variations are required.");

        if (list.Count < 2)
        {
            throw new DomainException("A flag needs at least two variations.");
        }

        if (list.Count > MaxVariations)
        {
            throw new DomainException($"A flag supports at most {MaxVariations} variations.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variation in list)
        {
            Guard.Key(variation.Key, "Variation");
            if (!seen.Add(variation.Key))
            {
                throw new DomainException($"Duplicated variation key '{variation.Key}'.");
            }

            EnsureValueMatchesType(type, variation);
        }

        if (type == FlagType.Boolean)
        {
            var values = list.Select(v => v.Value.Trim()).ToHashSet(StringComparer.Ordinal);
            if (list.Count != 2 || !values.SetEquals(["true", "false"]))
            {
                throw new DomainException("Boolean flags must have exactly two variations: true and false.");
            }
        }

        return list;
    }

    private static List<TargetingRule> ValidateRules(IReadOnlyList<Variation> variations, IEnumerable<TargetingRule> rules)
    {
        var list = rules?.ToList() ?? [];
        if (list.Count > MaxRules)
        {
            throw new DomainException($"A flag supports at most {MaxRules} rules.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in list)
        {
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id))
            {
                throw new DomainException($"Rule id '{rule.Id}' is missing or duplicated.");
            }

            if (rule.Clauses is null || rule.Clauses.Count == 0)
            {
                throw new DomainException($"Rule '{rule.Id}' must have at least one clause.");
            }

            foreach (var clause in rule.Clauses)
            {
                ClauseValidator.Validate(clause, $"Rule '{rule.Id}'");
            }

            ValidateServe(variations, rule.Serve, $"Rule '{rule.Id}'");
        }

        return list;
    }

    private static void ValidateServe(IReadOnlyList<Variation> variations, Serve? serve, string owner)
    {
        if (serve is null)
        {
            throw new DomainException($"{owner} must serve a variation or a rollout.");
        }

        if (serve.Variation is not null && serve.Rollout is null)
        {
            if (Find(variations, serve.Variation) is null)
            {
                throw new DomainException($"{owner} serves unknown variation '{serve.Variation}'.");
            }

            return;
        }

        if (serve.Variation is not null || serve.Rollout is null)
        {
            throw new DomainException($"{owner} must serve either a variation or a rollout, not both.");
        }

        var weights = serve.Rollout.Variations;
        if (weights is null || weights.Count == 0)
        {
            throw new DomainException($"{owner} rollout has no variations.");
        }

        foreach (var weighted in weights)
        {
            if (Find(variations, weighted.Variation) is null)
            {
                throw new DomainException($"{owner} rollout references unknown variation '{weighted.Variation}'.");
            }

            if (weighted.Weight < 0)
            {
                throw new DomainException($"{owner} rollout has a negative weight.");
            }
        }

        if (weights.Sum(w => w.Weight) != Rollout.TotalWeight)
        {
            throw new DomainException($"{owner} rollout weights must sum to {Rollout.TotalWeight} (100%).");
        }
    }

    private static void EnsureValueMatchesType(FlagType type, Variation variation)
    {
        var kind = Guard.Json(variation.Value, $"Variation '{variation.Key}' value");
        var ok = type switch
        {
            FlagType.Boolean => kind is JsonValueKind.True or JsonValueKind.False,
            FlagType.String => kind is JsonValueKind.String,
            FlagType.Number => kind is JsonValueKind.Number,
            FlagType.Json => kind is not JsonValueKind.Undefined,
            _ => false,
        };

        if (!ok)
        {
            throw new DomainException(
                $"Variation '{variation.Key}' value is not a valid {type.ToString().ToLowerInvariant()}.");
        }
    }
}

internal static class ClauseValidator
{
    public static void Validate(Clause clause, string owner)
    {
        if (string.IsNullOrWhiteSpace(clause.Attribute))
        {
            throw new DomainException($"{owner} has a clause without attribute.");
        }

        if (clause.Values is null || clause.Values.Count == 0)
        {
            throw new DomainException($"{owner} clause on '{clause.Attribute}' has no values.");
        }

        foreach (var value in clause.Values)
        {
            switch (clause.Operator)
            {
                case ClauseOperator.Matches:
                    try
                    {
                        _ = new Regex(value, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
                    }
                    catch (ArgumentException)
                    {
                        throw new DomainException($"{owner} has an invalid regular expression '{value}'.");
                    }

                    break;

                case ClauseOperator.InSegment:
                    Guard.Key(value, "Segment");
                    break;

                case ClauseOperator.LessThan or ClauseOperator.LessThanOrEqual
                    or ClauseOperator.GreaterThan or ClauseOperator.GreaterThanOrEqual:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        throw new DomainException($"{owner} numeric clause value '{value}' is not a number.");
                    }

                    break;
            }
        }
    }
}
