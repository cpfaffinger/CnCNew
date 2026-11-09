using System.Globalization;
using System.Text.RegularExpressions;

namespace CnCNew.Launcher;

/// <summary>A version as releases name it: <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2.3-rc.1</c> (build metadata after <c>+</c> is ignored). Ordered as semantic versioning orders it.</summary>
public sealed partial record SemanticVersion(int Major, int Minor, int Patch, string PreRelease = "") : IComparable<SemanticVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    public static SemanticVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Pattern().Match(text.Trim());
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var patch)) return null;
        return new SemanticVersion(major, minor, patch, match.Groups[4].Value);
    }

    public static SemanticVersion Parse(string text) => TryParse(text) ?? throw new FormatException($"'{text}' is not a version (MAJOR.MINOR.PATCH).");

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        // A version without a pre-release tag is newer than one with it.
        if (!IsPreRelease && !other.IsPreRelease) return 0;
        if (!IsPreRelease) return 1;
        if (!other.IsPreRelease) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(IsPreRelease ? "-" + PreRelease : "")}";

    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var index = 0; index < Math.Min(a.Length, b.Length); index++)
        {
            var aNumber = int.TryParse(a[index], NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var bNumber = int.TryParse(b[index], NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            var order = aNumber && bNumber ? x.CompareTo(y) : aNumber ? -1 : bNumber ? 1 : string.CompareOrdinal(a[index], b[index]);
            if (order != 0) return order;
        }
        return a.Length.CompareTo(b.Length);
    }

    [GeneratedRegex(@"^[vV]?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
