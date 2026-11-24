using System.Globalization;
using System.Reflection;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Simplified semantic version: "1.0.0" or "1.0.0-rc.1". A pre-release comes before the final
/// version with the same number (1.0.0-rc.1 &lt; 1.0.0).
/// </summary>
public sealed record AppVersion(Version Number, string? PreRelease = null) : IComparable<AppVersion>
{
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>The version of <paramref name="assembly"/> (its informational version, set by CI from the tag).</summary>
    public static AppVersion Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return TryParse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
            ?? new AppVersion(Normalize(assembly.GetName().Version ?? new Version(0, 0)));
    }

    /// <summary>"v1.0.0-rc.1+abc123" → 1.0.0-rc.1; null if it is not a version (e.g. "PreRelease").</summary>
    public static AppVersion? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim().TrimStart('v', 'V');
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];   // build metadata does not count
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        var number = dash >= 0 ? value[..dash] : value;
        var preRelease = dash >= 0 ? value[(dash + 1)..] : null;

        return Version.TryParse(number, out var version)
            ? new AppVersion(Normalize(version), string.IsNullOrWhiteSpace(preRelease) ? null : preRelease)
            : null;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text) ?? throw new FormatException($"'{text}' is not a version.");

    public int CompareTo(AppVersion? other)
    {
        if (other is null) return 1;

        var byNumber = Number.CompareTo(other.Number);
        if (byNumber != 0) return byNumber;
        if (PreRelease is null || other.PreRelease is null)
        {
            return (PreRelease is null).CompareTo(other.PreRelease is null);
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator >(AppVersion a, AppVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(AppVersion a, AppVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(AppVersion a, AppVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(AppVersion a, AppVersion b) => a.CompareTo(b) <= 0;

    public override string ToString() => PreRelease is null ? Number.ToString() : $"{Number}-{PreRelease}";

    /// <summary>Always compares 3 components: 0.8 == 0.8.0 == 0.8.0.0.</summary>
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Like semver: dot-separated identifiers; numeric ones compare as numbers (rc.2 &lt; rc.10).</summary>
    private static int ComparePreRelease(string a, string b)
    {
        var left = a.Split('.');
        var right = b.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var leftIsNumber = int.TryParse(left[i], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightIsNumber = int.TryParse(right[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            var result = (leftIsNumber, rightIsNumber) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left[i], right[i])
            };
            if (result != 0) return result;
        }

        return left.Length.CompareTo(right.Length);
    }
}
