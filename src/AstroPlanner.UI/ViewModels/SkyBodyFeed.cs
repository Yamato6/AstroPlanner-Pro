using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.SkyBridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// The time-dependent messages of the sky (spec §5.3): the site, the frame transform from Core,
/// the Sun/Moon/planet/comet/asteroid tracks, the satellites, the sky appearance and the clock.
/// Tracks cover ± <see cref="TrackHalfWindow"/> around the sky time and are re-sent when the time
/// leaves them; while playing <see cref="RefreshWhilePlaying"/> keeps them current.
/// </summary>
internal sealed class SkyBodyFeed
{
    /// <summary>Body tracks cover ± this around the sky time; regenerated when the time leaves them.</summary>
    public static readonly TimeSpan TrackHalfWindow = TimeSpan.FromHours(12);

    public static readonly TimeSpan TrackStep = TimeSpan.FromMinutes(10);

    /// <summary>A new frame transform is sent when the sky time moved more than this from the last one.</summary>
    public static readonly TimeSpan FrameRefresh = TimeSpan.FromMinutes(10);

    /// <summary>Satellite tracks are re-sent this long before the sky time leaves their ±15 min window.</summary>
    private static readonly TimeSpan SatelliteResendMargin = TimeSpan.FromMinutes(5);

    private readonly SkySession _session;
    private readonly Func<IEnumerable<CelestialObject>> _catalog;
    private readonly SatelliteState? _satellites;
    private DateTime? _frameEpochUtc;
    private DateTime? _tracksCenterUtc;
    private DateTime? _satellitesCenterUtc;
    private int _sentSatelliteCount = -1;

    /// <param name="catalog">The catalog's objects: the comets and asteroids to draw, and the planets' names.</param>
    public SkyBodyFeed(SkySession session, Func<IEnumerable<CelestialObject>> catalog, SatelliteState? satellites)
    {
        _session = session;
        _catalog = catalog;
        _satellites = satellites;
    }

    /// <summary>Most satellites drawn at once (those above the horizon in the ±15 min window, brightest first).</summary>
    public const int SatelliteCountLimit = 60;

    /// <summary>Faintest comet or asteroid drawn in the sky.</summary>
    public const double SmallBodyMagnitudeLimit = 12;

    /// <summary>Most comets and asteroids drawn (the brightest), to keep the tracks message small.</summary>
    public const int SmallBodyCountLimit = 40;

    private IObserverContext Context => _session.Context;

    /// <summary>The site's zenith SQM for the renderer: manual or atlas; null (natural sky) when unknown.</summary>
    public double? SiteSqm => Context.ZenithSqmSource == SkyQualitySource.Default ? null : Context.EffectiveZenithSqm;

    /// <summary>True when the satellites changed since the last tracks were sent.</summary>
    public bool SatellitesChanged => _satellites is not null && _satellites.Satellites.Count != _sentSatelliteCount;

    /// <summary>Observer, frame, tracks and time for the current sky time (after a site or time change).</summary>
    public void SendSiteAndTime()
    {
        if (!_session.IsReady || Context.InputError is not null)
        {
            return;
        }

        var utc = _session.Clock.UtcNow;
        _session.Send(SkyMessageCodec.SetObserver(SkyBridgeMapper.ToPayload(
            Context.LocationName, Context.Observer, Context.MinimumAltitude, Context.HorizonMask)));
        SendFrame(utc);
        SendBodies(utc);
        SendTime();
    }

    /// <summary>While playing: a new frame and tracks when the sky time has moved far enough, and the clock.</summary>
    public void RefreshWhilePlaying()
    {
        var utc = _session.Clock.UtcNow;
        if (_frameEpochUtc is null || (utc - _frameEpochUtc.Value).Duration() > FrameRefresh)
        {
            SendFrame(utc);
        }

        if (_tracksCenterUtc is null || (utc - _tracksCenterUtc.Value).Duration() > TrackHalfWindow - TimeSpan.FromHours(1)
            || (_satellitesCenterUtc is { } satellites && (utc - satellites).Duration() > SatelliteTracks.HalfWindow - SatelliteResendMargin))
        {
            SendBodies(utc);
        }

        SendTime();
    }

