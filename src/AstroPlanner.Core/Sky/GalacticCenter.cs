using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Sky;

/// <summary>
/// The Milky Way's core as a wide-field target (spec §6): centred on the galactic centre
/// (Sgr A*, J2000 17h45m40.04s −29°00′28.2″, Reid &amp; Brunthaler 2004), with the size of the
/// bright bulge region usually photographed (~30° × 20°).
/// </summary>
public static class GalacticCenter
{
    public const string Id = "Milky Way core";

    /// <summary>Sgr A*, J2000.</summary>
    public static readonly EquatorialCoordinates Coordinates = new(17 + (45 / 60.0) + (40.04 / 3600.0), -(29 + (28.2 / 3600.0)));

    public const double MajorAxisArcmin = 30 * 60;
    public const double MinorAxisArcmin = 20 * 60;

    /// <summary>The catalog entry.</summary>
    public static CelestialObject Target { get; } = new(Id, "Galactic centre (Sgr A*)", "Sagittarius",
        CelestialObjectType.MilkyWayCore, double.NaN, Coordinates)
    {
        AlternateNames = ["Galactic center", "Galactic core", "Sgr A*"],
        MajorAxisArcmin = MajorAxisArcmin,
        MinorAxisArcmin = MinorAxisArcmin,
        PositionAngleDegrees = 60   // the galactic plane runs about NE–SW there
    };
}
