namespace AstroPlanner.Core.Models;

/// <summary>
/// Mean orbital elements of an Earth satellite in the SGP4 (TEME, Kozai mean motion) convention,
/// as CelesTrak publishes them in OMM form (the same numbers as a two-line element set).
/// </summary>
/// <param name="CatalogNumber">NORAD catalog number.</param>
/// <param name="Name">Object name (e.g. "ISS (ZARYA)").</param>
/// <param name="InternationalDesignator">COSPAR id (e.g. "1998-067A"), empty when unknown.</param>
/// <param name="EpochUtc">Element epoch (UTC).</param>
/// <param name="MeanMotionRevPerDay">Kozai mean motion, revolutions per day.</param>
/// <param name="BStar">SGP4 drag term, 1 / Earth radii.</param>
public sealed record SatelliteElements(
    int CatalogNumber,
    string Name,
    string InternationalDesignator,
    DateTime EpochUtc,
    double MeanMotionRevPerDay,
    double Eccentricity,
    double InclinationDegrees,
    double AscendingNodeDegrees,
    double ArgumentOfPerigeeDegrees,
    double MeanAnomalyDegrees,
    double BStar)
{
    /// <summary>Orbital period in minutes.</summary>
    public double PeriodMinutes => 1440.0 / MeanMotionRevPerDay;

    /// <summary>
    /// True for the "near-Earth" orbits SGP4 handles without the deep-space (SDP4) terms: a
    /// period under 225 minutes. Every object of CelesTrak's <c>visual</c> and <c>stations</c>
    /// groups is one.
    /// </summary>
    public bool IsNearEarth => PeriodMinutes < 225.0;
}

/// <summary>Element sets of the satellites the app follows, with when they were downloaded.</summary>
public sealed record SatelliteCatalog(IReadOnlyList<SatelliteElements> Satellites, DateTime FetchedAtUtc);

/// <summary>Sunlight on a satellite.</summary>
public enum SatelliteIllumination
{
    /// <summary>The whole solar disc is visible from the satellite.</summary>
    Sunlit,

    /// <summary>The Earth hides part of the solar disc.</summary>
    Penumbra,

    /// <summary>In the Earth's shadow: the satellite cannot be seen.</summary>
    Umbra
}

/// <summary>
/// A satellite seen by an observer at an instant.
/// </summary>
/// <param name="Horizontal">Topocentric geometric altitude/azimuth (no refraction).</param>
/// <param name="Equatorial">Topocentric right ascension/declination, true equator and equinox of date.</param>
/// <param name="RangeKm">Distance from the observer.</param>
/// <param name="RangeRateKmPerSecond">Rate of change of the range (positive = receding).</param>
/// <param name="PhaseAngleDegrees">Sun–satellite–observer angle.</param>
/// <param name="Magnitude">Visual magnitude, null when the satellite's standard magnitude is unknown or it is in the umbra.</param>
public sealed record SatelliteObservation(
    DateTime Utc,
    HorizontalCoordinates Horizontal,
    EquatorialCoordinates Equatorial,
    double RangeKm,
    double RangeRateKmPerSecond,
    SatelliteIllumination Illumination,
    double PhaseAngleDegrees,
    double? Magnitude);

/// <summary>
/// A pass of a satellite above the minimum altitude.
/// </summary>
/// <param name="VisibleStartUtc">Start of the part of the pass in which the satellite is sunlit and the observer's sky is dark
/// (Sun at or below nautical twilight); null when no part of the pass is visible.</param>
/// <param name="BrightestMagnitude">Brightest magnitude during the visible part; null when unknown or not visible.</param>
public sealed record SatellitePass(
    int CatalogNumber,
    string Name,
    DateTime RiseUtc,
    double RiseAzimuthDegrees,
    DateTime MaxUtc,
    double MaxAltitudeDegrees,
    double MaxAzimuthDegrees,
    DateTime SetUtc,
    double SetAzimuthDegrees,
    DateTime? VisibleStartUtc,
    DateTime? VisibleEndUtc,
    double? BrightestMagnitude)
{
    public bool IsVisible => VisibleStartUtc is not null;
}

/// <summary>A sunlit satellite crossing a camera's field during an exposure block.</summary>
/// <param name="Magnitude">Brightest magnitude while inside the field, null when unknown.</param>
public sealed record FieldCrossing(
    int CatalogNumber,
    string Name,
    DateTime EnterUtc,
    DateTime ExitUtc,
    double? Magnitude);
