namespace AstroPlanner.Core.Models;

/// <summary>Geocentric position of the Sun (equinox of date).</summary>
public sealed record SunPosition(
    double EclipticLongitudeDegrees,
    EquatorialCoordinates Equatorial,
    double DistanceKm);

/// <summary>Geocentric position of the Moon (mean equinox of date).</summary>
public sealed record MoonPosition(
    double EclipticLongitudeDegrees,
    double EclipticLatitudeDegrees,
    EquatorialCoordinates Equatorial,
    double DistanceKm,
    double HorizontalParallaxDegrees);

/// <summary>
/// The Moon seen by an observer at an instant: position in the sky (topocentric, with
/// refraction) and phase.
/// </summary>
/// <param name="IlluminatedFraction">0 = new Moon, 1 = full Moon.</param>
/// <param name="IsWaxing">True between new and full Moon (waxing).</param>
public sealed record MoonState(
    HorizontalCoordinates Horizontal,
    EquatorialCoordinates Equatorial,
    double IlluminatedFraction,
    bool IsWaxing)
{
    public bool IsAboveHorizon => Horizontal.AltitudeDegrees > 0;
}

/// <summary>State of the sky according to the altitude of the Sun.</summary>
public enum SkyCondition
{
    /// <summary>Sun above the horizon.</summary>
    Day,

    /// <summary>Sun between 0° and -18°: the sky is not fully dark yet.</summary>
    Twilight,

    /// <summary>Sun below -18°: astronomical night.</summary>
    Dark
}

/// <summary>
/// Observing night in local time: the window goes from sunset to sunrise; the dark night,
/// from the end to the start of astronomical twilight.
/// </summary>
/// <param name="WindowStartLocal">Sunset, or 18:00 if the Sun does not set (polar day/night).</param>
/// <param name="WindowEndLocal">Sunrise, or 06:00 if the Sun does not rise.</param>
/// <param name="AstronomicalDuskLocal">End of evening twilight (Sun at -18°); null if it never gets fully dark.</param>
/// <param name="AstronomicalDawnLocal">Start of morning twilight (Sun at -18°).</param>
public sealed record NightInfo(
    DateTime WindowStartLocal,
    DateTime WindowEndLocal,
    DateTime? SunsetLocal,
    DateTime? SunriseLocal,
    DateTime? AstronomicalDuskLocal,
    DateTime? AstronomicalDawnLocal)
{
    public bool HasAstronomicalDarkness => AstronomicalDuskLocal is not null && AstronomicalDawnLocal is not null;

    /// <summary>True if the window is the fallback one (18:00 → 06:00) because the Sun does not set or rise.</summary>
    public bool IsFallbackWindow => SunsetLocal is null || SunriseLocal is null;
}
