using System.Globalization;
using System.Text.RegularExpressions;

namespace AstroPlanner.DataBuilder;

/// <summary>Formatting of catalog identifiers into the forms used in the app.</summary>
internal static partial class Designations
{
    /// <summary>Catalogs whose numbers are written without a space ("M42", "C14").</summary>
    private static readonly HashSet<string> CompactCatalogs = new(StringComparer.Ordinal) { "M", "C" };

    /// <summary>
    /// Other catalogs kept as cross-IDs. Survey ids (2MASX, SDSS, IRAS, MCG…) are dropped: they
    /// are not useful to search from a planner and would triple the size of the table.
    /// </summary>
    private static readonly HashSet<string> KeptCatalogs = new(StringComparer.Ordinal)
    {
        "PGC", "UGC", "UGCA", "ESO", "LBN", "PK", "PN", "HD", "HIP", "Sh2", "Mel", "Cl"
    };

    /// <summary>
    /// Formats an OpenNGC name or identifier: "NGC0224" → "NGC 224", "IC0080 NED01" →
    /// "IC 80 NED01", "ESO056-115" → "ESO 56-115", "M040" → "M40", "C 001" → "C1",
    /// "SH 2-155" → "Sh2-155". Returns the catalog prefix too.
    /// </summary>
    public static Designation Format(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var value = raw.Trim();

        if (SharplessPattern().Match(value) is { Success: true } sharpless)
        {
            return new Designation("Sh2-" + Number(sharpless.Groups[1].Value), "Sh2");
        }

        if (PlanetaryNebulaPattern().IsMatch(value))
        {
            return new Designation(value, "PN");
        }

        var match = NamePattern().Match(value);
        if (!match.Success)
        {
            // Not "<catalog> <number>" (e.g. "HD" alone, "50 Cas", "MCG -02-01-031"): never kept.
            return new Designation(value, string.Empty);
        }

        var catalog = match.Groups["catalog"].Value;
        var number = Number(match.Groups["number"].Value);
        var rest = match.Groups["rest"].Value;
        var separator = CompactCatalogs.Contains(catalog) ? string.Empty : " ";
        return new Designation(catalog + separator + number + rest, catalog);
    }

    /// <summary>Formats a Messier number from the OpenNGC "M" column ("031" → "M31").</summary>
    public static Designation Messier(string number) => new("M" + Number(number), "M");

    /// <summary>Formats an NGC or IC number from the cross-reference columns ("0281" → "NGC 281").</summary>
    public static Designation CrossReference(string catalog, string number) => Format(catalog + number.Trim());

    /// <summary>True if <paramref name="designation"/> belongs to a catalog worth keeping.</summary>
    public static bool IsKept(Designation designation) =>
        designation.Catalog is "M" or "NGC" or "IC" or "C" || KeptCatalogs.Contains(designation.Catalog);

    /// <summary>Strips leading zeros ("0074" → "74", "000" → "0").</summary>
    private static string Number(string digits) =>
        int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<catalog>[A-Za-z]+) ?(?<number>\d+)(?<rest>.*)$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^SH ?2-(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SharplessPattern();

    [GeneratedRegex(@"^PN G")]
    private static partial Regex PlanetaryNebulaPattern();
}
