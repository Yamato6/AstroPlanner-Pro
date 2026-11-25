using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Core.Sky;
using AstroPlanner.Core.SkyQuality;

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
    /// <paramref name="step"/>) for the renderer to interpolate: the Sun, the Moon, the planets and
    /// the given comets and asteroids (only those with a known magnitude), then the satellites with
    /// their own short, fine tracks (<see cref="SatelliteTracks"/>).
    /// </summary>
    public static MovingBodiesPayload MovingBodies(
        ObserverLocation observer,
        DateTime utc,
        TimeSpan halfWindow,
        TimeSpan step,
        Func<string, string>? displayName = null,
        IReadOnlyList<CelestialObject>? smallBodies = null,
        IReadOnlyList<SatelliteTrack>? satellites = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var start = utc - halfWindow;
        var tracks = MovingBodyCalculator.GetTracks(observer, utc, start, utc + halfWindow, step, smallBodies)
            .Where(track => double.IsFinite(track.State.Magnitude));

        return new MovingBodiesPayload(
        [
            .. tracks.Select(track => new MovingBodyDto(
                track.State.Id,
                track.State.Kind switch
                {
                    MovingBodyKind.Sun => "sun",
                    MovingBodyKind.Moon => "moon",
                    MovingBodyKind.Comet => "comet",
                    MovingBodyKind.Asteroid => "asteroid",
                    MovingBodyKind.Satellite => "satellite",
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
                        .Select(c => Math.Round(c, VectorDigits))]))),
            .. (satellites ?? []).Select(Satellite)
        ]);
    }

    private static MovingBodyDto Satellite(SatelliteTrack satellite)
    {
        var track = satellite.Track;
        return new MovingBodyDto(
            track.State.Id,
            "satellite",
            satellite.Name,
            Math.Round(track.State.Magnitude, 2),
            0,
            Math.Round(track.State.PhaseAngleDegrees, 3),
            track.State.IlluminatedFraction,
            0,
            new TrackDto(
                SkyMessageCodec.FormatUtc(track.Samples[0].Utc),
                track.Samples.Count > 1 ? (track.Samples[1].Utc - track.Samples[0].Utc).TotalSeconds : SatelliteTracks.Step.TotalSeconds,
                [.. track.Samples.SelectMany(s => new[] { s.J2000Direction.X, s.J2000Direction.Y, s.J2000Direction.Z })
                    .Select(c => Math.Round(c, VectorDigits))]),
            [.. satellite.Sunlit.Select(lit => lit ? 1 : 0)]);
    }

    /// <summary>
    /// Sky brightness around <paramref name="utc"/> (± <paramref name="halfWindow"/>, every
    /// <paramref name="step"/>) for a site of zenith SQM <paramref name="siteSqm"/> (null = unknown),
    /// V extinction <paramref name="extinction"/> and site-glow brightening per air mass
    /// <paramref name="brighteningPerAirmass"/> (the feasibility constants).
    /// </summary>
    public static SkyAppearancePayload Appearance(
        ObserverLocation observer,
        DateTime utc,
        TimeSpan halfWindow,
        TimeSpan step,
        double? siteSqm,
        double extinction = Airmass.DefaultExtinctionCoefficient,
        double brighteningPerAirmass = SkyBackground.DefaultBrighteningPerAirmass)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var start = utc - halfWindow;
        var samples = SkyAppearanceCalculator.Sample(observer, start, utc + halfWindow, step, siteSqm, extinction);
        return new SkyAppearancePayload(
            SkyMessageCodec.FormatUtc(start),
            step.TotalSeconds,
            [.. samples.Select(s => Math.Round(s.ZenithSqm, 3))],
            [.. samples.Select(s => Math.Round(s.LimitingMagnitude, 3))],
            [.. samples.Select(s => Part(s.TwilightSqm))],
            [.. samples.Select(s => Part(s.MoonlightSqm))],
            Math.Round(samples.Count > 0 ? samples[0].SiteSqm : SkyBrightness.NaturalSqm, 3),
            Math.Round(extinction, 4),
            Math.Round(brighteningPerAirmass, 4));
    }

    /// <summary>A part of the sky brightness for the protocol: rounded, or <see cref="SkyAppearancePayload.None"/>.</summary>
    private static double Part(double sqm) =>
        double.IsFinite(sqm) && sqm < SkyAppearancePayload.None ? Math.Round(sqm, 3) : SkyAppearancePayload.None;

    /// <summary>A J2000 direction for the protocol, rounded like every other vector.</summary>
    public static VectorDto ToVector(Vector3d direction) =>
        new(Math.Round(direction.X, VectorDigits), Math.Round(direction.Y, VectorDigits), Math.Round(direction.Z, VectorDigits));

    public static ObserverPayload ToPayload(string name, ObserverLocation observer, double minimumAltitudeDegrees, HorizonMask? horizon)
    {
        ArgumentNullException.ThrowIfNull(observer);
        return new ObserverPayload(
            name,
            observer.LatitudeDegrees,
            observer.LongitudeDegrees,
            observer.ElevationMeters,
            minimumAltitudeDegrees,
            [.. (horizon?.Points ?? []).Select(p => new HorizonPointDto(p.AzimuthDegrees, p.AltitudeDegrees))],
            RefractionModel.ForElevation(observer.ElevationMeters));
    }

    /// <summary>Center-on payload for catalog coordinates (J2000).</summary>
    public static CenterOnPayload CenterOn(EquatorialCoordinates j2000, double? fovDegrees = null)
    {
        var v = SkyFrame.FromEquatorial(j2000);
        return new CenterOnPayload(Math.Round(v.X, VectorDigits), Math.Round(v.Y, VectorDigits), Math.Round(v.Z, VectorDigits), fovDegrees);
    }
}
