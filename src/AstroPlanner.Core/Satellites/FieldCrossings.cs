using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Satellites;

/// <summary>
/// The camera field of an exposure block: centre (J2000), size and rotation (the image's up side
/// from north through east, as <see cref="Imaging.FramingRequest"/>).
/// </summary>
public sealed record CameraField(EquatorialCoordinates CenterJ2000, double WidthDegrees, double HeightDegrees, double RotationDegrees = 0);

/// <summary>
/// Sunlit satellites crossing a camera field during a block: they would leave a trail in the
/// frames. Each satellite is followed every 10 s, and wherever it comes within reach of the field
/// (half the diagonal plus 12°, more than a low satellite moves in 10 s) every 0.1 s, so that
/// even a 0.2° telescope field crossed in a fraction of a second is caught.
/// </summary>
public static class FieldCrossings
{
    private static readonly TimeSpan CoarseStep = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FineStep = TimeSpan.FromSeconds(0.1);
    private const double ReachMarginDegrees = 12;

    public static IReadOnlyList<FieldCrossing> Find(
        IEnumerable<TrackedSatellite> satellites,
        ObserverLocation observer,
        CameraField field,
        DateTime startUtc,
        DateTime endUtc)
    {
        ArgumentNullException.ThrowIfNull(satellites);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(field);
        startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        if (endUtc <= startUtc)
        {
            return [];
        }

        // The field's centre in the frame of date the satellites are computed in.
        var center = Precession.FromJ2000(field.CenterJ2000, startUtc + ((endUtc - startUtc) / 2));
        var halfDiagonal = AstronomyMath.RadiansToDegreesValue(Math.Atan(Math.Sqrt(
            Math.Pow(Math.Tan(AstronomyMath.DegreesToRadiansValue(field.WidthDegrees / 2)), 2)
            + Math.Pow(Math.Tan(AstronomyMath.DegreesToRadiansValue(field.HeightDegrees / 2)), 2))));
        var crossings = new List<FieldCrossing>();
        foreach (var satellite in satellites)
        {
            try
            {
                crossings.AddRange(FindFor(satellite, observer, field, center, halfDiagonal, startUtc, endUtc));
            }
            catch (SatellitePropagationException)
            {
                // A decayed orbit crosses nothing.
            }
        }

        return [.. crossings.OrderBy(c => c.EnterUtc)];
    }

    private static List<FieldCrossing> FindFor(
        TrackedSatellite satellite, ObserverLocation observer, CameraField field, EquatorialCoordinates center,
        double halfDiagonal, DateTime startUtc, DateTime endUtc)
    {
        var result = new List<FieldCrossing>();
        var scannedUntil = DateTime.MinValue;
        for (var t = startUtc; t <= endUtc; t += CoarseStep)
        {
            var observation = SatelliteCalculator.Observe(satellite, observer, t);
            if (observation.Horizontal.AltitudeDegrees < -ReachMarginDegrees
                || AstronomyMath.AngularSeparationDegrees(observation.Equatorial, center) > halfDiagonal + ReachMarginDegrees)
            {
                continue;
            }

            // Fine scan of the neighbourhood not scanned yet.
            var from = Max(Max(t - CoarseStep, startUtc), scannedUntil);
            var to = Min(t + CoarseStep, endUtc);
            if (from >= to)
            {
                continue;
            }

            scannedUntil = to;
            DateTime? enter = null;
            double? brightest = null;
            var lit = false;
            var previous = from;
            for (var s = from; ; s += FineStep)
            {
                if (s > to)
                {
                    s = to;
                }

                var fine = SatelliteCalculator.Observe(satellite, observer, s);
                var inside = fine.Horizontal.AltitudeDegrees > 0 && Inside(fine.Equatorial, center, field);
                if (inside)
                {
                    enter ??= s;
                    lit |= fine.Illumination != SatelliteIllumination.Umbra;
                    if (fine.Magnitude is { } m && (brightest is null || m < brightest))
                    {
                        brightest = m;
                    }
                }
                else if (enter is { } entered)
                {
                    if (lit)
                    {
                        result.Add(new FieldCrossing(satellite.CatalogNumber, satellite.Name, entered, previous, brightest));
                    }

                    enter = null;
                    brightest = null;
                    lit = false;
                }

                previous = s;
                if (s >= to)
                {
                    break;
                }
            }

            if (enter is { } open && lit)
            {
                // Still inside at the end of the scanned stretch: continue it.
                var exit = previous;
                while (exit < endUtc)
                {
                    var next = Min(exit + FineStep, endUtc);
                    var fine = SatelliteCalculator.Observe(satellite, observer, next);
                    if (!(fine.Horizontal.AltitudeDegrees > 0 && Inside(fine.Equatorial, center, field)))
                    {
                        break;
                    }

                    exit = next;
                }

                // The next stretch starts after the last inside sample, so the crossing is not counted twice.
                scannedUntil = exit + FineStep;
                result.Add(new FieldCrossing(satellite.CatalogNumber, satellite.Name, open, exit, brightest));
            }
        }

        return result;
    }

    /// <summary>Whether a position (frame of date) falls inside the rotated rectangle of the field (gnomonic projection).</summary>
    public static bool Inside(EquatorialCoordinates position, EquatorialCoordinates center, CameraField field)
    {
        var dec0 = AstronomyMath.DegreesToRadiansValue(center.DeclinationDegrees);
        var dec = AstronomyMath.DegreesToRadiansValue(position.DeclinationDegrees);
        var dRa = AstronomyMath.DegreesToRadiansValue((position.RightAscensionHours - center.RightAscensionHours) * 15);
        var cosC = (Math.Sin(dec0) * Math.Sin(dec)) + (Math.Cos(dec0) * Math.Cos(dec) * Math.Cos(dRa));
        if (cosC <= 1e-9)
        {
            return false;
        }

        var xi = Math.Cos(dec) * Math.Sin(dRa) / cosC;
        var eta = ((Math.Cos(dec0) * Math.Sin(dec)) - (Math.Sin(dec0) * Math.Cos(dec) * Math.Cos(dRa))) / cosC;
        var (sinRot, cosRot) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(field.RotationDegrees));
        var right = (eta * sinRot) - (xi * cosRot);
        var up = (xi * sinRot) + (eta * cosRot);
        return Math.Abs(right) <= Math.Tan(AstronomyMath.DegreesToRadiansValue(field.WidthDegrees / 2))
            && Math.Abs(up) <= Math.Tan(AstronomyMath.DegreesToRadiansValue(field.HeightDegrees / 2));
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
}
