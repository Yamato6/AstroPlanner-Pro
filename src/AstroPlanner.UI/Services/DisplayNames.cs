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
    /// <summary>"Spectral type A0m... · B−V 0.01 · 8.6 light-years · variable (Hipparcos 1.52–1.33)".</summary>
    public static string StarSummary(StarInfo star)
    {
        ArgumentNullException.ThrowIfNull(star);
        var parts = new List<string>();
        if (star.SpectralType is { } spectral)
        {
            parts.Add(Format(Strings.StarSpectralFormat, spectral));
        }

        if (star.ColorIndex is { } colour)
        {
            parts.Add(Format(Strings.StarColorFormat, colour));
        }

        if (star.DistanceLightYears is { } lightYears)
        {
            parts.Add(Format(Strings.StarDistanceFormat, lightYears));
        }

        if (star.IsVariable)
        {
            parts.Add(star.VariableFaintestMagnitude is { } faint && star.VariableBrightestMagnitude is { } bright
                ? Format(Strings.StarVariableRangeFormat, bright, faint)
                : Strings.StarVariable);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>"STFA 43 AB · 34.3″ at PA 54° · mags 3.2 + 4.7 (2025)".</summary>
    public static string DoubleStarPair(DoubleStarPair pair)
    {
        ArgumentNullException.ThrowIfNull(pair);
        return pair.PositionAngleDegrees is { } angle
            ? Format(Strings.DoubleStarPairFormat, pair.Name, pair.SeparationArcsec, angle, pair.PrimaryMagnitude, pair.CompanionMagnitude, pair.LastYear)
            : Format(Strings.DoubleStarPairNoAngleFormat, pair.Name, pair.SeparationArcsec, pair.PrimaryMagnitude, pair.CompanionMagnitude, pair.LastYear);
    }

    /// <summary>Eight-point compass direction of an azimuth (north = 0°, east = 90°): "N", "NE", …</summary>
    public static string Compass(double azimuthDegrees)
    {
        var points = Strings.CompassPoints.Split(',');
        var index = (int)Math.Round((((azimuthDegrees % 360) + 360) % 360) / 45) % 8;
        return points[index];
    }

    /// <summary>"Photographed" or "Observed".</summary>
    public static string LogKind(ObservationLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsPhotographed ? Strings.LogKindPhotographed : Strings.LogKindObserved;
    }

    /// <summary>"Rig · 120 s × 30 · ISO 800 · 1 h 0 min" for a photographed entry; empty otherwise.</summary>
    public static string Capture(ObservationLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsPhotographed || entry.Capture is not { } capture)
        {
            return "";
        }

        var parts = new List<string>();
        if (capture.RigName.Length > 0) parts.Add(capture.RigName);
        if (capture.SubSeconds is { } sub)
        {
            parts.Add(capture.SubCount is { } count ? Format(Strings.CaptureSubsFormat, sub, count) : Format(Strings.CaptureSubFormat, sub));
        }

        if (capture.Iso is { } iso) parts.Add(Format(Strings.CaptureIsoFormat, iso));
        if (capture.Gain is { } gain) parts.Add(Format(Strings.CaptureGainFormat, gain));
        if (capture.TotalIntegration is { } total) parts.Add(Duration(total));
        return string.Join(" · ", parts);
    }

    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
