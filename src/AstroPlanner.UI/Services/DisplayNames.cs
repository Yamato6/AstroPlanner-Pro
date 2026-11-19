using System.Globalization;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;

namespace AstroPlanner.UI.Services;

/// <summary>Option of a filter: <see cref="Value"/> is what is filtered, <see cref="Label"/> what is shown.</summary>
public sealed record FilterOption(string Value, string Label);

/// <summary>User-facing names of internal values (texts come from <see cref="Strings"/>).</summary>
public static class DisplayNames
{
    /// <summary>Value of the type filter that filters nothing.</summary>
    public const string AllTypes = "All";

    public static string Of(CelestialObjectType type) => type switch
    {
        CelestialObjectType.Galaxy => Strings.TypeGalaxy,
        CelestialObjectType.OpenCluster => Strings.TypeOpenCluster,
        CelestialObjectType.GlobularCluster => Strings.TypeGlobularCluster,
        CelestialObjectType.PlanetaryNebula => Strings.TypePlanetaryNebula,
        CelestialObjectType.DiffuseNebula => Strings.TypeDiffuseNebula,
        CelestialObjectType.SupernovaRemnant => Strings.TypeSupernovaRemnant,
        CelestialObjectType.Planet => Strings.TypePlanet,
        _ => Strings.TypeOther
    };

    /// <summary>Name of the lunar phase from the illuminated fraction.</summary>
    public static string MoonPhase(double illuminatedFraction, bool isWaxing) => illuminatedFraction switch
    {
        < 0.03 => Strings.MoonNew,
        > 0.97 => Strings.MoonFull,
        < 0.47 => isWaxing ? Strings.MoonWaxingCrescent : Strings.MoonWaningCrescent,
        <= 0.53 => isWaxing ? Strings.MoonFirstQuarter : Strings.MoonLastQuarter,
        _ => isWaxing ? Strings.MoonWaxingGibbous : Strings.MoonWaningGibbous
    };

    /// <summary>Illuminated fraction as a percentage, e.g. "87%"; "—" without a Moon.</summary>
    public static string MoonIllumination(MoonState? moon) =>
        moon is null ? "—" : Format(Strings.PercentFormat, moon.IlluminatedFraction * 100);

    /// <summary>Words for a cloud-cover percentage.</summary>
    public static string CloudCover(double cloudCoverPercent) => cloudCoverPercent switch
    {
        < 15 => Strings.CloudClear,
        < 40 => Strings.CloudMostlyClear,
        < 70 => Strings.CloudPartlyCloudy,
        < 90 => Strings.CloudMostlyCloudy,
        _ => Strings.CloudOvercast
    };

    /// <summary>Formats a resource string with the current culture.</summary>
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
