using System.Text.Json;
using System.Text.Json.Serialization;

namespace FeatureFlare.Application.Serialization;

/// <summary>
/// Single source of truth for the wire format (camelCase properties, camelCase enum names). Used by the
/// HTTP API and by the SSE stream so that SDKs see exactly the same shapes on both channels.
/// </summary>
public static class FeatureFlareJson
{
    public static JsonSerializerOptions Options { get; } = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
