using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>
/// High-level astronomy service for the presentation layer. Centralizes positions (objects,
/// Sun, Moon), the observing night and altitude time series, so that view models do not
/// depend directly on the static <c>Algorithms</c> classes.
/// </summary>
public interface IAstronomyService
{
    /// <summary>
    /// Apparent position (altitude/azimuth) of <paramref name="target"/> seen from
    /// <paramref name="observer"/> at the given UTC instant: applies precession J2000 → date and
    /// atmospheric refraction.
    /// </summary>
    HorizontalCoordinates GetHorizontal(
        CelestialObject target,
        ObserverLocation observer,
        DateTime utcDateTime);

    /// <summary>
    /// Altitude time series (degrees) of <paramref name="target"/> from
    /// <paramref name="startUtc"/> to <paramref name="endUtc"/> every <paramref name="interval"/>.
    /// </summary>
    IEnumerable<(DateTime Time, double AltitudeDegrees)> GetAltitudeSeries(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan interval);

    /// <summary>
    /// Altitude series from <paramref name="startLocal"/> to <paramref name="endLocal"/> (local
    /// time of <paramref name="timeZone"/>, both included), sampled every
    /// <paramref name="interval"/> in local time. Used for the night curve.
    /// </summary>
    IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetNightAltitudeSeries(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        TimeSpan interval);

    /// <summary>
    /// Rise, minimum-altitude crossing, culmination and set of <paramref name="target"/>
    /// between <paramref name="startLocal"/> and <paramref name="endLocal"/> (usually from
    /// sunset to sunrise).
    /// </summary>
    ObjectNightEvents GetNightEvents(
        CelestialObject target,
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        double minimumAltitudeDegrees);

    /// <summary>
    /// Position, distance, phase, magnitude and apparent size of a planet; null if
    /// <paramref name="target"/> is not a planet.
    /// </summary>
    PlanetPosition? GetPlanetPosition(CelestialObject target, DateTime utcDateTime);

    /// <summary>
    /// Position, distances, phase and magnitude of a comet or asteroid; null if
    /// <paramref name="target"/> has no orbit.
    /// </summary>
    SmallBodyPosition? GetSmallBodyPosition(CelestialObject target, DateTime utcDateTime);

    /// <summary>
    /// IAU constellation of <paramref name="target"/> at <paramref name="utcDateTime"/>: planets
    /// move between constellations, fixed objects never do.
    /// </summary>
    Constellation GetConstellation(CelestialObject target, DateTime utcDateTime);

    /// <summary>Apparent magnitude: calculated for planets, comets and asteroids, the catalog one for the rest.</summary>
    double GetApparentMagnitude(CelestialObject target, DateTime utcDateTime);

    /// <summary>Geometric altitude of the center of the Sun (degrees).</summary>
    double GetSunAltitudeDegrees(ObserverLocation observer, DateTime utcDateTime);

    /// <summary>Day, twilight or dark night according to the altitude of the Sun.</summary>
    SkyCondition GetSkyCondition(ObserverLocation observer, DateTime utcDateTime);

    /// <summary>
    /// Sunset, sunrise and astronomical twilight of the night containing
    /// <paramref name="localTime"/> (before noon the previous night counts).
    /// </summary>
    NightInfo GetNightInfo(ObserverLocation observer, DateTime localTime, TimeZoneInfo timeZone);

    /// <summary>Apparent position and phase of the Moon for the observer.</summary>
    MoonState GetMoon(ObserverLocation observer, DateTime utcDateTime);

    /// <summary>Angular distance (degrees) between <paramref name="target"/> and the Moon.</summary>
    double GetMoonSeparationDegrees(CelestialObject target, MoonState moon, DateTime utcDateTime);

    /// <summary>Altitude series of the Moon, like <see cref="GetNightAltitudeSeries"/>.</summary>
    IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetMoonAltitudeSeries(
        ObserverLocation observer,
        DateTime startLocal,
        DateTime endLocal,
        TimeZoneInfo timeZone,
        TimeSpan interval);
}
