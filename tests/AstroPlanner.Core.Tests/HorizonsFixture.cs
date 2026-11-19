using System.Globalization;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// A JPL Horizons golden fixture (tests/fixtures/horizons, produced once by
/// tools/fetch-horizons-fixtures.ps1): apparent azimuth/elevation every minute.
/// </summary>
internal sealed class HorizonsFixture
{
    private HorizonsFixture(IReadOnlyList<(DateTime Utc, double Azimuth, double Elevation)> rows) => Rows = rows;

    public IReadOnlyList<(DateTime Utc, double Azimuth, double Elevation)> Rows { get; }

    public static HorizonsFixture Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "horizons", name + ".csv");
        var rows = File.ReadLines(path)
            .Where(line => !line.StartsWith('#') && !line.StartsWith("utc", StringComparison.Ordinal))
            .Select(line => line.Split(','))
            .Select(parts => (
                DateTime.SpecifyKind(DateTime.ParseExact(parts[0], "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(parts[1], CultureInfo.InvariantCulture),
                double.Parse(parts[2], CultureInfo.InvariantCulture)))
            .ToList();
        return new HorizonsFixture(rows);
    }

    /// <summary>First crossing of <paramref name="elevation"/> in the given direction, interpolated linearly.</summary>
    public DateTime? Crossing(double elevation, bool rising)
    {
        for (var index = 1; index < Rows.Count; index++)
        {
            var a = Rows[index - 1].Elevation - elevation;
            var b = Rows[index].Elevation - elevation;
            if (rising ? a < 0 && b >= 0 : a >= 0 && b < 0)
            {
                return Rows[index - 1].Utc.AddMinutes(a / (a - b));
            }
        }

        return null;
    }

    /// <summary>Time and elevation of the highest point, refined with a parabola through the three highest samples.</summary>
    public (DateTime Utc, double Elevation) Maximum()
    {
        var best = 0;
        for (var index = 1; index < Rows.Count; index++)
        {
            if (Rows[index].Elevation > Rows[best].Elevation)
            {
                best = index;
            }
        }

        if (best == 0 || best == Rows.Count - 1)
        {
            return (Rows[best].Utc, Rows[best].Elevation);
        }

        var (y0, y1, y2) = (Rows[best - 1].Elevation, Rows[best].Elevation, Rows[best + 1].Elevation);
        var offsetMinutes = 0.5 * (y0 - y2) / (y0 - 2 * y1 + y2);
        var peak = y1 - 0.25 * (y0 - y2) * offsetMinutes;
        return (Rows[best].Utc.AddMinutes(offsetMinutes), peak);
    }

    public double MinimumElevation => Rows.Min(row => row.Elevation);
}
