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
        CelestialObjectType.GalaxyGroup => Strings.TypeGalaxyGroup,
        CelestialObjectType.StellarAssociation => Strings.TypeStellarAssociation,
        CelestialObjectType.ClusterWithNebula => Strings.TypeClusterWithNebula,
        CelestialObjectType.HiiRegion => Strings.TypeHiiRegion,
        CelestialObjectType.EmissionNebula => Strings.TypeEmissionNebula,
        CelestialObjectType.ReflectionNebula => Strings.TypeReflectionNebula,
        CelestialObjectType.DarkNebula => Strings.TypeDarkNebula,
        CelestialObjectType.Star => Strings.TypeStar,
        CelestialObjectType.DoubleStar => Strings.TypeDoubleStar,
        CelestialObjectType.Nova => Strings.TypeNova,
        CelestialObjectType.Comet => Strings.TypeComet,
        CelestialObjectType.Asteroid => Strings.TypeAsteroid,
        CelestialObjectType.MilkyWayCore => Strings.TypeMilkyWayCore,
        _ => Strings.TypeOther
    };

    /// <summary>Apparent size, e.g. "177.8′ × 69.7′" or "6.9′"; empty if unknown.</summary>
    public static string Size(double? majorAxisArcmin, double? minorAxisArcmin) => (majorAxisArcmin, minorAxisArcmin) switch
    {
        ({ } major, { } minor) when minor > 0 && Math.Abs(major - minor) > 1e-9 => Format(Strings.SizeFormat, major, minor),
        ({ } major, _) => Format(Strings.SizeSingleFormat, major),
        _ => string.Empty
    };

    /// <summary>A time window, e.g. "21:20–05:05"; empty without one.</summary>
    /// <summary>"2P/Encke", "C/2023 A3 (Tsuchinshan-ATLAS)", "(4) Vesta"; the display name for other objects.</summary>
    public static string SmallBodyName(CelestialObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Orbit is not { } orbit || orbit.Name.Length == 0)
        {
            return target.DisplayName == target.Id ? target.Id : $"{target.Id} {target.DisplayName}";
        }

        return orbit.Kind == SmallBodyKind.Asteroid ? $"{orbit.Designation} {orbit.Name}"
            : orbit.Designation.Contains('/', StringComparison.Ordinal) ? $"{orbit.Designation} ({orbit.Name})"
            : $"{orbit.Designation}/{orbit.Name}";
    }

    public static string Window(DateTime? start, DateTime? end) =>
        start is { } from && end is { } to ? Format(Strings.WindowFormat, from, to) : string.Empty;

    /// <summary>A duration in hours and minutes, e.g. "7h 45m".</summary>
    public static string Duration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes);
        return Format(Strings.DurationFormat, minutes / 60, minutes % 60);
    }

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
    /// <summary>Eight-point compass direction of an azimuth (north = 0°, east = 90°): "N", "NE", …</summary>
    public static string Compass(double azimuthDegrees)
    {
        var points = Strings.CompassPoints.Split(',');
        var index = (int)Math.Round((((azimuthDegrees % 360) + 360) % 360) / 45) % 8;
        return points[index];
    }

    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
