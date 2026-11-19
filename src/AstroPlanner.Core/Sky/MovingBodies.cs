using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Sky;

/// <summary>Kind of a body that moves against the stars.</summary>
public enum MovingBodyKind
{
    Sun,
    Moon,
    Planet
}

/// <summary>
/// A Solar System body seen by an observer at an instant.
/// </summary>
/// <param name="Id">"Sun", "Moon" or the planet id used in the catalog.</param>
/// <param name="Horizontal">Topocentric geometric position (parallax included for the Moon, no refraction).</param>
/// <param name="Equatorial">Geocentric coordinates, equinox of date.</param>
/// <param name="ApparentDiameterArcsec">Apparent diameter of the disc.</param>
/// <param name="PhaseAngleDegrees">Sun-body-Earth angle (0 = fully lit; 0 for the Sun).</param>
/// <param name="IlluminatedFraction">Lit fraction of the disc (1 for the Sun).</param>
/// <param name="BrightLimbAngleDegrees">Position angle of the bright limb from north through east (0 for the Sun).</param>
public sealed record MovingBodyState(
    string Id,
    MovingBodyKind Kind,
    HorizontalCoordinates Horizontal,
    EquatorialCoordinates Equatorial,
    double Magnitude,
    double ApparentDiameterArcsec,
    double PhaseAngleDegrees,
    double IlluminatedFraction,
    double BrightLimbAngleDegrees);

/// <summary>One sample of a body track: the direction expressed in the J2000 frame of <see cref="SkyFrame"/>.</summary>
public readonly record struct TrackSample(DateTime Utc, Vector3d J2000Direction);

/// <summary>A body with its sampled track; the renderer interpolates between samples.</summary>
public sealed record MovingBodyTrack(MovingBodyState State, IReadOnlyList<TrackSample> Samples);

/// <summary>
/// Positions of the Sun, the Moon and the planets for the sky renderer. Each sample's horizontal
/// direction is rotated back with the inverse of <see cref="SkyFrame.J2000ToHorizontalAt"/>, so
/// that the renderer, applying the same frame, reproduces the Core position exactly at the samples
/// (including the Moon's topocentric parallax, which a plain J2000 vector cannot express).
/// </summary>
public static class MovingBodyCalculator
{
    /// <summary>Mean radius of the Moon, km.</summary>
    private const double MoonRadiusKm = 1737.4;

    /// <summary>Apparent diameter of the Sun at 1 au, arcseconds (Meeus ch. 55).</summary>
    private const double SunDiameterAt1AuArcsec = 1919.26;

    private const double KilometresPerAu = 149_597_870.7;

    /// <summary>All bodies (Sun, Moon, planets in catalog order) at <paramref name="utcDateTime"/>.</summary>
    public static IReadOnlyList<MovingBodyState> GetBodies(ObserverLocation observer, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);

        var sun = SolarSystemCalculator.GetSunPosition(utc);
        var moon = SolarSystemCalculator.GetMoonPosition(utc);
        var bodies = new List<MovingBodyState>(2 + SolarSystemCatalog.Planets.Count)
        {
            new("Sun", MovingBodyKind.Sun,
                CoordinateTransformer.ToHorizontal(sun.Equatorial, observer, utc),
                sun.Equatorial,
                Magnitude: -26.74,
                ApparentDiameterArcsec: SunDiameterAt1AuArcsec * KilometresPerAu / sun.DistanceKm,
                PhaseAngleDegrees: 0,
                IlluminatedFraction: 1,
                BrightLimbAngleDegrees: 0)
        };

        var geocentricMoon = CoordinateTransformer.ToHorizontal(moon.Equatorial, observer, utc);
        var moonPhase = SolarSystemCalculator.GetMoonPhaseAngleDegrees(moon, sun);
        bodies.Add(new MovingBodyState("Moon", MovingBodyKind.Moon,
            // Same parallax model as AstronomyService.GetMoon.
            geocentricMoon with
            {
                AltitudeDegrees = geocentricMoon.AltitudeDegrees
                    - moon.HorizontalParallaxDegrees * Math.Cos(AstronomyMath.DegreesToRadiansValue(geocentricMoon.AltitudeDegrees))
            },
            moon.Equatorial,
            Magnitude: MoonMagnitude(moonPhase),
            ApparentDiameterArcsec: 2 * AstronomyMath.RadiansToDegreesValue(Math.Asin(MoonRadiusKm / moon.DistanceKm)) * 3600,
            PhaseAngleDegrees: moonPhase,
            IlluminatedFraction: SolarSystemCalculator.GetMoonIlluminatedFraction(moon, sun),
            BrightLimbAngleDegrees: SolarSystemCalculator.GetBrightLimbPositionAngleDegrees(moon.Equatorial, sun.Equatorial)));

        foreach (var planet in SolarSystemCatalog.Planets)
        {
            var position = PlanetCalculator.GetPosition(planet.Planet!.Value, utc);
            bodies.Add(new MovingBodyState(planet.Id, MovingBodyKind.Planet,
                CoordinateTransformer.ToHorizontal(position.Equatorial, observer, utc),
                position.Equatorial,
                position.Magnitude,
                position.ApparentDiameterArcsec,
                position.PhaseAngleDegrees,
                position.IlluminatedFraction,
                SolarSystemCalculator.GetBrightLimbPositionAngleDegrees(position.Equatorial, sun.Equatorial)));
        }

        return bodies;
    }

    /// <summary>
    /// Tracks of every body from <paramref name="startUtc"/> to <paramref name="endUtc"/> (inclusive)
    /// every <paramref name="step"/>; the state of each track is the one at <paramref name="stateUtc"/>.
    /// </summary>
    public static IReadOnlyList<MovingBodyTrack> GetTracks(
        ObserverLocation observer,
        DateTime stateUtc,
        DateTime startUtc,
        DateTime endUtc,
        TimeSpan step)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(step, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(endUtc, startUtc);

        var frame = SkyFrame.Compute(observer, stateUtc);
        var states = GetBodies(observer, stateUtc);
        var samples = states.Select(_ => new List<TrackSample>()).ToList();

        for (var time = startUtc; time <= endUtc; time += step)
        {
            var utc = DateTime.SpecifyKind(time, DateTimeKind.Utc);
            // The frame is orthogonal, so its inverse is its transpose.
            var toJ2000 = frame.J2000ToHorizontalAt(utc).Transpose();
            var bodies = GetBodies(observer, utc);
            for (var i = 0; i < bodies.Count; i++)
            {
                samples[i].Add(new TrackSample(utc, toJ2000.Transform(SkyFrame.FromHorizontal(bodies[i].Horizontal))));
            }
        }

        return [.. states.Select((state, i) => new MovingBodyTrack(state, samples[i]))];
    }

    /// <summary>Visual magnitude of the Moon from its phase angle (Allen, "Astrophysical Quantities").</summary>
    internal static double MoonMagnitude(double phaseAngleDegrees)
    {
        var i = Math.Abs(phaseAngleDegrees);
        return -12.73 + 0.026 * i + 4e-9 * Math.Pow(i, 4);
    }
}
