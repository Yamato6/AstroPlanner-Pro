using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Services;

/// <summary>
/// Default <see cref="IAstronomyService"/>. Delegates the calculations to the static
/// <c>Algorithms</c> classes. It lives in Core because it does not depend on the UI and reuses
/// pure, tested logic.
/// </summary>
public sealed class AstronomyService : IAstronomyService
{
    /// <summary>
    /// Altitude of the center of the Sun at sunset/sunrise: apparent radius (16') plus
    /// refraction at the horizon (34'), the standard convention.
    /// </summary>
    public const double SunriseSunsetAltitudeDegrees = -0.833;

    /// <summary>Sun at -18°: end/start of astronomical twilight.</summary>
    public const double AstronomicalTwilightAltitudeDegrees = -18.0;

    private static readonly TimeSpan SunSampleStep = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EventSampleStep = TimeSpan.FromMinutes(10);

    public HorizontalCoordinates GetHorizontal(
        CelestialObject target,
        ObserverLocation observer,
        DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observer);

        // Coordinates of date → horizon → apparent altitude.
        var ofDate = EquatorialOfDate(target, utcDateTime);
        var geometric = CoordinateTransformer.ToHorizontal(ofDate, observer, utcDateTime);
        return geometric with { AltitudeDegrees = Refraction.ApparentAltitude(geometric.AltitudeDegrees) };
    }

    /// <summary>
    /// Equatorial coordinates, equinox of date: planets are calculated for that instant; the
    /// rest by precession from the J2000 catalog.
    /// </summary>
    private static EquatorialCoordinates EquatorialOfDate(CelestialObject target, DateTime utcDateTime) =>
        target.Planet is { } planet
            ? PlanetCalculator.GetPosition(planet, utcDateTime).Equatorial
            : Precession.FromJ2000(target.Coordinates, utcDateTime);

    public PlanetPosition? GetPlanetPosition(CelestialObject target, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Planet is { } planet ? PlanetCalculator.GetPosition(planet, utcDateTime) : null;
    }

    public double GetApparentMagnitude(CelestialObject target, DateTime utcDateTime) =>
        GetPlanetPosition(target, utcDateTime)?.Magnitude ?? target.ApparentMagnitude;

    public IEnumerable<(DateTime Time, double AltitudeDegrees)> GetAltitudeSeries(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observer);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The time interval must be positive.");
        }

        for (var t = startUtc; t <= endUtc; t += interval)
        {
            yield return (t, GetHorizontal(target, observer, t).AltitudeDegrees);
        }
    }

    public IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetNightAltitudeSeries(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(observer);

        return SampleLocal(startLocal, endLocal, timeZone, interval,
            utc => GetHorizontal(target, observer, utc).AltitudeDegrees);
    }

    public ObjectNightEvents GetNightEvents(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        double minimumAltitudeDegrees)
    {
        var samples = GetNightAltitudeSeries(target, observer, startLocal, endLocal, timeZone, EventSampleStep);
        var maximum = samples.MaxBy(sample => sample.AltitudeDegrees);

        return new ObjectNightEvents(
            RiseLocal: ObservationNight.FindCrossing(samples, 0, rising: true),
            ThresholdCrossingLocal: ObservationNight.FindCrossing(samples, minimumAltitudeDegrees, rising: true),
            CulminationLocal: maximum.LocalTime,
            SetLocal: ObservationNight.FindCrossing(samples, 0, rising: false),
            MaximumAltitudeDegrees: maximum.AltitudeDegrees);
    }

    public double GetSunAltitudeDegrees(ObserverLocation observer, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(observer);

        // Geometric altitude of the center: the sunset (-0.833°) and twilight (-18°)
        // conventions already include refraction.
        var sun = SolarSystemCalculator.GetSunPosition(utcDateTime);
        return CoordinateTransformer.ToHorizontal(sun.Equatorial, observer, utcDateTime).AltitudeDegrees;
    }

    public SkyCondition GetSkyCondition(ObserverLocation observer, DateTime utcDateTime)
    {
        var sunAltitude = GetSunAltitudeDegrees(observer, utcDateTime);
        return sunAltitude switch
        {
            > SunriseSunsetAltitudeDegrees => SkyCondition.Day,
            > AstronomicalTwilightAltitudeDegrees => SkyCondition.Twilight,
            _ => SkyCondition.Dark
        };
    }

    public NightInfo GetNightInfo(ObserverLocation observer, DateTime localTime, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(observer);

        // Search from noon to noon around the night that contains the time.
        var nightDate = ObservationNight.GetNightStartLocal(localTime).Date;
        var noon = nightDate.AddHours(12);
        var samples = SampleLocal(noon, noon.AddDays(1), timeZone, SunSampleStep,
            utc => GetSunAltitudeDegrees(observer, utc));

        var sunset = ObservationNight.FindCrossing(samples, SunriseSunsetAltitudeDegrees, rising: false);
        var sunrise = ObservationNight.FindCrossing(samples, SunriseSunsetAltitudeDegrees, rising: true);
        var dusk = ObservationNight.FindCrossing(samples, AstronomicalTwilightAltitudeDegrees, rising: false);
        var dawn = ObservationNight.FindCrossing(samples, AstronomicalTwilightAltitudeDegrees, rising: true);

        // Without sunset or sunrise (polar day or night) the fixed 18:00 → 06:00 window is used.
        var hasSunWindow = sunset is not null && sunrise is not null && sunrise > sunset;
        var start = hasSunWindow ? sunset!.Value : nightDate.AddHours(ObservationNight.StartHour);
        var end = hasSunWindow ? sunrise!.Value : start + ObservationNight.Duration;

        return new NightInfo(
            WindowStartLocal: start,
            WindowEndLocal: end,
            SunsetLocal: hasSunWindow ? sunset : null,
            SunriseLocal: hasSunWindow ? sunrise : null,
            AstronomicalDuskLocal: dusk is not null && dawn is not null && dawn > dusk ? dusk : null,
            AstronomicalDawnLocal: dusk is not null && dawn is not null && dawn > dusk ? dawn : null);
    }

    public MoonState GetMoon(ObserverLocation observer, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(observer);

        var moon = SolarSystemCalculator.GetMoonPosition(utcDateTime);
        var sun = SolarSystemCalculator.GetSunPosition(utcDateTime);
        var geocentric = CoordinateTransformer.ToHorizontal(moon.Equatorial, observer, utcDateTime);

        // Parallax: seen from the surface the Moon appears up to ~1° lower.
        var topocentricAltitude = geocentric.AltitudeDegrees
            - moon.HorizontalParallaxDegrees * Math.Cos(geocentric.AltitudeDegrees * Math.PI / 180.0);

        return new MoonState(
            Horizontal: geocentric with { AltitudeDegrees = Refraction.ApparentAltitude(topocentricAltitude) },
            Equatorial: moon.Equatorial,
            IlluminatedFraction: SolarSystemCalculator.GetMoonIlluminatedFraction(moon, sun),
            IsWaxing: SolarSystemCalculator.IsMoonWaxing(moon, sun));
    }

    public double GetMoonSeparationDegrees(CelestialObject target, MoonState moon, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(moon);

        // Both in coordinates of date so that they are comparable.
        return AstronomyMath.AngularSeparationDegrees(EquatorialOfDate(target, utcDateTime), moon.Equatorial);
    }

    public IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetMoonAltitudeSeries(
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(observer);

        return SampleLocal(startLocal, endLocal, timeZone, interval,
            utc => GetMoon(observer, utc).Horizontal.AltitudeDegrees);
    }

    /// <summary>
    /// Samples <paramref name="altitudeAt"/> in local time from start to end (both included).
    /// Sampling is in local time so that the chart axis is regular; each point is converted
    /// to UTC separately, which honors daylight saving time.
    /// </summary>
    private static List<(DateTime LocalTime, double AltitudeDegrees)> SampleLocal(
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        TimeSpan interval,
        Func<DateTime, double> altitudeAt)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "The time interval must be positive.");
        }

        if (endLocal < startLocal)
        {
            throw new ArgumentOutOfRangeException(nameof(endLocal), "The end must not be before the start.");
        }

        var points = new List<(DateTime LocalTime, double AltitudeDegrees)>();
        for (var local = startLocal; local < endLocal; local += interval)
        {
            points.Add((local, altitudeAt(TimeZoneConversion.ToUtc(local, timeZone))));
        }

        points.Add((endLocal, altitudeAt(TimeZoneConversion.ToUtc(endLocal, timeZone))));
        return points;
    }
}
