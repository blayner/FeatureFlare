using System.Text.Json;

namespace FeatureFlare.Api.Contracts;

internal static class JsonValues
{
    /// <summary>Turns a stored raw JSON value into an element that serializes as-is (not as a string).</summary>
    public static JsonElement ToElement(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.Clone();
    }

    public static JsonElement? ToElementOrNull(string? rawJson) =>
        rawJson is null ? null : ToElement(rawJson);
}
