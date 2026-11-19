using System.Globalization;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// IAU constellation containing a position of the sky. The official (Delporte) boundaries are
/// arcs of constant right ascension or declination for the equinox B1875.0; the position is
/// precessed to B1875.0 and looked up in the table of Roman (1987), "Identification of a
/// constellation from a position", PASP 99, 695 (CDS catalogue VI/42).
/// </summary>
public static class ConstellationLookup
{
    private const string BoundsResource = "AstroPlanner.Core.Data.constellation_bounds_roman1987.dat";
    private const string NamesResource = "AstroPlanner.Core.Data.constellation_names.dat";

    private static readonly Lazy<(Boundary[] Boundaries, Dictionary<string, Constellation> ByAbbreviation)> Table =
        new(LoadTable);

    /// <summary>The 88 IAU constellations, in alphabetical order of abbreviation.</summary>
    public static IReadOnlyList<Constellation> All =>
        [.. Table.Value.ByAbbreviation.Values.OrderBy(c => c.Abbreviation, StringComparer.Ordinal)];

    /// <summary>Constellation of J2000.0 catalog coordinates.</summary>
    public static Constellation FromJ2000(EquatorialCoordinates j2000) =>
        Find(j2000, Precession.J2000JulianDate);

    /// <summary>
    /// Constellation of <paramref name="coordinates"/> referred to the equinox
    /// <paramref name="equinoxJulianDate"/> (e.g. coordinates of date for a planet).
    /// </summary>
    public static Constellation Find(EquatorialCoordinates coordinates, double equinoxJulianDate)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var b1875 = Precession.Precess(coordinates, equinoxJulianDate, Precession.B1875JulianDate);
        var (boundaries, byAbbreviation) = Table.Value;

        // The table is sorted by decreasing lower declination: the first strip that contains the
        // point is its constellation (Roman 1987, section 2).
        foreach (var boundary in boundaries)
        {
            if (b1875.DeclinationDegrees >= boundary.LowerDeclinationDegrees
                && b1875.RightAscensionHours >= boundary.LowerRightAscensionHours
                && b1875.RightAscensionHours < boundary.UpperRightAscensionHours)
            {
                return byAbbreviation[boundary.Abbreviation];
            }
        }

        // The last row of the table covers the whole sky down to -90°, so this cannot happen.
        throw new InvalidOperationException("The constellation boundary table is incomplete.");
    }

    /// <summary>Finds a constellation by IAU abbreviation (case-insensitive); null if unknown.</summary>
    public static Constellation? FindByAbbreviation(string abbreviation) =>
        Table.Value.ByAbbreviation.GetValueOrDefault(abbreviation);

    private static (Boundary[], Dictionary<string, Constellation>) LoadTable()
    {
        var names = new Dictionary<string, Constellation>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in ReadDataLines(NamesResource))
        {
            var abbreviation = line[..3];
            names[abbreviation] = new Constellation(abbreviation, line[4..].Trim());
        }

        var boundaries = ReadDataLines(BoundsResource)
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => new Boundary(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture),
                names[parts[3]].Abbreviation))
            .ToArray();

        return (boundaries, names);
    }

    private static List<string> ReadDataLines(string resourceName)
    {
        using var stream = typeof(ConstellationLookup).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);

        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    private sealed record Boundary(
        double LowerRightAscensionHours,
        double UpperRightAscensionHours,
        double LowerDeclinationDegrees,
        string Abbreviation);
}
