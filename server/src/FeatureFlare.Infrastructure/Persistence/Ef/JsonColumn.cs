using System.Text.Json;
using FeatureFlare.Application.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FeatureFlare.Infrastructure.Persistence.Ef;

/// <summary>
/// Stores value objects (variations, rules, serves) as <c>jsonb</c>. They are always read and written as
/// a whole with their aggregate, so a document column is simpler and faster than a normalised schema.
/// The same serializer settings as the public API are used, so the stored JSON matches the wire format.
/// </summary>
internal static class JsonColumn
{
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property)
        where T : class
    {
        property
            .HasConversion(
                value => Serialize(value),
                json => Deserialize<T>(json),
                new ValueComparer<T>(
                    (a, b) => Serialize(a) == Serialize(b),
                    value => Serialize(value).GetHashCode(StringComparison.Ordinal),
                    value => Deserialize<T>(Serialize(value))))
            .HasColumnType("jsonb");

        return property;
    }

    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, FeatureFlareJson.Options);

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, FeatureFlareJson.Options)
        ?? throw new InvalidOperationException($"Column contains null JSON for {typeof(T).Name}.");
}
