using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Satellites;

/// <summary>
/// A satellite as the app follows it: its SGP4 propagator and its intrinsic magnitude, when known.
/// </summary>
public sealed class TrackedSatellite
{
    public TrackedSatellite(SatelliteElements elements)
    {
        Propagator = Sgp4.Create(elements);
        IntrinsicMagnitude = SatelliteMagnitudes.Intrinsic(elements.CatalogNumber);
    }

    public Sgp4 Propagator { get; }
    public SatelliteElements Elements => Propagator.Elements;
    public int CatalogNumber => Elements.CatalogNumber;
    public string Name => Elements.Name;

    /// <summary>Magnitude at 1000 km and full phase (McCants), null when unknown.</summary>
    public double? IntrinsicMagnitude { get; }

    /// <summary>
    /// Followable satellites of <paramref name="catalog"/>: near-Earth orbits only, and modules
    /// docked to a station (same elements, e.g. ISS (NAUKA)) collapsed into the lowest catalog number.
    /// </summary>
    public static IReadOnlyList<TrackedSatellite> From(IEnumerable<SatelliteElements> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var result = new List<TrackedSatellite>();
        foreach (var group in catalog
                     .Where(e => e.IsNearEarth && e.Eccentricity is >= 0 and < 1 && e.MeanMotionRevPerDay > 0)
                     .GroupBy(e => (e.EpochUtc, e.MeanMotionRevPerDay, e.MeanAnomalyDegrees, e.AscendingNodeDegrees)))
        {
            result.Add(new TrackedSatellite(group.MinBy(e => e.CatalogNumber)!));
        }

        return [.. result.OrderBy(s => s.CatalogNumber)];
    }
}

/// <summary>Intrinsic magnitudes from McCants' quicksat.mag (embedded <c>satellite_magnitudes.dat</c>).</summary>
public static class SatelliteMagnitudes
{
    private static readonly Lazy<Dictionary<int, double>> Table = new(Load);

    public static double? Intrinsic(int catalogNumber) =>
        Table.Value.TryGetValue(catalogNumber, out var magnitude) ? magnitude : null;

    private static Dictionary<int, double> Load()
    {
        using var stream = typeof(SatelliteMagnitudes).Assembly.GetManifestResourceStream("AstroPlanner.Core.Data.satellite_magnitudes.dat")
            ?? throw new InvalidOperationException("The satellite magnitude table is missing.");
        using var reader = new StreamReader(stream);
        var table = new Dictionary<int, double>();
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (line.StartsWith('#') || parts.Length != 2)
            {
                continue;
            }

            table[int.Parse(parts[0], CultureInfo.InvariantCulture)] = double.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        return table;
    }
}

/// <summary>
/// Where a satellite is for an observer, whether the Sun lights it, how bright it is, and its
/// passes. The SGP4 TEME frame is turned into the Earth-fixed one with the Greenwich mean sidereal
/// time (polar motion and UT1 − UTC, below 1 s, are ignored: ~0.01° for a low satellite).
/// </summary>
public static class SatelliteCalculator
{
    /// <summary>Default minimum altitude of a pass.</summary>
    public const double PassMinimumAltitudeDegrees = 10;

    /// <summary>The observer's sky counts as dark for satellite watching from nautical twilight on.</summary>
    public const double DarkSkySunAltitudeDegrees = -6;

    private const double EarthEquatorialRadiusKm = 6378.137;
    private const double EarthFlattening = 1 / 298.257223563;
    private const double SunRadiusKm = 696_000;
    private const double EarthRotationRadPerSecond = 7.292115e-5;

    /// <summary>Altitude sampling step of the pass search: a pass above 10° lasts longer.</summary>
    private static readonly TimeSpan PassSearchStep = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan VisibilityStep = TimeSpan.FromSeconds(5);

