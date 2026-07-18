using System.Text.Json;
using System.Text.RegularExpressions;

namespace FeatureFlare.Domain.Common;

public static partial class Guard
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    /// <summary>Keys are part of the public SDK contract, so they are restricted to a URL- and log-safe alphabet.</summary>
    public static string Key(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value) || !KeyPattern().IsMatch(value))
        {
            throw new DomainException(
                $"{what} key '{value}' is invalid. Use 1-64 chars: lowercase letters, digits, '.', '_' or '-'.");
        }

        return value;
    }

    public static string NotEmpty(string? value, string what, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{what} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{what} must have at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string what, int maxLength = 2000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? throw new DomainException($"{what} must have at most {maxLength} characters.")
            : trimmed;
    }

    /// <summary>Validates that <paramref name="raw"/> is a single well-formed JSON value and returns its kind.</summary>
    public static JsonValueKind Json(string? raw, string what)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainException($"{what} must be a JSON value.");
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind;
        }
        catch (JsonException ex)
        {
            throw new DomainException($"{what} is not valid JSON: {ex.Message}");
        }
    }
}
