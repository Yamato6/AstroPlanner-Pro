using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Planning;

/// <summary>
/// What a target does during one observing night, in local time.
/// </summary>
/// <param name="MaximumAltitudeDegrees">Highest apparent altitude inside the night window (sunset → sunrise).</param>
/// <param name="TransitLocal">Upper transit (culmination) if it happens inside the night window.</param>
/// <param name="TimeAboveMinimumInDarkness">
/// Time during astronomical darkness with the target above both the minimum altitude and the
/// local horizon (horizon mask).
/// </param>
/// <param name="BestWindowStartLocal">Start of the longest such interval; null if there is none.</param>
/// <param name="BestWindowEndLocal">End of the longest such interval.</param>
public sealed record NightEphemeris(
    double MaximumAltitudeDegrees,
    DateTime? TransitLocal,
    TimeSpan TimeAboveMinimumInDarkness,
    DateTime? BestWindowStartLocal,
    DateTime? BestWindowEndLocal)
{
    public bool IsObservableInDarkness => TimeAboveMinimumInDarkness > TimeSpan.Zero;
}

/// <summary>Night, site and constraints shared by every target of a <see cref="NightEphemerisCalculator"/> run.</summary>
public sealed record NightEphemerisRequest(
    ObserverLocation Observer,
    TimeZoneInfo TimeZone,
    NightInfo Night,
    double MinimumAltitudeDegrees,
    HorizonMask? HorizonMask = null);

/// <summary>
/// Precomputes the <see cref="NightEphemeris"/> of a whole catalog for one night. Fixed
/// (deep-sky) objects take a fast path: they are precessed once for the night and their
/// altitude is evaluated with the shared sidereal time of each sample, with the transit solved
/// analytically. Moving bodies (planets) go through <see cref="IAstronomyService"/>.
/// Crossings are interpolated linearly between samples (error of a few seconds with the
/// default 5-minute step).
/// </summary>
public sealed class NightEphemerisCalculator(IAstronomyService astronomy)
{
    /// <summary>Sampling step of the night.</summary>
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(5);

    /// <summary>Sidereal rotation of the Earth, degrees per day.</summary>
    private const double SiderealDegreesPerDay = 360.98564736629;

    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Below the minimum altitude by more than this, the horizon mask is not evaluated.</summary>
    private const double AzimuthMarginDegrees = 10.0;

    private readonly IAstronomyService _astronomy = astronomy ?? throw new ArgumentNullException(nameof(astronomy));

    /// <summary>Ephemerides of <paramref name="targets"/> (same order) for the night of <paramref name="request"/>.</summary>
    public IReadOnlyList<NightEphemeris> Calculate(
        IReadOnlyList<CelestialObject> targets,
        NightEphemerisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(request);

        var night = NightSamples.Create(request);
        var results = new NightEphemeris[targets.Count];

        Parallel.For(0, targets.Count,
            new ParallelOptions { CancellationToken = cancellationToken },
            index =>
            {
                var target = targets[index];
                results[index] = target.IsPlanet
                    ? CalculateMoving(target, night, request)
                    : CalculateFixed(target, night, request);
            });

        return results;
    }

    /// <summary>Ephemeris of a single target.</summary>
    public NightEphemeris Calculate(CelestialObject target, NightEphemerisRequest request) =>
        Calculate([target], request)[0];

