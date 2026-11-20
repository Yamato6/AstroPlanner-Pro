using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Sky;

/// <summary>How bright the sky looks at an instant (zenith), for the renderer's background.</summary>
/// <param name="ZenithSqm">Zenith brightness, mag/arcsec² (site baseline + twilight + moonlight).</param>
/// <param name="LimitingMagnitude">Naked-eye limiting magnitude for that brightness.</param>
public readonly record struct SkyAppearanceSample(
    DateTime Utc,
    double SunAltitudeDegrees,
    double MoonAltitudeDegrees,
    double ZenithSqm,
    double LimitingMagnitude);

/// <summary>
/// Samples the sky brightness over a time window, like the body tracks, so that the renderer can
/// interpolate it while time plays (spec §4.2 "sky brightness rendering").
/// </summary>
public static class SkyAppearanceCalculator
{
    /// <summary>Appearance at one instant for a site of zenith brightness <paramref name="siteSqm"/> (null = unknown).</summary>
    public static SkyAppearanceSample At(ObserverLocation observer, DateTime utcDateTime, double? siteSqm)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        var sun = SolarSystemCalculator.GetSunPosition(utc);
        var moon = SolarSystemCalculator.GetMoonPosition(utc);
        var sunAltitude = CoordinateTransformer.ToHorizontal(sun.Equatorial, observer, utc).AltitudeDegrees;
        var geocentricMoon = CoordinateTransformer.ToHorizontal(moon.Equatorial, observer, utc).AltitudeDegrees;
        // Same topocentric parallax as AstronomyService.GetMoon.
        var moonAltitude = geocentricMoon - moon.HorizontalParallaxDegrees * Math.Cos(double.DegreesToRadians(geocentricMoon));
        var zenith = SkyBrightness.Zenith(siteSqm, sunAltitude, moonAltitude, SolarSystemCalculator.GetMoonPhaseAngleDegrees(moon, sun));
        return new SkyAppearanceSample(utc, sunAltitude, moonAltitude, zenith, SkyBrightness.NakedEyeLimitingMagnitude(zenith));
    }

    /// <summary>Samples from <paramref name="startUtc"/> to <paramref name="endUtc"/> (inclusive) every <paramref name="step"/>.</summary>
    public static IReadOnlyList<SkyAppearanceSample> Sample(
        ObserverLocation observer, DateTime startUtc, DateTime endUtc, TimeSpan step, double? siteSqm)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(endUtc, startUtc);

        var samples = new List<SkyAppearanceSample>();
        for (var time = startUtc; time <= endUtc; time += step)
        {
            samples.Add(At(observer, time, siteSqm));
        }

        return samples;
    }
}