    /// <summary>The satellite seen from <paramref name="observer"/> at <paramref name="utc"/>.</summary>
    public static SatelliteObservation Observe(TrackedSatellite satellite, ObserverLocation observer, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(satellite);
        ArgumentNullException.ThrowIfNull(observer);
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        var state = satellite.Propagator.Propagate(utc);
        var theta = AstronomyMath.DegreesToRadiansValue(AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(utc));
        var (sinTheta, cosTheta) = Math.SinCos(theta);
        var observerEcef = ObserverEcef(observer);
        var observerTeme = new Vector3d(
            (cosTheta * observerEcef.X) - (sinTheta * observerEcef.Y),
            (sinTheta * observerEcef.X) + (cosTheta * observerEcef.Y),
            observerEcef.Z);
        var rho = state.PositionKm - observerTeme;
        var range = rho.Length;

        // Topocentric RA/Dec (TEME ≈ true equator of date; the equation of the equinoxes is ~1″).
        var ra = AstronomyMath.NormalizeDegrees(AstronomyMath.RadiansToDegreesValue(Math.Atan2(rho.Y, rho.X)));
        var dec = AstronomyMath.RadiansToDegreesValue(Math.Asin(rho.Z / range));

        // Altitude/azimuth in the local east-north-up frame.
        var rhoEcef = new Vector3d((cosTheta * rho.X) + (sinTheta * rho.Y), (-sinTheta * rho.X) + (cosTheta * rho.Y), rho.Z);
        var (sinLat, cosLat) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(observer.LatitudeDegrees));
        var (sinLon, cosLon) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(observer.LongitudeDegrees));
        var east = (-sinLon * rhoEcef.X) + (cosLon * rhoEcef.Y);
        var north = (-sinLat * cosLon * rhoEcef.X) - (sinLat * sinLon * rhoEcef.Y) + (cosLat * rhoEcef.Z);
        var up = (cosLat * cosLon * rhoEcef.X) + (cosLat * sinLon * rhoEcef.Y) + (sinLat * rhoEcef.Z);
        var altitude = AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(up / range, -1, 1)));
        var azimuth = AstronomyMath.NormalizeDegrees(AstronomyMath.RadiansToDegreesValue(Math.Atan2(east, north)));

        var observerVelocity = new Vector3d(-EarthRotationRadPerSecond * observerTeme.Y, EarthRotationRadPerSecond * observerTeme.X, 0);
        var relativeVelocity = state.VelocityKmPerSecond - observerVelocity;
        var rangeRate = Dot(rho, relativeVelocity) / range;

        var sun = SunTeme(utc);
        var illumination = Illumination(state.PositionKm, sun);
        var toSun = sun - state.PositionKm;
        var toObserver = rho * -1;
        var phase = AngleDegrees(toSun, toObserver);
        var magnitude = illumination == SatelliteIllumination.Umbra ? null : Magnitude(satellite.IntrinsicMagnitude, range, phase);

        return new SatelliteObservation(utc, new HorizontalCoordinates(altitude, azimuth), new EquatorialCoordinates(ra / 15, dec),
            range, rangeRate, illumination, phase, magnitude);
    }

    /// <summary>
    /// Visual magnitude from the intrinsic one (1000 km, full phase): range and the lit fraction of a
    /// sphere, (1 + cos φ) / 2 — McCants' convention, in which the "standard" magnitude at 90° is 0.75 fainter.
    /// </summary>
    public static double? Magnitude(double? intrinsicMagnitude, double rangeKm, double phaseAngleDegrees)
    {
        if (intrinsicMagnitude is not { } intrinsic)
        {
            return null;
        }

        var fraction = Math.Max((1 + Math.Cos(AstronomyMath.DegreesToRadiansValue(phaseAngleDegrees))) / 2, 1e-3);
        return intrinsic + (5 * Math.Log10(rangeKm / 1000)) - (2.5 * Math.Log10(fraction));
    }

    /// <summary>
    /// Sunlight on a satellite at <paramref name="satelliteKm"/> (geocentric) with the Sun at
    /// <paramref name="sunKm"/>: a conical shadow from the apparent discs of the Earth and the Sun as
    /// seen from the satellite (the atmosphere's extra ~0.5 % of shadow radius is ignored).
    /// </summary>
    public static SatelliteIllumination Illumination(Vector3d satelliteKm, Vector3d sunKm)
    {
        var toSun = sunKm - satelliteKm;
        var toEarth = satelliteKm * -1;
        var earthRadius = Math.Asin(Math.Min(EarthEquatorialRadiusKm / satelliteKm.Length, 1));
        var sunRadius = Math.Asin(SunRadiusKm / toSun.Length);
        var separation = AstronomyMath.DegreesToRadiansValue(AngleDegrees(toSun, toEarth));

        if (separation >= earthRadius + sunRadius)
        {
            return SatelliteIllumination.Sunlit;
        }

        return earthRadius > sunRadius && separation <= earthRadius - sunRadius
            ? SatelliteIllumination.Umbra
            : SatelliteIllumination.Penumbra;
    }

    /// <summary>
    /// Passes above <paramref name="minimumAltitudeDegrees"/> between <paramref name="startUtc"/> and
    /// <paramref name="endUtc"/> (a pass under way at either end is cut there), with the part in
    /// which the satellite is sunlit and the sky dark. A satellite whose orbit has decayed has none.
    /// </summary>
    public static IReadOnlyList<SatellitePass> FindPasses(
        TrackedSatellite satellite,
        ObserverLocation observer,
        DateTime startUtc,
        DateTime endUtc,
        double minimumAltitudeDegrees = PassMinimumAltitudeDegrees)
    {
        ArgumentNullException.ThrowIfNull(satellite);
        ArgumentNullException.ThrowIfNull(observer);
        startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);

        try
        {
            return FindPassesCore(satellite, observer, startUtc, endUtc, minimumAltitudeDegrees);
        }
        catch (SatellitePropagationException)
        {
            return [];
        }
    }

    private static List<SatellitePass> FindPassesCore(
        TrackedSatellite satellite, ObserverLocation observer, DateTime startUtc, DateTime endUtc, double minimumAltitudeDegrees)
    {
        double Altitude(DateTime t) => Observe(satellite, observer, t).Horizontal.AltitudeDegrees;

        var samples = EventFinder.Sample(Altitude, startUtc, endUtc, PassSearchStep);
        var passes = new List<SatellitePass>();
        var i = 0;
        while (i < samples.Count)
        {
            if (samples[i].Value < minimumAltitudeDegrees)
            {
                i++;
                continue;
            }

            var first = i;
            while (i < samples.Count && samples[i].Value >= minimumAltitudeDegrees)
            {
                i++;
            }

            var last = i - 1;
            var rise = first == 0
                ? startUtc
                : EventFinder.FindCrossing(Altitude, [samples[first - 1], samples[first]], minimumAltitudeDegrees, rising: true) ?? samples[first].Time;
            var set = last == samples.Count - 1
                ? endUtc
                : EventFinder.FindCrossing(Altitude, [samples[last], samples[last + 1]], minimumAltitudeDegrees, rising: false) ?? samples[last].Time;
            var window = samples.Skip(first).Take(last - first + 1).Prepend((rise, Altitude(rise))).Append((set, Altitude(set))).ToList();
            var (maxTime, maxAltitude) = EventFinder.FindMaximum(Altitude, window);
            passes.Add(BuildPass(satellite, observer, rise, (maxTime, maxAltitude), set));
        }

        return passes;
    }

    private static SatellitePass BuildPass(TrackedSatellite satellite, ObserverLocation observer, DateTime rise, (DateTime Time, double Altitude) max, DateTime set)
    {
        bool Visible(DateTime t) =>
            Observe(satellite, observer, t).Illumination != SatelliteIllumination.Umbra
            && CoordinateTransformer.ToHorizontal(SolarSystemCalculator.GetSunPosition(t).Equatorial, observer, t).AltitudeDegrees <= DarkSkySunAltitudeDegrees;

        DateTime? visibleStart = null;
        DateTime? visibleEnd = null;
        double? brightest = null;
        var previous = rise;
        var previousVisible = false;
        for (var t = rise; ; t += VisibilityStep)
        {
            if (t > set)
            {
                t = set;
            }

            var visible = Visible(t);
            if (visible)
            {
                visibleStart ??= t == rise ? t : Refine(Visible, previous, t, becomesTrue: true);
                visibleEnd = t;
                var magnitude = Observe(satellite, observer, t).Magnitude;
                if (magnitude is { } m && (brightest is null || m < brightest))
                {
                    brightest = m;
                }
            }
            else if (previousVisible)
            {
                visibleEnd = Refine(Visible, previous, t, becomesTrue: false);
            }

            previousVisible = visible;
            previous = t;
            if (t >= set)
            {
                break;
            }
        }

        var riseState = Observe(satellite, observer, rise);
        var maxState = Observe(satellite, observer, max.Time);
        var setState = Observe(satellite, observer, set);
        return new SatellitePass(satellite.CatalogNumber, satellite.Name,
            rise, riseState.Horizontal.AzimuthDegrees,
            max.Time, max.Altitude, maxState.Horizontal.AzimuthDegrees,
            set, setState.Horizontal.AzimuthDegrees,
            visibleStart, visibleStart is null ? null : visibleEnd, visibleStart is null ? null : brightest);
    }

    /// <summary>Instant (to 0.5 s) at which <paramref name="predicate"/> changes between two times.</summary>
    internal static DateTime Refine(Func<DateTime, bool> predicate, DateTime before, DateTime after, bool becomesTrue)
    {
        while (after - before > TimeSpan.FromSeconds(0.5))
        {
            var middle = before + ((after - before) / 2);
            if (predicate(middle) == becomesTrue)
            {
                after = middle;
            }
            else
            {
                before = middle;
            }
        }

        return becomesTrue ? after : before;
    }

    /// <summary>Geocentric Sun vector (km), true equator and equinox of date (≈ TEME).</summary>
    internal static Vector3d SunTeme(DateTime utc)
    {
        var sun = SolarSystemCalculator.GetSunPosition(utc);
        return Vector3d.FromSpherical(sun.Equatorial.RightAscensionHours * 15, sun.Equatorial.DeclinationDegrees) * sun.DistanceKm;
    }

    /// <summary>Earth-fixed position (km) of the observer on the WGS-84 ellipsoid.</summary>
    internal static Vector3d ObserverEcef(ObserverLocation observer)
    {
        var (sinLat, cosLat) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(observer.LatitudeDegrees));
        var (sinLon, cosLon) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(observer.LongitudeDegrees));
        var e2 = EarthFlattening * (2 - EarthFlattening);
        var n = EarthEquatorialRadiusKm / Math.Sqrt(1 - (e2 * sinLat * sinLat));
        var h = observer.ElevationMeters / 1000;
        return new Vector3d((n + h) * cosLat * cosLon, (n + h) * cosLat * sinLon, ((n * (1 - e2)) + h) * sinLat);
    }

    private static double Dot(Vector3d a, Vector3d b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double AngleDegrees(Vector3d a, Vector3d b) =>
        AstronomyMath.RadiansToDegreesValue(Math.Acos(Math.Clamp(Dot(a, b) / (a.Length * b.Length), -1, 1)));
}
