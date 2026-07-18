using System.Globalization;

namespace FeatureFlare.Application.Evaluation;

/// <summary>
/// Minimal, lenient semantic version used by the semVer* operators. Accepts an optional leading "v" and
/// missing minor/patch ("2" == "2.0.0"). Build metadata is ignored, pre-release ordering follows semver 2.0.
/// </summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string[] PreRelease) : IComparable<SemVer>
{
    public static bool TryParse(string? input, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string[] pre = [];
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            pre = text[(dash + 1)..].Split('.');
            text = text[..dash];
            if (pre.Any(string.IsNullOrEmpty))
            {
                return false;
            }
        }

        var parts = text.Split('.');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new SemVer(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        var mine = PreRelease ?? [];
        var theirs = other.PreRelease ?? [];

        // A release is greater than any of its pre-releases.
        if (mine.Length == 0 || theirs.Length == 0)
        {
            return theirs.Length.CompareTo(mine.Length);
        }

        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            result = CompareIdentifier(mine[i], theirs[i]);
            if (result != 0) return result;
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
        var bNumeric = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);

        return (aNumeric, bNumeric) switch
        {
            (true, true) => an.CompareTo(bn),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(a, b) switch { < 0 => -1, > 0 => 1, _ => 0 },
        };
    }
}
