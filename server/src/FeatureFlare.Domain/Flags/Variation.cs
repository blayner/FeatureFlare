namespace FeatureFlare.Domain.Flags;

/// <summary>
/// One of the values a flag can serve. <see cref="Value"/> holds raw JSON so that every flag type
/// (boolean, string, number, json) shares a single storage and wire representation.
/// </summary>
public sealed record Variation(string Key, string Value, string? Name = null);
