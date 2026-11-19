using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>Option of a filter: <see cref="Value"/> is what is filtered, <see cref="Label"/> what is shown.</summary>
public sealed record FilterOption(string Value, string Label);

/// <summary>User-facing names of internal values.</summary>
public static class DisplayNames
{
    /// <summary>Value of the type filter that filters nothing.</summary>
    public const string AllTypes = "All";

    public static string Of(CelestialObjectType type) => type switch
    {
        CelestialObjectType.Galaxy => "Galaxia",
        CelestialObjectType.OpenCluster => "Cúmulo abierto",
        CelestialObjectType.GlobularCluster => "Cúmulo globular",
        CelestialObjectType.PlanetaryNebula => "Nebulosa planetaria",
        CelestialObjectType.DiffuseNebula => "Nebulosa difusa",
        CelestialObjectType.SupernovaRemnant => "Resto de supernova",
        CelestialObjectType.Planet => "Planeta",
        _ => "Otro"
    };

    /// <summary>Name of the lunar phase from the illuminated fraction.</summary>
    public static string MoonPhase(double illuminatedFraction, bool isWaxing) => illuminatedFraction switch
    {
        < 0.03 => "Luna nueva",
        > 0.97 => "Luna llena",
        < 0.47 => isWaxing ? "Creciente" : "Menguante",
        <= 0.53 => isWaxing ? "Cuarto creciente" : "Cuarto menguante",
        _ => isWaxing ? "Gibosa creciente" : "Gibosa menguante"
    };

    /// <summary>Illuminated fraction as a percentage, e.g. "87 %"; "—" without a Moon.</summary>
    public static string MoonIllumination(MoonState? moon) =>
        moon is null ? "—" : $"{moon.IlluminatedFraction * 100:0} %";
}
