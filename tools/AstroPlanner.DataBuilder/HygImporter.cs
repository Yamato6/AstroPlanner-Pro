using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Converts the HYG database (v4.x CSV) into star entries down to a limiting magnitude
/// (spec §16: magnitude 9). The Sun (HYG id 0) is skipped.
/// </summary>
internal static class HygImporter
{
    public const double DefaultLimitingMagnitude = 9.0;

    public static IReadOnlyList<StarEntry> Import(
        IEnumerable<IReadOnlyDictionary<string, string>> rows,
        double limitingMagnitude = DefaultLimitingMagnitude)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var stars = new List<StarEntry>();
        foreach (var row in rows)
        {
            var id = int.Parse(row["id"], CultureInfo.InvariantCulture);
            var magnitude = ParseOptional(row["mag"]);
            if (id == 0 || magnitude is null || magnitude > limitingMagnitude)
            {
                continue;
            }

            var coordinates = new EquatorialCoordinates(
                NormalizeHours(double.Parse(row["ra"], CultureInfo.InvariantCulture)),
                double.Parse(row["dec"], CultureInfo.InvariantCulture));

            stars.Add(new StarEntry(
                HygId: id,
                Hip: ParseInt(row["hip"]),
                Hd: ParseInt(row["hd"]),
                Hr: ParseInt(row["hr"]),
                ProperName: Text(row["proper"]),
                Bayer: Text(row["bayer"]),
                Flamsteed: ParseInt(row["flam"]),
                Constellation: ConstellationLookup.FromJ2000(coordinates).Abbreviation,
                RightAscensionHours: coordinates.RightAscensionHours,
                DeclinationDegrees: coordinates.DeclinationDegrees,
                Magnitude: magnitude.Value,
                ColorIndex: ParseOptional(row["ci"]),
                ProperMotionRaMas: ParseOptional(row["pmra"]),
                ProperMotionDecMas: ParseOptional(row["pmdec"])));
        }

        return [.. stars.OrderBy(s => s.HygId)];
    }

    private static double NormalizeHours(double hours) => hours >= 24 ? hours - 24 : hours < 0 ? hours + 24 : hours;

    private static string? Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static double? ParseOptional(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
}
