namespace FeatureFlare.Domain.Flags;

/// <summary>Weight is expressed in basis points (0..10000), so 2500 = 25%.</summary>
public sealed record WeightedVariation(string Variation, int Weight);

/// <summary>
/// Percentage split between variations. Users are bucketed by hashing <see cref="BucketBy"/>
/// (defaults to the context key), which keeps assignments sticky across requests and SDKs.
/// </summary>
public sealed record Rollout(IReadOnlyList<WeightedVariation> Variations, string? BucketBy = null)
{
    public const int TotalWeight = 10_000;
}

/// <summary>What a rule (or the fallthrough) serves: either a fixed variation or a percentage rollout.</summary>
public sealed record Serve(string? Variation = null, Rollout? Rollout = null)
{
    public static Serve Fixed(string variation) => new(variation);

    public static Serve Split(params WeightedVariation[] variations) => new(null, new Rollout(variations));
}
