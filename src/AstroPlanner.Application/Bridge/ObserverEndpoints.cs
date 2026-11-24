using System.ComponentModel;
using System.Globalization;
using AstroPlanner.Application.Services;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// <c>observer.get</c> and <c>observer.changed</c>: the site, time and sky of
/// <see cref="IObserverContext"/>, the first area built on the snapshot + revision pattern
/// (<see cref="IEndpointModule"/>).
/// </summary>
public sealed class ObserverEndpoints : IEndpointModule
{
    private const string LocalTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    private readonly IObserverContext _context;
    private readonly ChangeFeed _changes;
    private RpcDispatcher? _rpc;

    public ObserverEndpoints(IObserverContext context, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.ObserverChanged, new ObserverChangedEvent(revision)));
    }

    /// <summary>The revision of the snapshot <c>observer.get</c> returns now.</summary>
    public int Revision => _changes.Revision;

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.ObserverGet, (_, _) => Task.FromResult(Snapshot()));
        _context.PropertyChanged += OnPropertyChanged;
        _context.Recalculated += OnRecalculated;
    }

    /// <summary>The current state as a contract (read on the UI thread).</summary>
    public ObserverSnapshot Snapshot()
    {
        var context = _context;
        return new ObserverSnapshot(
            _changes.Revision,
            Truncate(context.LocationName ?? "", 200),
            Math.Clamp(context.Latitude, -90, 90),
            Math.Clamp(context.Longitude, -180, 180),
            Math.Clamp(context.Elevation, -1000, 10000),
            IanaId(context.TimeZone),
            Local(context.ObservationDateTime),
            Math.Clamp(context.MinimumAltitude, -90, 90),
            Math.Clamp(context.EffectiveZenithSqm, 0, 30),
            context.ZenithSqmSource switch
            {
                SkyQualitySource.Manual => SkyQualitySources.Manual,
                SkyQualitySource.Atlas => SkyQualitySources.Atlas,
                _ => SkyQualitySources.Default
            },
            context.Night is { } night
                ? new ObserverNight(Local(night.WindowStartLocal), Local(night.WindowEndLocal), Local(night.SunsetLocal),
                    Local(night.SunriseLocal), Local(night.AstronomicalDuskLocal), Local(night.AstronomicalDawnLocal))
                : null,
            context.Moon is { } moon
                ? new ObserverMoon(Math.Clamp(moon.IlluminatedFraction * 100, 0, 100), Math.Clamp(moon.Horizontal.AltitudeDegrees, -90, 90), moon.IsWaxing)
                : null,
            context.InputError is { } error ? Truncate(error, 500) : null);
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e) => _changes.Invalidate();

    private void OnRecalculated(object? sender, EventArgs e) => _changes.Invalidate();

    /// <summary>The zone's IANA id ("America/Mexico_City"), which the page's Intl understands; Windows ids are converted.</summary>
    public static string IanaId(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.HasIanaId ? zone.Id
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana
            : zone.Id;
    }

    private static string Local(DateTime time) => time.ToString(LocalTimeFormat, CultureInfo.InvariantCulture);

    private static string? Local(DateTime? time) => time is { } value ? Local(value) : null;

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    public void Dispose()
    {
        _context.PropertyChanged -= OnPropertyChanged;
        _context.Recalculated -= OnRecalculated;
        _changes.Dispose();
        _rpc = null;
    }
}