    private static NightEphemeris CalculateFixed(CelestialObject target, NightSamples night, NightEphemerisRequest request)
    {
        var ofDate = Precession.Precess(target.Coordinates, Precession.J2000JulianDate, night.MiddleJulianDate);
        var rightAscensionDegrees = ofDate.RightAscensionHours * 15.0;
        var declination = ofDate.DeclinationDegrees * DegreesToRadians;
        var sinDeclination = Math.Sin(declination);
        var cosDeclination = Math.Cos(declination);

        var altitudes = new double[night.Count];
        var azimuths = new double[night.Count];
        for (var k = 0; k < night.Count; k++)
        {
            var hourAngle = (night.LocalSiderealTimeDegrees[k] - rightAscensionDegrees) * DegreesToRadians;
            var sinAltitude = Math.Clamp(
                night.SinLatitude * sinDeclination + night.CosLatitude * cosDeclination * Math.Cos(hourAngle), -1.0, 1.0);
            var altitude = Math.Asin(sinAltitude) / DegreesToRadians;
            altitudes[k] = Refraction.ApparentAltitude(altitude, night.RefractionScale);

            // The horizon only matters near or above the minimum altitude; far below it the
            // azimuth is not needed (NaN = use the minimum altitude alone).
            azimuths[k] = double.NaN;
            if (night.NeedsAzimuth && altitudes[k] >= request.MinimumAltitudeDegrees - AzimuthMarginDegrees)
            {
                // Azimuth measured from north towards east, as in CoordinateTransformer.
                var azimuth = Math.Atan2(
                    -cosDeclination * Math.Sin(hourAngle),
                    sinDeclination * night.CosLatitude - cosDeclination * Math.Cos(hourAngle) * night.SinLatitude);
                azimuths[k] = AstronomyMath.NormalizeDegrees(azimuth / DegreesToRadians);
            }
        }

        // Upper transit: the first instant after the start of the window when LST = RA.
        DateTime? transitUtc = null;
        var maximum = altitudes.Max();
        var daysToTransit = AstronomyMath.NormalizeDegrees(rightAscensionDegrees - night.LocalSiderealTimeDegrees[0])
                            / SiderealDegreesPerDay;
        var transit = night.Times[0].AddDays(daysToTransit);
        if (transit <= night.Times[^1])
        {
            transitUtc = transit;
            var culmination = Refraction.ApparentAltitude(90.0 - Math.Abs(request.Observer.LatitudeDegrees - ofDate.DeclinationDegrees), night.RefractionScale);
            maximum = Math.Max(maximum, culmination);
        }

        return Summarize(night, request, altitudes, azimuths, maximum, transitUtc);
    }

    private NightEphemeris CalculateMoving(CelestialObject target, NightSamples night, NightEphemerisRequest request)
    {
        var altitudes = new double[night.Count];
        var azimuths = new double[night.Count];
        for (var k = 0; k < night.Count; k++)
        {
            var horizontal = _astronomy.GetHorizontal(target, request.Observer, night.Times[k]);
            altitudes[k] = horizontal.AltitudeDegrees;
            azimuths[k] = horizontal.AzimuthDegrees;
        }

        // Planets move slowly: the highest sample, refined around it, is the culmination.
        double Altitude(DateTime utc) => _astronomy.GetHorizontal(target, request.Observer, utc).AltitudeDegrees;
        var samples = night.Times.Select((time, k) => (time, altitudes[k])).ToList();
        var peak = EventFinder.FindMaximum(Altitude, samples);
        var isInterior = peak.Time > night.Times[0] && peak.Time < night.Times[^1];

        return Summarize(night, request, altitudes, azimuths, peak.Value, isInterior ? peak.Time : null);
    }

    private static NightEphemeris Summarize(
        NightSamples night,
        NightEphemerisRequest request,
        double[] altitudes,
        double[] azimuths,
        double maximumAltitude,
        DateTime? transitUtc)
    {
        var mask = request.HorizonMask;
        var margins = new double[night.Count];
        for (var k = 0; k < night.Count; k++)
        {
            var threshold = night.NeedsAzimuth && !double.IsNaN(azimuths[k])
                ? Math.Max(request.MinimumAltitudeDegrees, mask!.AltitudeAt(azimuths[k]))
                : request.MinimumAltitudeDegrees;
            margins[k] = altitudes[k] - threshold;
        }

        // Walk the dark part of the night and collect the intervals with a positive margin,
        // interpolating each crossing linearly between samples.
        var total = TimeSpan.Zero;
        DateTime? bestStart = null, bestEnd = null;
        DateTime? runStart = null;
        for (var k = 0; k < night.Count; k++)
        {
            var visible = night.IsDark[k] && margins[k] >= 0;
            if (visible && runStart is null)
            {
                runStart = k > 0 && night.IsDark[k - 1] ? Interpolate(night, margins, k - 1, k) : night.Times[k];
            }
            else if (!visible && runStart is not null)
            {
                var runEnd = night.IsDark[k] ? Interpolate(night, margins, k - 1, k) : night.Times[k - 1];
                Close(runStart.Value, runEnd);
                runStart = null;
            }
        }

        if (runStart is not null)
        {
            Close(runStart.Value, night.Times[^1]);
        }

        DateTime? Local(DateTime? utc) => utc is { } value ? TimeZoneConversion.ToLocal(value, request.TimeZone) : null;

        return new NightEphemeris(maximumAltitude, Local(transitUtc), total, Local(bestStart), Local(bestEnd));

        void Close(DateTime start, DateTime end)
        {
            var length = end - start;
            total += length;
            if (bestStart is null || length > bestEnd!.Value - bestStart.Value)
            {
                bestStart = start;
                bestEnd = end;
            }
        }
    }

