using System.Collections;
using System.Globalization;
using System.Text.Json;
using FeatureFlare.Domain.Flags;

namespace FeatureFlare.Domain.Evaluation;

/// <summary>
/// The subject a flag is evaluated for. Attribute values are normalised to strings using the same rules
/// as the SDKs (see spec/README.md), so that a rule behaves identically on the server and on clients.
/// </summary>
public sealed class EvaluationContext
{
    private readonly Dictionary<string, IReadOnlyList<string>> _normalized;

    public EvaluationContext(string key, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Context key is required.", nameof(key));
        }

        Key = key;
        Attributes = attributes ?? new Dictionary<string, object?>();
        _normalized = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var (name, value) in Attributes)
        {
            var values = AttributeNormalizer.Normalize(value);
            if (values.Count > 0)
            {
                _normalized[name] = values;
            }
        }
    }

    public string Key { get; }

    public IReadOnlyDictionary<string, object?> Attributes { get; }

    public bool TryGetValues(string attribute, out IReadOnlyList<string> values)
    {
        if (attribute == Clause.KeyAttribute)
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

    /// <summary>Returns the first value of an attribute, used for <c>bucketBy</c>.</summary>
    public string? GetBucketingValue(string? attribute) =>
        string.IsNullOrEmpty(attribute) || attribute == Clause.KeyAttribute
            ? Key
            : TryGetValues(attribute, out var values) ? values[0] : null;
}

internal static class AttributeNormalizer
{
    public static IReadOnlyList<string> Normalize(object? value)
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
            case IConvertible convertible when IsNumeric(convertible.GetTypeCode()):
                return [FormatNumber(convertible.ToDouble(CultureInfo.InvariantCulture))];
            default:
                return [Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty];
        }
    }

    private static IReadOnlyList<string> NormalizeJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.True => ["true"],
        JsonValueKind.False => ["false"],
        JsonValueKind.Number => [FormatNumber(element.GetDouble())],
        JsonValueKind.Array => element.EnumerateArray().SelectMany(NormalizeJson).ToList(),
        _ => [],
    };

    /// <summary>Mirrors JavaScript's <c>String(number)</c> for the common cases (integers and decimals).</summary>
    private static string FormatNumber(double number) => number.ToString("R", CultureInfo.InvariantCulture);

    private static bool IsNumeric(TypeCode code) => code is >= TypeCode.SByte and <= TypeCode.Decimal;
}
