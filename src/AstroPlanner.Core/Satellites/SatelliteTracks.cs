using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Satellites;

/// <summary>A satellite's sky track with whether the Sun lights it at each sample.</summary>
public sealed record SatelliteTrack(string Name, MovingBodyTrack Track, IReadOnlyList<bool> Sunlit);

/// <summary>
/// Satellite tracks for the sky renderer. A satellite crosses the sky in minutes, so its track is
/// short and fine (± <see cref="HalfWindow"/> every <see cref="Step"/>) and is re-sent as time
/// moves; like the Moon's, each sample is the topocentric direction rotated back to J2000 with
/// the inverse of <see cref="SkyFrame.J2000ToHorizontalAt"/>.
/// </summary>
public static class SatelliteTracks
{
    public static readonly TimeSpan HalfWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan Step = TimeSpan.FromSeconds(10);

    /// <summary>Drawing magnitude of a satellite whose standard magnitude is unknown (the faint end of the bright list).</summary>
    public const double UnknownMagnitude = 4.5;

    /// <summary>Id of a satellite in the renderer (never clashes with a catalog id).</summary>
    public static string Id(int catalogNumber) => "sat-" + catalogNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Tracks around <paramref name="utc"/> of the satellites above the horizon at some moment of
    /// the window, at most <paramref name="maxCount"/>, brightest first. Decayed orbits are skipped.
    /// </summary>
    public static IReadOnlyList<SatelliteTrack> Get(
        IEnumerable<TrackedSatellite> satellites,
        ObserverLocation observer,
        DateTime utc,
        int maxCount)
    {
        ArgumentNullException.ThrowIfNull(satellites);
        ArgumentNullException.ThrowIfNull(observer);
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var frame = SkyFrame.Compute(observer, utc);
        var start = utc - HalfWindow;
        var count = (int)(2 * HalfWindow.Ticks / Step.Ticks) + 1;
        var toJ2000 = new Matrix3[count];
        for (var i = 0; i < count; i++)
        {
            toJ2000[i] = frame.J2000ToHorizontalAt(start + (Step * i)).Transpose();
        }

        var result = new List<(SatelliteTrack Track, double Magnitude)>();
        foreach (var satellite in satellites)
        {
            try
            {
                var samples = new List<TrackSample>(count);
                var sunlit = new List<bool>(count);
                var aboveHorizon = false;
                for (var i = 0; i < count; i++)
                {
                    var time = start + (Step * i);
                    var observation = SatelliteCalculator.Observe(satellite, observer, time);
                    aboveHorizon |= observation.Horizontal.AltitudeDegrees > 0;
                    samples.Add(new TrackSample(time, toJ2000[i].Transform(SkyFrame.FromHorizontal(observation.Horizontal))));
                    sunlit.Add(observation.Illumination != SatelliteIllumination.Umbra);
                }

                if (!aboveHorizon)
                {
                    continue;
                }

                var now = SatelliteCalculator.Observe(satellite, observer, utc);
                var magnitude = satellite.IntrinsicMagnitude is { } intrinsic
                    ? SatelliteCalculator.Magnitude(intrinsic, now.RangeKm, now.PhaseAngleDegrees)!.Value
                    : UnknownMagnitude;
                var state = new MovingBodyState(Id(satellite.CatalogNumber), MovingBodyKind.Satellite, now.Horizontal, now.Equatorial,
                    Math.Round(magnitude, 2), ApparentDiameterArcsec: 0, now.PhaseAngleDegrees,
                    IlluminatedFraction: now.Illumination == SatelliteIllumination.Umbra ? 0 : 1, BrightLimbAngleDegrees: 0);
                result.Add((new SatelliteTrack(satellite.Name, new MovingBodyTrack(state, samples), sunlit), magnitude));
            }
            catch (SatellitePropagationException)
            {
                // A decayed orbit is not drawn.
            }
        }

        return [.. result.OrderBy(r => r.Magnitude).Take(maxCount).Select(r => r.Track)];
    }
}
