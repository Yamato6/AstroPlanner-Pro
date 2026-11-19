using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.SkyBridge;

/// <summary>Builds protocol payloads from Core results (the renderer never computes astronomy).</summary>
public static class SkyBridgeMapper
{
    /// <summary>Digits kept for unit vectors: 1e-7 rad ≈ 0.02″, far below what is drawn.</summary>
    private const int VectorDigits = 7;

    public static FrameTransformPayload ToPayload(SkyFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new FrameTransformPayload(
            SkyMessageCodec.FormatUtc(frame.EpochUtc),
            frame.Precession.ToArray(),
            frame.LatitudeDegrees,
            frame.LocalSiderealTimeDegrees,
            SkyFrame.SiderealRateDegreesPerSecond);
    }

    /// <summary>
    /// Tracks of every body around <paramref name="utc"/> (± <paramref name="halfWindow"/>, every
    /// <paramref name="step"/>) for the renderer to interpolate.
    /// </summary>
    public static MovingBodiesPayload MovingBodies(
        ObserverLocation observer,
        DateTime utc,
        TimeSpan halfWindow,
        TimeSpan step,
        Func<string, string>? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var start = utc - halfWindow;
        var tracks = MovingBodyCalculator.GetTracks(observer, utc, start, utc + halfWindow, step);

        return new MovingBodiesPayload(
        [
            .. tracks.Select(track => new MovingBodyDto(
                track.State.Id,
                track.State.Kind switch
                {
                    MovingBodyKind.Sun => "sun",
                    MovingBodyKind.Moon => "moon",
                    _ => "planet"
                },
                displayName?.Invoke(track.State.Id) ?? track.State.Id,
                Math.Round(track.State.Magnitude, 2),
                Math.Round(track.State.ApparentDiameterArcsec, 2),
                Math.Round(track.State.PhaseAngleDegrees, 3),
                Math.Round(track.State.IlluminatedFraction, 4),
                Math.Round(track.State.BrightLimbAngleDegrees, 3),
                new TrackDto(
                    SkyMessageCodec.FormatUtc(start),
                    step.TotalSeconds,
                    [.. track.Samples.SelectMany(s => new[] { s.J2000Direction.X, s.J2000Direction.Y, s.J2000Direction.Z })
                        .Select(c => Math.Round(c, VectorDigits))])))
        ]);
    }

    public static ObserverPayload ToPayload(string name, ObserverLocation observer, double minimumAltitudeDegrees, HorizonMask? horizon)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new ObserverPayload(
            name,
            observer.LatitudeDegrees,
            observer.LongitudeDegrees,
            observer.ElevationMeters,
            minimumAltitudeDegrees,
            [.. (horizon?.Points ?? []).Select(p => new HorizonPointDto(p.AzimuthDegrees, p.AltitudeDegrees))]);
    }

    /// <summary>Center-on payload for catalog coordinates (J2000).</summary>
    public static CenterOnPayload CenterOn(EquatorialCoordinates j2000, double? fovDegrees = null)
    {
        var v = SkyFrame.FromEquatorial(j2000);
        return new CenterOnPayload(Math.Round(v.X, VectorDigits), Math.Round(v.Y, VectorDigits), Math.Round(v.Z, VectorDigits), fovDegrees);
    }
}