    public void SendFrame(DateTime utc)
    {
        if (!_session.IsReady)
        {
            return;
        }

        _frameEpochUtc = utc;
        _session.Send(SkyMessageCodec.FrameTransform(SkyBridgeMapper.ToPayload(SkyFrame.Compute(Context.Observer, utc))));
    }

    public void SendBodies(DateTime utc)
    {
        if (!_session.IsReady)
        {
            return;
        }

        _tracksCenterUtc = utc;
        var satellites = SkySatellites(utc);
        _satellitesCenterUtc = satellites.Count > 0 ? utc : null;
        _session.Send(SkyMessageCodec.MovingBodies(SkyBridgeMapper.MovingBodies(
            Context.Observer, utc, TrackHalfWindow, TrackStep, BodyDisplayName, SkySmallBodies(utc), satellites)));
        SendAppearance(utc);
    }

    /// <summary>Sky brightness over the same window as the body tracks (they are refreshed together).</summary>
    public void SendAppearance(DateTime utc)
    {
        if (!_session.IsReady || Context.InputError is not null)
        {
            return;
        }

        _session.Send(SkyMessageCodec.SetSkyAppearance(SkyBridgeMapper.Appearance(
            Context.Observer, utc, TrackHalfWindow, TrackStep, SiteSqm)));
    }

    public void SendTime() =>
        _session.Send(SkyMessageCodec.SetTime(new TimePayload(SkyMessageCodec.FormatUtc(_session.Clock.UtcNow), _session.Clock.Rate)));

    /// <summary>"ISS (ZARYA) · next pass 19:42–19:48, max 54°" for a satellite picked in the sky; null for anything else.</summary>
    public string? SatelliteSelectionText(string id)
    {
        if (_satellites is null || !id.StartsWith("sat-", StringComparison.Ordinal)
            || !int.TryParse(id.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || _satellites.Find(number) is not { } satellite)
        {
            return null;
        }

        var next = _satellites.NextPasses(number, _session.Clock.UtcNow, 1);
        if (next.Count == 0)
        {
            return DisplayNames.Format(Strings.SkySatelliteNoPassFormat, satellite.Name);
        }

        var pass = next[0];
        return DisplayNames.Format(Strings.SkySatelliteNextPassFormat, satellite.Name,
            TimeZoneInfo.ConvertTimeFromUtc(pass.RiseUtc, Context.TimeZone), TimeZoneInfo.ConvertTimeFromUtc(pass.SetUtc, Context.TimeZone),
            pass.MaxAltitudeDegrees, pass.IsVisible ? Strings.SkySatelliteVisible : Strings.SkySatelliteNotVisible);
    }

    /// <summary>The Sun and the Moon by their names; other ids as they are.</summary>
    public static string BodyName(string id) => id switch
    {
        "Sun" => Strings.SkySun,
        "Moon" => Strings.SkyMoon,
        _ => id
    };

    private string BodyDisplayName(string id) =>
        _catalog().FirstOrDefault(o => o.Id == id) is { } target ? target.CommonName : BodyName(id);

    private IReadOnlyList<SatelliteTrack> SkySatellites(DateTime utc)
    {
        _sentSatelliteCount = _satellites?.Satellites.Count ?? 0;
        return _satellites is { IsEnabled: true, Satellites.Count: > 0 } state
            ? SatelliteTracks.Get(state.Satellites, Context.Observer, utc, SatelliteCountLimit)
            : [];
    }

    /// <summary>The comets and asteroids of the catalog bright enough to draw at <paramref name="utc"/>, brightest first.</summary>
    private List<CelestialObject> SkySmallBodies(DateTime utc) =>
    [
        .. _catalog()
            .Where(target => target.IsSmallBody)
            .Select(target => (Target: target, Magnitude: MagnitudeAt(target, utc)))
            .Where(b => double.IsFinite(b.Magnitude) && b.Magnitude <= SmallBodyMagnitudeLimit)
            .OrderBy(b => b.Magnitude)
            .Take(SmallBodyCountLimit)
            .Select(b => b.Target)
    ];

    private static double MagnitudeAt(CelestialObject target, DateTime utc) =>
        target.Orbit is { } orbit ? OrbitCalculator.GetPosition(orbit, utc).Magnitude : double.NaN;
}
