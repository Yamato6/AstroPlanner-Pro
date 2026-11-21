using System.Globalization;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Satellites;

/// <summary>
/// Reads a classic two-line element set (fixed columns, NORAD format). The app downloads OMM
/// (JSON) instead, since five-digit catalog numbers ran out in 2026; this reader serves the SGP4
/// verification set and anyone pasting a TLE.
/// </summary>
public static class TwoLineElements
{
    /// <summary>The elements of <paramref name="line1"/>/<paramref name="line2"/>, or null if they are not a valid set.</summary>
    public static SatelliteElements? Parse(string name, string line1, string line2)
    {
        ArgumentNullException.ThrowIfNull(line1);
        ArgumentNullException.ThrowIfNull(line2);
        if (line1.Length < 61 || line2.Length < 63 || line1[0] != '1' || line2[0] != '2')
        {
            return null;
        }

        try
        {
            var catalogNumber = int.Parse(line1.AsSpan(2, 5), NumberStyles.Integer, CultureInfo.InvariantCulture);
            var designator = line1.Substring(9, 8).Trim();
            var year = int.Parse(line1.AsSpan(18, 2), NumberStyles.Integer, CultureInfo.InvariantCulture);
            var dayOfYear = double.Parse(line1.AsSpan(20, 12), NumberStyles.Float, CultureInfo.InvariantCulture);
            var fullYear = year < 57 ? 2000 + year : 1900 + year;
            var epoch = new DateTime(fullYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks((long)Math.Round((dayOfYear - 1) * TimeSpan.TicksPerDay));

            return new SatelliteElements(
                catalogNumber,
                string.IsNullOrWhiteSpace(name) ? catalogNumber.ToString(CultureInfo.InvariantCulture) : name.Trim(),
                designator,
                epoch,
                MeanMotionRevPerDay: Number(line2, 52, 11),
                Eccentricity: double.Parse("0." + line2.Substring(26, 7).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture),
                InclinationDegrees: Number(line2, 8, 8),
                AscendingNodeDegrees: Number(line2, 17, 8),
                ArgumentOfPerigeeDegrees: Number(line2, 34, 8),
                MeanAnomalyDegrees: Number(line2, 43, 8),
                BStar: Exponent(line1.Substring(53, 8)));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static double Number(string line, int start, int length) =>
        double.Parse(line.AsSpan(start, length), NumberStyles.Float, CultureInfo.InvariantCulture);

    /// <summary>The TLE's implied-decimal exponent notation: " 28098-4" = 0.28098e-4.</summary>
    private static double Exponent(string field)
    {
        var text = field.Trim();
        if (text.Length < 2)
        {
            return 0;
        }

        var exponentAt = text.LastIndexOfAny(['-', '+']);
        if (exponentAt <= 0)
        {
            return double.Parse("0." + text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        var mantissa = text[..exponentAt];
        var sign = mantissa.StartsWith('-') ? "-" : "";
        mantissa = mantissa.TrimStart('-', '+');
        var exponent = int.Parse(text.AsSpan(exponentAt), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return double.Parse($"{sign}0.{mantissa}e{exponent}", NumberStyles.Float, CultureInfo.InvariantCulture);
    }
}
