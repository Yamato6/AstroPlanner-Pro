namespace AstroPlanner.Core.Models;

public enum Planet
{
    Mercury,
    Venus,
    Mars,
    Jupiter,
    Saturn,
    Uranus,
    Neptune
}

/// <summary>
/// A planet seen from the Earth at a given instant.
/// </summary>
/// <param name="Equatorial">Apparent coordinates, equinox of date.</param>
/// <param name="DistanceAu">Distance to the Earth (Δ), in astronomical units.</param>
/// <param name="SunDistanceAu">Distance to the Sun (r), in astronomical units.</param>
/// <param name="ElongationDegrees">Angular separation from the Sun as seen from the Earth.</param>
/// <param name="PhaseAngleDegrees">Sun-planet-Earth angle.</param>
/// <param name="IlluminatedFraction">Illuminated fraction of the disc (0-1).</param>
/// <param name="Magnitude">Apparent visual magnitude.</param>
/// <param name="ApparentDiameterArcsec">Apparent equatorial diameter, in arcseconds.</param>
public sealed record PlanetPosition(
    Planet Planet,
    EquatorialCoordinates Equatorial,
    double DistanceAu,
    double SunDistanceAu,
    double ElongationDegrees,
    double PhaseAngleDegrees,
    double IlluminatedFraction,
    double Magnitude,
    double ApparentDiameterArcsec);

/// <summary>The planets added to the catalog; for planets the id is the name.</summary>
public static class SolarSystemCatalog
{
    public static IReadOnlyList<CelestialObject> Planets { get; } =
    [
        CelestialObject.CreatePlanet("Mercury", "Mercury", Planet.Mercury),
        CelestialObject.CreatePlanet("Venus", "Venus", Planet.Venus),
        CelestialObject.CreatePlanet("Mars", "Mars", Planet.Mars),
        CelestialObject.CreatePlanet("Jupiter", "Jupiter", Planet.Jupiter),
        CelestialObject.CreatePlanet("Saturn", "Saturn", Planet.Saturn),
        CelestialObject.CreatePlanet("Uranus", "Uranus", Planet.Uranus),
        CelestialObject.CreatePlanet("Neptune", "Neptune", Planet.Neptune)
    ];
}
