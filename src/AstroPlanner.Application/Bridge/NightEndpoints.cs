using AstroPlanner.Application.Services;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// <c>night.get</c>: tonight sampled every 10 min of the site's clock for the planetarium's darkness bar (Sun and Moon altitudes,
/// the Moon's illumination, cloud cover), with the same Core sampling the feasibility model uses
/// (<see cref="NightSkySamples"/>). <c>night.changed</c> is announced only when the night, the
/// site or the forecast changed, not on every change of the observation time.
/// </summary>
public sealed class NightEndpoints : IEndpointModule
{
    /// <summary>Samples are this far apart.</summary>
    public const int StepMinutes = 10;

    /// <summary>The bar starts this long before sunset and ends this long after sunrise.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromHours(1);

    private readonly IObserverContext _context;
    private readonly WeatherState? _weather;
    private readonly ChangeFeed _changes;
    private object? _key;
    private RpcDispatcher? _rpc;

    /// <param name="weather">The forecast, for the cloud cover; null without one.</param>
    public NightEndpoints(IObserverContext context, WeatherState? weather, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _weather = weather;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.NightChanged, new NightChangedEvent(revision)));
        _key = Key();
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.NightGet, (_, _) => Task.FromResult(Timeline()));
        _context.Recalculated += OnChanged;
        if (_weather is not null)
        {
            _weather.Changed += OnChanged;
        }
    }

    /// <summary>The night of the observation time; empty when the site's input is invalid.</summary>
    public NightTimeline Timeline()
    {
        if (_context.Night is not { } night || _context.InputError is not null)
        {
            return new NightTimeline(_changes.Revision, 0, StepMinutes, [], [], 0, []);
        }

        // The first sample is on a whole step of the site's clock (18:30, not 18:39), so the
        // scrubber's steps land on round times.
        var start = night.WindowStartLocal - Margin;
        var wide = night with
        {
            WindowStartLocal = start.AddTicks(-(start.Ticks % TimeSpan.FromMinutes(StepMinutes).Ticks)),
            WindowEndLocal = night.WindowEndLocal + Margin,
        };
        var samples = NightSkySamples.Create(_context.Observer, _context.TimeZone, wide, TimeSpan.FromMinutes(StepMinutes),
            weather: Forecast());
        var count = Math.Min(samples.Count, 400);
        var middle = count / 2;
        return new NightTimeline(
            _changes.Revision,
            count == 0 ? 0 : new DateTimeOffset(DateTime.SpecifyKind(samples.TimesUtc[0], DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            StepMinutes,
            [.. samples.SunAltitudeDegrees.Take(count).Select(Round)],
            [.. samples.MoonAltitudeDegrees.Take(count).Select(Round)],
            count == 0 ? 0 : Math.Clamp(Math.Round(samples.MoonIlluminatedFraction[middle] * 100, 1), 0, 100),
            samples.HasForecast ? [.. samples.CloudFraction.Take(count).Select(c => Math.Round(c * 100))] : []);
    }

    private WeatherForecast? Forecast() => _weather is { IsEnabled: true } ? _weather.Forecast : null;

    /// <summary>What the timeline depends on: a change of anything else (the time within the night) is not announced.</summary>
    private object? Key() => _context.Night is { } night
        ? (night, _context.Observer, _context.TimeZoneId, _context.InputError, Forecast()?.FetchedAtUtc)
        : null;

    private void OnChanged(object? sender, EventArgs e)
    {
        var key = Key();
        if (!Equals(key, _key))
        {
            _key = key;
            _changes.Invalidate();
        }
    }

    private static double Round(double degrees) => Math.Round(degrees, 1);

    public void Dispose()
    {
        _context.Recalculated -= OnChanged;
        if (_weather is not null)
        {
            _weather.Changed -= OnChanged;
        }

        _changes.Dispose();
        _rpc = null;
    }
}
