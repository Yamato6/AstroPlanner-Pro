using System.Globalization;

namespace AstroPlanner.DataBuilder;

/// <summary>A pair of the Washington Double Star Catalog matched to a HYG star.</summary>
/// <param name="WdsId">WDS identifier, J2000 position "hhmmm±ddmm" (e.g. "19307+2758").</param>
/// <param name="Discoverer">Discoverer code and number (e.g. "STFA 43").</param>
/// <param name="Components">Components (e.g. "AB", "AB,CD"); empty for a plain pair.</param>
/// <param name="HygId">The HYG star the primary was matched to.</param>
/// <param name="LastYear">Year of the last measure, which <paramref name="PositionAngleDegrees"/> and <paramref name="SeparationArcsec"/> are from.</param>
internal sealed record DoubleStarEntry(
    string WdsId,
    string Discoverer,
    string Components,
    int HygId,
    int? FirstYear,
    int LastYear,
    double? PositionAngleDegrees,
    double SeparationArcsec,
    double PrimaryMagnitude,
    double SecondaryMagnitude,
    string? SpectralType);

/// <summary>
/// Reads the WDS summary file (<c>wdsweb_summ2.txt</c>, fixed columns, "WDS Bible" format) and
/// keeps the pairs a small telescope or a camera can split and show: a primary up to
/// <see cref="MaximumPrimaryMagnitude"/>, a secondary up to <see cref="MaximumSecondaryMagnitude"/>,
/// the last separation between <see cref="MinimumSeparationArcsec"/> and
/// <see cref="MaximumSeparationArcsec"/>, measured in <see cref="MinimumLastYear"/> or later. Each
/// pair is matched to the nearest HYG star within <see cref="MatchRadiusArcsec"/> of its primary
/// whose magnitude agrees within <see cref="MatchMagnitudeTolerance"/>.
/// </summary>
internal static class WdsImporter
{
    public const double MaximumPrimaryMagnitude = 7.0;
    public const double MaximumSecondaryMagnitude = 11.0;
    public const double MinimumSeparationArcsec = 0.3;
    public const double MaximumSeparationArcsec = 300;
    public const int MinimumLastYear = 1900;
    public const double MatchRadiusArcsec = 60;
    public const double MatchMagnitudeTolerance = 0.7;

    /// <summary>A pair of the file that passed the selection, before matching.</summary>
    internal sealed record WdsPair(
        string WdsId, string Discoverer, string Components, int? FirstYear, int LastYear, double? PositionAngleDegrees,
        double SeparationArcsec, double PrimaryMagnitude, double SecondaryMagnitude, string? SpectralType,
        double RightAscensionHours, double DeclinationDegrees);

    public static (IReadOnlyList<DoubleStarEntry> Pairs, int Selected, int Unmatched) Import(
        IEnumerable<string> lines, IReadOnlyList<StarEntry> stars)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(stars);

        var selected = lines.Select(Parse).OfType<WdsPair>().Where(Keep).ToList();
        var index = new StarIndex(stars);
        var matched = new List<DoubleStarEntry>();
        var keys = new HashSet<(string, string, string)>();
        var unmatched = 0;
        foreach (var pair in selected)
        {
            if (index.Nearest(pair.RightAscensionHours, pair.DeclinationDegrees, pair.PrimaryMagnitude) is not { } star)
            {
                unmatched++;
                continue;
            }

            if (keys.Add((pair.WdsId, pair.Discoverer, pair.Components)))
            {
                matched.Add(new DoubleStarEntry(pair.WdsId, pair.Discoverer, pair.Components, star.HygId, pair.FirstYear, pair.LastYear,
                    pair.PositionAngleDegrees, pair.SeparationArcsec, pair.PrimaryMagnitude, pair.SecondaryMagnitude, pair.SpectralType));
            }
        }

