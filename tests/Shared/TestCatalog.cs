using AstroPlanner.Core.Models;

namespace AstroPlanner.Testing;

/// <summary>Catalog objects shared by several tests.</summary>
internal static class TestCatalog
{
    public static readonly CelestialObject M42 = new(
        "M42", "Orion Nebula", "Orion", CelestialObjectType.DiffuseNebula, 4.0,
        new EquatorialCoordinates(5.59, -5.39));

    public static readonly CelestialObject M31 = new(
        "M31", "Andromeda Galaxy", "Andromeda", CelestialObjectType.Galaxy, 3.4,
        new EquatorialCoordinates(0.712, 41.27));

    public static readonly CelestialObject M13 = new(
        "M13", "Great Hercules Cluster", "Hercules", CelestialObjectType.GlobularCluster, 5.8,
        new EquatorialCoordinates(16.69, 36.46));
}
