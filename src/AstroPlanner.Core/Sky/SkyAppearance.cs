using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Sky;

/// <summary>
/// How bright the sky is at an instant, at the zenith, and what it is made of: the same model as
/// the feasibility engine (<see cref="SkyBackground"/>: the site's glow, Krisciunas &amp; Schaefer
/// moonlight and twilight, added in linear units). The renderer spreads each part over the sky
/// with the same angular laws (spec §8.2: Core supplies the level, the shader the distribution).
/// </summary>
/// <param name="ZenithSqm">Everything at the zenith, mag/arcsec².</param>
/// <param name="LimitingMagnitude">Naked-eye limiting magnitude for that brightness.</param>
/// <param name="SiteSqm">The site's own sky (natural + artificial), mag/arcsec².</param>
/// <param name="TwilightSqm">Twilight at the zenith, mag/arcsec² (infinity = none).</param>
/// <param name="MoonlightSqm">Scattered moonlight at the zenith, mag/arcsec² (infinity = Moon down).</param>
public readonly record struct SkyAppearanceSample(
    DateTime Utc,
    double SunAltitudeDegrees,
    double MoonAltitudeDegrees,
    double ZenithSqm,
    double LimitingMagnitude,
    double SiteSqm,
    double TwilightSqm,
    double MoonlightSqm);

/// <summary>
/// Samples the sky brightness over a time window, like the body tracks, so that the renderer can
/// interpolate it while time plays (spec §8.2).
/// </summary>
public static class SkyAppearanceCalculator
{
    /// <summary>
    /// Appearance at one instant for a site of zenith brightness <paramref name="siteSqm"/>
    /// (null = unknown: a natural sky), with V extinction <paramref name="extinction"/> (mag/air mass).
    /// </summary>
    public static SkyAppearanceSample At(
        ObserverLocation observer,
        DateTime utcDateTime,
        double? siteSqm,
        double extinction = Airmass.DefaultExtinctionCoefficient)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var sun = SolarSystemCalculator.GetSunPosition(utc);
        var moon = SolarSystemCalculator.GetMoonPosition(utc);
        var sunAltitude = CoordinateTransformer.ToHorizontal(sun.Equatorial, observer, utc).AltitudeDegrees;
        var geocentricMoon = CoordinateTransformer.ToHorizontal(moon.Equatorial, observer, utc).AltitudeDegrees;
        // Same topocentric parallax as AstronomyService.GetMoon.
        var moonAltitude = geocentricMoon - moon.HorizontalParallaxDegrees * Math.Cos(double.DegreesToRadians(geocentricMoon));
        var site = siteSqm ?? SkyBrightness.NaturalSqm;
        var zenith = SkyBackground.At(
            site,
            targetAltitudeDegrees: 90,
            sunAltitude,
            includeTwilight: true,
            moonAltitude,
            SolarSystemCalculator.GetMoonPhaseAngleDegrees(moon, sun),
            moonSeparationDegrees: 90 - moonAltitude,
            extinction);
        return new SkyAppearanceSample(
            utc,
            sunAltitude,
            moonAltitude,
            zenith.TotalSqm,
            SkyBrightness.NakedEyeLimitingMagnitude(zenith.TotalSqm),
            zenith.SiteSqm,
            zenith.TwilightSqm,
            zenith.MoonlightSqm);
    }

    /// <summary>Samples from <paramref name="startUtc"/> to <paramref name="endUtc"/> (inclusive) every <paramref name="step"/>.</summary>
    public static IReadOnlyList<SkyAppearanceSample> Sample(
        ObserverLocation observer,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan step,
        double? siteSqm,
        double extinction = Airmass.DefaultExtinctionCoefficient)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(endUtc, startUtc);

        var samples = new List<SkyAppearanceSample>();
        for (var time = startUtc; time <= endUtc; time += step)
        {
            samples.Add(At(observer, time, siteSqm, extinction));
        }

        return samples;
    }
}