        return ([.. matched.OrderBy(p => p.HygId).ThenBy(p => p.SeparationArcsec).ThenBy(p => p.WdsId, StringComparer.Ordinal)
            .ThenBy(p => p.Discoverer, StringComparer.Ordinal).ThenBy(p => p.Components, StringComparer.Ordinal)], selected.Count, unmatched);
    }

    private static bool Keep(WdsPair pair) =>
        pair.PrimaryMagnitude <= MaximumPrimaryMagnitude
        && pair.SecondaryMagnitude <= MaximumSecondaryMagnitude
        && pair.SeparationArcsec is >= MinimumSeparationArcsec and <= MaximumSeparationArcsec
        && pair.LastYear >= MinimumLastYear;

    /// <summary>One line of the summary file, or null for headers and lines that cannot be read.</summary>
    internal static WdsPair? Parse(string line)
    {
        if (line.Length < 69 || !char.IsAsciiDigit(line[0]) || line[5] is not ('+' or '-'))
        {
            return null;
        }

        var lastYear = Int(line, 29, 32);
        var separation = Number(line, 53, 57);
        var primary = Number(line, 59, 63);
        var secondary = Number(line, 65, 69);
        if (lastYear is not { } year || separation is not { } rho || primary is not { } m1 || secondary is not { } m2
            || rho <= 0 || rho >= 999)
        {
            return null;
        }

        var position = line.Length >= 130 ? PrecisePosition(line.Substring(112, 18)) : null;
        position ??= CoarsePosition(line[..10]);
        if (position is not { } coordinates)
        {
            return null;
        }

        var angle = Int(line, 43, 45);
        return new WdsPair(line[..10], Field(line, 11, 17), Field(line, 18, 22), Int(line, 24, 27), year,
            angle is >= 0 and <= 360 ? angle : null, rho, m1, m2,
            line.Length >= 79 && Field(line, 71, 79) is { Length: > 0 } spectral ? spectral : null,
            coordinates.Ra, coordinates.Dec);
    }

    /// <summary>"193043.29+275734.9" → hours and degrees.</summary>
    private static (double Ra, double Dec)? PrecisePosition(string text)
    {
        if (text.Length < 18 || text[9] is not ('+' or '-')
            || !double.TryParse(text.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || !double.TryParse(text.AsSpan(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            || !double.TryParse(text.AsSpan(4, 5), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            || !double.TryParse(text.AsSpan(10, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
            || !double.TryParse(text.AsSpan(12, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dm)
            || !double.TryParse(text.AsSpan(14, 4), NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
        {
            return null;
        }

        var dec = d + (dm / 60) + (ds / 3600);
        return (h + (m / 60) + (s / 3600), text[9] == '-' ? -dec : dec);
    }

    /// <summary>"19307+2758" (hours, minutes and tenths; degrees and minutes) → hours and degrees.</summary>
    private static (double Ra, double Dec)? CoarsePosition(string text)
    {
        if (!double.TryParse(text.AsSpan(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || !double.TryParse(text.AsSpan(2, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tenths)
            || !double.TryParse(text.AsSpan(6, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
            || !double.TryParse(text.AsSpan(8, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var dm))
        {
            return null;
        }

        var dec = d + (dm / 60);
        return (h + (tenths / 600), text[5] == '-' ? -dec : dec);
    }

    /// <summary>Text of the 1-based inclusive columns <paramref name="first"/>–<paramref name="last"/>.</summary>
    private static string Field(string line, int first, int last) =>
        line.Length < first ? "" : line.Substring(first - 1, Math.Min(last, line.Length) - first + 1).Trim();

    private static int? Int(string line, int first, int last) =>
        int.TryParse(Field(line, first, last), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static double? Number(string line, int first, int last)
    {
        var text = Field(line, first, last).TrimEnd('.');
        return text.Length > 0 && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>HYG stars bucketed by declination arcminute for the nearest-neighbour search.</summary>
    private sealed class StarIndex
    {
        private readonly Dictionary<int, List<StarEntry>> _bands = [];

        public StarIndex(IEnumerable<StarEntry> stars)
        {
            foreach (var star in stars)
            {
                var band = Band(star.DeclinationDegrees);
                if (!_bands.TryGetValue(band, out var list))
                {
                    _bands[band] = list = [];
                }

                list.Add(star);
            }
        }

        private static int Band(double declination) => (int)Math.Floor(declination * 60);

        public StarEntry? Nearest(double raHours, double decDegrees, double magnitude)
        {
            StarEntry? best = null;
            var bestDistance = MatchRadiusArcsec;
            var band = Band(decDegrees);
            for (var b = band - 1; b <= band + 1; b++)
            {
                if (!_bands.TryGetValue(b, out var list))
                {
                    continue;
                }

                foreach (var star in list)
                {
                    if (Math.Abs(star.Magnitude - magnitude) > MatchMagnitudeTolerance)
                    {
                        continue;
                    }

                    var distance = SeparationArcsec(raHours, decDegrees, star.RightAscensionHours, star.DeclinationDegrees);
                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        best = star;
                    }
                }
            }

            return best;
        }

        private static double SeparationArcsec(double ra1, double dec1, double ra2, double dec2)
        {
            const double radians = Math.PI / 180;
            var dRa = (ra1 - ra2) * 15 * radians;
            var (s1, c1) = Math.SinCos(dec1 * radians);
            var (s2, c2) = Math.SinCos(dec2 * radians);
            var cos = (s1 * s2) + (c1 * c2 * Math.Cos(dRa));
            return Math.Acos(Math.Clamp(cos, -1, 1)) / radians * 3600;
        }
    }
}
