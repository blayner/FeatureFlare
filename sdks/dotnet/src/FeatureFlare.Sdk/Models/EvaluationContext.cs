using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace FeatureFlare.Sdk.Models;

/// <summary>
/// The subject a flag is evaluated for. Supports collection initializers:
/// <code>new EvaluationContext("user-123") { { "plan", "pro" }, { "country", "BR" } }</code>
/// Attribute values are normalised to strings exactly like the server (spec §2).
/// </summary>
public sealed class EvaluationContext : IEnumerable<KeyValuePair<string, object?>>
{
    public const string KeyAttribute = "key";

    private readonly Dictionary<string, object?> _attributes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> _normalized = new(StringComparer.Ordinal);

    public EvaluationContext(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    public string Key { get; }

    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    public object? this[string attribute]
    {
        get => _attributes.GetValueOrDefault(attribute);
        set => Add(attribute, value);
    }

    public void Add(string attribute, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attribute);
        _attributes[attribute] = value;

        var values = Normalize(value);
        if (values.Count > 0)
        {
            _normalized[attribute] = values;
        }
        else
        {
            _normalized.Remove(attribute);
        }
    }

    public bool TryGetValues(string attribute, out IReadOnlyList<string> values)
    {
        if (attribute == KeyAttribute)
        {
            values = [Key];
            return true;
        }

        if (_normalized.TryGetValue(attribute, out var found))
        {
            values = found;
            return true;
        }

        values = [];
        return false;
    }

    internal string? GetBucketingValue(string? attribute) =>
        string.IsNullOrEmpty(attribute) || attribute == KeyAttribute
            ? Key
            : TryGetValues(attribute, out var values) ? values[0] : null;

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static IReadOnlyList<string> Normalize(object? value)
    {
        switch (value)
        {
            case null:
                return [];
            case string s:
                return [s];
            case bool b:
                return [b ? "true" : "false"];
            case JsonElement element:
                return NormalizeJson(element);
            case IEnumerable enumerable:
                var list = new List<string>();
                foreach (var item in enumerable)
                {
                    list.AddRange(Normalize(item));
                }

                return list;
            case IConvertible convertible when convertible.GetTypeCode() is >= TypeCode.SByte and <= TypeCode.Decimal:
                return [convertible.ToDouble(CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)];
            default:
                return [Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty];
        }
    }

    private static IReadOnlyList<string> NormalizeJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.True => ["true"],
        JsonValueKind.False => ["false"],
        JsonValueKind.Number => [element.GetDouble().ToString("R", CultureInfo.InvariantCulture)],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(e => NormalizeJson(e)).ToList(),
        _ => [],
    };
}
