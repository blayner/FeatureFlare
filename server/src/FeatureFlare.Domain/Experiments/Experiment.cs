using FeatureFlare.Domain.Common;

namespace FeatureFlare.Domain.Experiments;

public enum ExperimentStatus
{
    Draft,
    Running,
    Paused,
    Concluded,
}

/// <summary>Weight in basis points (0..10000). <see cref="Payload"/> is optional raw JSON handed to the client.</summary>
public sealed record ExperimentVariant(string Key, int Weight, string? Payload = null);

/// <summary>
/// A/B(/n) experiment. Users are first gated by <see cref="TrafficAllocation"/> and then split between
/// variants by weight; both decisions are deterministic hashes of the user key (see spec/README.md).
/// </summary>
public sealed class Experiment : AggregateRoot
{
    public const int FullTraffic = 10_000;

    private List<ExperimentVariant> _variants = [];

    private Experiment()
    {
    }

    public string Key { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string? Hypothesis { get; private set; }

    public ExperimentStatus Status { get; private set; }

    /// <summary>Share of users (basis points) that enter the experiment at all.</summary>
    public int TrafficAllocation { get; private set; }

    public string Salt { get; private set; } = default!;

    /// <summary>Baseline the other variants are compared against.</summary>
    public string ControlVariant { get; private set; } = default!;

    /// <summary>Metric used to declare a winner, e.g. "checkout-completed".</summary>
    public string PrimaryMetric { get; private set; } = default!;

    public IReadOnlyList<ExperimentVariant> Variants => _variants;

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? ConcludedAt { get; private set; }

    public string? WinningVariant { get; private set; }

    public static Experiment Create(
        string key,
        string name,
        string? hypothesis,
        IEnumerable<ExperimentVariant> variants,
        string controlVariant,
        string primaryMetric,
        int trafficAllocation = FullTraffic)
    {
        var validKey = Guard.Key(key, "Experiment");
        var validName = Guard.NotEmpty(name, "Experiment name");
        var validHypothesis = Guard.Optional(hypothesis, "Hypothesis");
        var validMetric = Guard.Key(primaryMetric, "Metric");
        var validVariants = ValidateVariants(variants, controlVariant);
        ValidateTraffic(trafficAllocation);

        return new Experiment
        {
            Key = validKey,
            Name = validName,
            Hypothesis = validHypothesis,
            Status = ExperimentStatus.Draft,
            Salt = Guid.NewGuid().ToString("N"),
            _variants = validVariants,
            ControlVariant = controlVariant,
            PrimaryMetric = validMetric,
            TrafficAllocation = trafficAllocation,
        };
    }

    /// <summary>
    /// Changing variants or weights after start would re-assign users and invalidate the collected data,
    /// so the design is frozen once the experiment leaves <see cref="ExperimentStatus.Draft"/>.
    /// </summary>
    public void Redesign(
        string name,
        string? hypothesis,
        IEnumerable<ExperimentVariant> variants,
        string controlVariant,
        string primaryMetric)
    {
        if (Status != ExperimentStatus.Draft)
        {
            throw new DomainException("Variants and metrics can only be changed while the experiment is a draft.");
        }

        var validName = Guard.NotEmpty(name, "Experiment name");
        var validHypothesis = Guard.Optional(hypothesis, "Hypothesis");
        var validMetric = Guard.Key(primaryMetric, "Metric");
        var validVariants = ValidateVariants(variants, controlVariant);

        Name = validName;
        Hypothesis = validHypothesis;
        _variants = validVariants;
        ControlVariant = controlVariant;
        PrimaryMetric = validMetric;
        Touch();
    }

    /// <summary>
    /// Traffic can change at any time before conclusion. Because the traffic gate uses its own hash,
    /// ramping up only adds users; nobody already in the experiment switches variant.
    /// </summary>
    public void SetTrafficAllocation(int trafficAllocation)
    {
        EnsureNotConcluded();
        ValidateTraffic(trafficAllocation);
        TrafficAllocation = trafficAllocation;
        Touch();
    }

    public void Start(DateTimeOffset now)
    {
        if (Status is not (ExperimentStatus.Draft or ExperimentStatus.Paused))
        {
            throw new DomainException($"Cannot start an experiment that is {Status.ToString().ToLowerInvariant()}.");
        }

        Status = ExperimentStatus.Running;
        StartedAt ??= now;
        Touch();
    }

    public void Pause()
    {
        if (Status != ExperimentStatus.Running)
        {
            throw new DomainException("Only running experiments can be paused.");
        }

        Status = ExperimentStatus.Paused;
        Touch();
    }

    public void Conclude(DateTimeOffset now, string? winningVariant)
    {
        if (Status is not (ExperimentStatus.Running or ExperimentStatus.Paused))
        {
            throw new DomainException("Only running or paused experiments can be concluded.");
        }

        if (winningVariant is not null && FindVariant(winningVariant) is null)
        {
            throw new DomainException($"Winning variant '{winningVariant}' does not exist.");
        }

        Status = ExperimentStatus.Concluded;
        WinningVariant = winningVariant;
        ConcludedAt = now;
        Touch();
    }

    public ExperimentVariant? FindVariant(string? key) =>
        key is null ? null : _variants.FirstOrDefault(v => v.Key == key);

    private void EnsureNotConcluded()
    {
        if (Status == ExperimentStatus.Concluded)
        {
            throw new DomainException("The experiment is concluded.");
        }
    }

    private static void ValidateTraffic(int trafficAllocation)
    {
        if (trafficAllocation is < 0 or > FullTraffic)
        {
            throw new DomainException($"Traffic allocation must be between 0 and {FullTraffic} basis points.");
        }
    }

    private static List<ExperimentVariant> ValidateVariants(IEnumerable<ExperimentVariant> variants, string controlVariant)
    {
        var list = variants?.ToList() ?? throw new DomainException("Variants are required.");
        if (list.Count is < 2 or > 10)
        {
            throw new DomainException("An experiment needs between 2 and 10 variants.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in list)
        {
            Guard.Key(variant.Key, "Variant");
            if (!keys.Add(variant.Key))
            {
                throw new DomainException($"Duplicated variant '{variant.Key}'.");
            }

            if (variant.Weight < 0)
            {
                throw new DomainException($"Variant '{variant.Key}' has a negative weight.");
            }

            if (variant.Payload is not null)
            {
                Guard.Json(variant.Payload, $"Variant '{variant.Key}' payload");
            }
        }

        if (list.Sum(v => v.Weight) != FullTraffic)
        {
            throw new DomainException($"Variant weights must sum to {FullTraffic} (100%).");
        }

        if (!keys.Contains(controlVariant))
        {
            throw new DomainException($"Control variant '{controlVariant}' does not exist.");
        }

        return list;
    }
}
