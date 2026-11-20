using AstroPlanner.Core.Algorithms;

namespace AstroPlanner.Core.SkyQuality;

/// <summary>Sky brightness toward a target and what it is made of (mag/arcsec², infinity = nothing).</summary>
/// <param name="TotalSqm">Everything added in linear units.</param>
/// <param name="SiteSqm">The site's dark-sky glow (natural + artificial) brightened toward the horizon.</param>
/// <param name="MoonlightSqm">Scattered moonlight (Krisciunas &amp; Schaefer).</param>
/// <param name="TwilightSqm">Twilight (only when twilight time is allowed).</param>
public readonly record struct SkyBackgroundSample(double TotalSqm, double SiteSqm, double MoonlightSqm, double TwilightSqm);

/// <summary>
/// Sky background at a target (spec §8.4): the zenith baseline of the site (manual SQM or the
/// atlas), brightened toward the horizon with air mass, plus moonlight (Krisciunas &amp; Schaefer
/// 1991) and, if allowed, twilight — all added in linear units.
/// </summary>
public static class SkyBackground
{
    /// <summary>Default brightening of the site glow per unit of air mass above 1 (heuristic, documented).</summary>
    public const double DefaultBrighteningPerAirmass = 0.5;

    /// <summary>The air mass toward the target is capped here (≈ 10° altitude) for the site glow.</summary>
    private const double MaximumAirmass = 5.6;

    /// <summary>
    /// Zenith SQM of a site: the manual reading if any, else the atlas (artificial + 0.171 mcd/m²),
    /// else a pristine natural sky (22.0).
    /// </summary>
    public static (double Sqm, SkyQualitySource Source) ZenithBaseline(double? manualSqm, double? atlasArtificialLuminance)
    {
        if (manualSqm is { } manual)
        {
            return (manual, SkyQualitySource.Manual);
        }

        if (atlasArtificialLuminance is { } artificial)
        {
            return (SkyBrightness.SqmFromArtificial(artificial), SkyQualitySource.Atlas);
        }

        return (SkyBrightness.NaturalSqm, SkyQualitySource.Default);
    }

    /// <summary>
    /// The site's glow toward an altitude: L = L_zenith · (1 + a·(X − 1)), X = Kasten–Young air mass
    /// (capped at 5.6). A documented heuristic for the combined airglow and light-pollution dome:
    /// with a = 0.5 the sky is ≈ 0.44 mag brighter at 30° and ≈ 0.7 mag at 20°.
    /// </summary>
    public static double SiteGlowAt(double zenithSqm, double altitudeDegrees, double brighteningPerAirmass = DefaultBrighteningPerAirmass)
    {
        var airmass = Math.Min(Airmass.KastenYoung(Math.Max(altitudeDegrees, 0)), MaximumAirmass);
        var factor = 1 + Math.Max(0, brighteningPerAirmass) * (airmass - 1);
        return zenithSqm - 2.5 * Math.Log10(factor);
    }

    /// <summary>Sky brightness toward a target at one instant.</summary>
    /// <param name="zenithSqm">Zenith baseline of the site (see <see cref="ZenithBaseline"/>).</param>
    /// <param name="targetAltitudeDegrees">Altitude of the target.</param>
    /// <param name="sunAltitudeDegrees">Altitude of the Sun.</param>
    /// <param name="includeTwilight">Add the twilight term (only when twilight time is allowed).</param>
    /// <param name="moonAltitudeDegrees">Topocentric altitude of the Moon.</param>
    /// <param name="moonPhaseAngleDegrees">Lunar phase angle (0 = full).</param>
    /// <param name="moonSeparationDegrees">Angle between the Moon and the target.</param>
    /// <param name="extinction">V extinction coefficient, mag/air mass.</param>
    /// <param name="brighteningPerAirmass">See <see cref="SiteGlowAt"/>.</param>
    public static SkyBackgroundSample At(
        double zenithSqm,
        double targetAltitudeDegrees,
        double sunAltitudeDegrees,
        bool includeTwilight,
        double moonAltitudeDegrees,
        double moonPhaseAngleDegrees,
        double moonSeparationDegrees,
        double extinction,
        double brighteningPerAirmass = DefaultBrighteningPerAirmass)
    {
        var site = SiteGlowAt(zenithSqm, targetAltitudeDegrees, brighteningPerAirmass);

        var moonlight = double.PositiveInfinity;
        if (moonAltitudeDegrees > 0)
        {
            var nanolamberts = KrisciunasSchaefer.MoonlightNanolamberts(
                moonPhaseAngleDegrees, moonSeparationDegrees, 90 - moonAltitudeDegrees, 90 - Math.Max(targetAltitudeDegrees, 0), extinction);
            if (nanolamberts > 0)
            {
                moonlight = KrisciunasSchaefer.MagnitudeFromNanolamberts(nanolamberts);
            }
        }

        var twilight = includeTwilight ? SkyBrightness.Twilight(sunAltitudeDegrees) : double.PositiveInfinity;
        return new SkyBackgroundSample(SkyBrightness.Combine(site, moonlight, twilight), site, moonlight, twilight);
    }
}

/// <summary>Where a site's zenith brightness comes from.</summary>
public enum SkyQualitySource
{
    /// <summary>Typed by the user (an SQM reading or a Bortle estimate).</summary>
    Manual,
    /// <summary>World Atlas 2015 grid.</summary>
    Atlas,
    /// <summary>Nothing known: a pristine natural sky is assumed.</summary>
    Default
}