    /// <summary>Instant where the margin crosses zero between samples <paramref name="a"/> and <paramref name="b"/>.</summary>
    private static DateTime Interpolate(NightSamples night, double[] margins, int a, int b)
    {
        var denominator = margins[a] - margins[b];
        var fraction = denominator == 0 ? 0.5 : Math.Clamp(margins[a] / denominator, 0.0, 1.0);
        return night.Times[a] + (night.Times[b] - night.Times[a]) * fraction;
    }

    /// <summary>Sample instants of the night, shared by every target.</summary>
    private sealed class NightSamples
    {
        public required DateTime[] Times { get; init; }
        public required double[] LocalSiderealTimeDegrees { get; init; }
        public required bool[] IsDark { get; init; }
        public required double SinLatitude { get; init; }
        public required double CosLatitude { get; init; }
        public required double MiddleJulianDate { get; init; }
        public required bool NeedsAzimuth { get; init; }
        public required double RefractionScale { get; init; }
        public int Count => Times.Length;

        public static NightSamples Create(NightEphemerisRequest request)
        {
            var night = request.Night;
            var startUtc = TimeZoneConversion.ToUtc(night.WindowStartLocal, request.TimeZone);
            var endUtc = TimeZoneConversion.ToUtc(night.WindowEndLocal, request.TimeZone);
            if (endUtc <= startUtc)
            {
                endUtc = startUtc.AddMinutes(1);
            }

            DateTime? darkStart = night.AstronomicalDuskLocal is { } dusk ? TimeZoneConversion.ToUtc(dusk, request.TimeZone) : null;
            DateTime? darkEnd = night.AstronomicalDawnLocal is { } dawn ? TimeZoneConversion.ToUtc(dawn, request.TimeZone) : null;

            var times = new List<DateTime>();
            for (var time = startUtc; time < endUtc; time += Step)
            {
                times.Add(time);
            }

            times.Add(endUtc);

            // Dusk and dawn are samples too, so that the dark part of the night is exact.
            foreach (var edge in new[] { darkStart, darkEnd })
            {
                if (edge is { } value && value > startUtc && value < endUtc && !times.Contains(value))
                {
                    times.Add(value);
                }
            }

            times.Sort();

            var latitude = request.Observer.LatitudeDegrees * DegreesToRadians;
            return new NightSamples
            {
                Times = [.. times],
                LocalSiderealTimeDegrees = [.. times.Select(t =>
                    AstronomyMath.CalculateLocalSiderealTimeDegrees(t, request.Observer.LongitudeDegrees))],
                IsDark = [.. times.Select(t => darkStart is not null && darkEnd is not null && t >= darkStart && t <= darkEnd)],
                SinLatitude = Math.Sin(latitude),
                CosLatitude = Math.Cos(latitude),
                MiddleJulianDate = AstronomyMath.ToJulianDate(startUtc + (endUtc - startUtc) / 2),
                NeedsAzimuth = request.HorizonMask is { IsFlat: false },
                RefractionScale = Refraction.PressureScale(request.Observer.ElevationMeters)
            };
        }
    }
}
