using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FeatureFlare.Domain.Common;

namespace FeatureFlare.Domain.Flags;

public sealed class FeatureFlag : AggregateRoot
{
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
        var flag = new FeatureFlag
        {
            Key = Guard.Key(key, "Flag"),
            Name = Guard.NotEmpty(name, "Flag name"),
            Description = Guard.Optional(description, "Flag description"),
            Type = type,
            Enabled = false,
            Salt = Guid.NewGuid().ToString("N"),
        };

        flag.ApplyVariations(variations, offVariation, fallthrough);
        flag.ApplyRules(rules ?? []);
        return flag;
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

    public void UpdateDetails(string name, string? description)
    {
        Name = Guard.NotEmpty(name, "Flag name");
        Description = Guard.Optional(description, "Flag description");
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

    public void ReplaceVariations(IEnumerable<Variation> variations, string offVariation, Serve fallthrough)
    {
        ApplyVariations(variations, offVariation, fallthrough);

        // Rules may reference variations that no longer exist.
        foreach (var rule in _rules)
        {
            ValidateServe(rule.Serve, $"Rule '{rule.Id}'");
        }

        Touch();
    }

    public void ReplaceRules(IEnumerable<TargetingRule> rules)
    {
        ApplyRules(rules);
        Touch();
    }

    public void RotateSalt()
    {
        Salt = Guid.NewGuid().ToString("N");
        Touch();
    }

    public Variation? FindVariation(string? key) =>
        key is null ? null : _variations.FirstOrDefault(v => v.Key == key);

    private void ApplyVariations(IEnumerable<Variation> variations, string offVariation, Serve fallthrough)
    {
        var list = variations?.ToList() ?? throw new DomainException("Variations are required.");

        if (list.Count < 2)
        {
            throw new DomainException("A flag needs at least two variations.");
        }

        if (list.Count > 50)
        {
            throw new DomainException("A flag supports at most 50 variations.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variation in list)
        {
            Guard.Key(variation.Key, "Variation");
            if (!seen.Add(variation.Key))
            {
                throw new DomainException($"Duplicated variation key '{variation.Key}'.");
            }

            EnsureValueMatchesType(variation);
        }

        if (Type == FlagType.Boolean)
        {
            var values = list.Select(v => v.Value.Trim()).ToHashSet(StringComparer.Ordinal);
            if (list.Count != 2 || !values.SetEquals(["true", "false"]))
            {
                throw new DomainException("Boolean flags must have exactly two variations: true and false.");
            }
        }

        _variations = list;

        if (FindVariation(offVariation) is null)
        {
            throw new DomainException($"Off variation '{offVariation}' does not exist.");
        }

        ValidateServe(fallthrough, "Fallthrough");
        OffVariation = offVariation;
        Fallthrough = fallthrough;
    }

    private void ApplyRules(IEnumerable<TargetingRule> rules)
    {
        var list = rules.ToList();
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

            ValidateServe(rule.Serve, $"Rule '{rule.Id}'");
        }

        _rules = list;
    }

    private void ValidateServe(Serve serve, string owner)
    {
        if (serve is null)
        {
            throw new DomainException($"{owner} must serve a variation or a rollout.");
        }

        switch (serve)
        {
            case { Variation: not null, Rollout: null }:
                if (FindVariation(serve.Variation) is null)
                {
                    throw new DomainException($"{owner} serves unknown variation '{serve.Variation}'.");
                }

                break;

            case { Variation: null, Rollout: not null }:
                var weights = serve.Rollout.Variations;
                if (weights is null || weights.Count == 0)
                {
                    throw new DomainException($"{owner} rollout has no variations.");
                }

                foreach (var weighted in weights)
                {
                    if (FindVariation(weighted.Variation) is null)
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

                break;

            default:
                throw new DomainException($"{owner} must serve either a variation or a rollout, not both.");
        }
    }

    private void EnsureValueMatchesType(Variation variation)
    {
        var kind = Guard.Json(variation.Value, $"Variation '{variation.Key}' value");
        var ok = Type switch
        {
            FlagType.Boolean => kind is JsonValueKind.True or JsonValueKind.False,
            FlagType.String => kind is JsonValueKind.String,
            FlagType.Number => kind is JsonValueKind.Number,
            FlagType.Json => kind is not JsonValueKind.Undefined,
            _ => false,
        };

        if (!ok)
        {
            throw new DomainException($"Variation '{variation.Key}' value is not a valid {Type.ToString().ToLowerInvariant()}.");
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
