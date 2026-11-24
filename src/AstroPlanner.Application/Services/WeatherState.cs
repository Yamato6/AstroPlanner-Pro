using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Weather;

namespace AstroPlanner.Application.Services;

/// <summary>
/// The forecasts for the active location, shared by the dashboard, the plan, the object detail,
/// the charts and the feasibility model: Open-Meteo (clouds, dew point, wind) and 7Timer! ASTRO
/// (seeing, transparency). Both are optional: when one fails the app keeps working and says so.
/// Each is re-fetched only when it is older than its cache duration or the location moved more than
/// ~1 km, and never more often than <see cref="RetryDelay"/> after a failed or offline attempt.
/// The services themselves keep a disk cache, so a forecast may come back old (offline): the
/// <c>…IsStale</c> properties say so.
/// </summary>
public sealed partial class WeatherState : ObservableObject
{
    /// <summary>The Open-Meteo forecast is reused for this long for the same location.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <summary>The 7Timer! forecast is reused for this long (it is issued every 6 hours).</summary>
    public static readonly TimeSpan AstroCacheDuration = TimeSpan.FromHours(3);

    /// <summary>After a failure (or an offline answer from the cache) there is no retry before this delay, unless the location changes.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly IObserverContext _context;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly Slot<WeatherForecast> _weather;
    private readonly Slot<AstroForecast> _astro;

    public WeatherState(
        IObserverContext context,
        IAppLogger logger,
        TimeProvider? timeProvider = null,
        IWeatherForecastService? service = null,
        IAstroForecastService? astroService = null)
    {
        _context = context;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _weather = new Slot<WeatherForecast>(
            service is null ? null : (lat, lon) => service.GetForecastAsync(lat, lon), CacheDuration, f => f.FetchedAtUtc, f => (f.Latitude, f.Longitude));
        _astro = new Slot<AstroForecast>(
            astroService is null ? null : (lat, lon) => astroService.GetForecastAsync(lat, lon), AstroCacheDuration, f => f.FetchedAtUtc, f => (f.Latitude, f.Longitude));
        _context.Recalculated += (_, _) =>
        {
            if (_context.InputError is null)
            {
                _ = RefreshSafeAsync();
            }
        };
    }

    /// <summary>Raised whenever a forecast, its error or an enabled flag may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Open-Meteo is enabled in Settings. Changing it does not fetch by itself.</summary>
    public bool IsEnabled
    {
        get => _weather.Enabled;
        set => _weather.Enabled = value;
    }

    /// <summary>7Timer! is enabled in Settings. Changing it does not fetch by itself.</summary>
    public bool IsAstroEnabled
    {
        get => _astro.Enabled;
        set => _astro.Enabled = value;
    }

    /// <summary>A request is in progress.</summary>
    [ObservableProperty]
    public partial bool IsFetching { get; private set; }

    /// <summary>Reason of the last Open-Meteo failure, or null.</summary>
    public string? LastError => _weather.Error;

    /// <summary>Reason of the last 7Timer! failure, or null.</summary>
    public string? AstroLastError => _astro.Error;

    /// <summary>Open-Meteo forecast for the current location (rounded to ~1 km), or null.</summary>
    public WeatherForecast? Forecast => _weather.For(RoundedLocation);

    /// <summary>7Timer! forecast for the current location (rounded to ~1 km), or null.</summary>
    public AstroForecast? AstroForecast => _astro.For(RoundedLocation);

    /// <summary>The Open-Meteo forecast shown is older than its cache duration (it came from the disk cache while offline).</summary>
    public bool IsForecastStale => Forecast is { } f && Now - f.FetchedAtUtc >= CacheDuration;

    /// <summary>The 7Timer! forecast shown is older than its cache duration.</summary>
    public bool IsAstroForecastStale => AstroForecast is { } f && Now - f.FetchedAtUtc >= AstroCacheDuration;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private (double Latitude, double Longitude) RoundedLocation =>
        (Math.Round(_context.Latitude, 2), Math.Round(_context.Longitude, 2));

    /// <summary>Forecast cloud cover (%) for a local time; null without a forecast for it.</summary>
    public int? CloudCoverAtLocal(DateTime? localTime) =>
        localTime is { } local ? Forecast?.At(_context.ToUtc(local))?.CloudCoverPercent : null;

    /// <summary>Hourly cloud cover inside the observing night, in local time, for the chart.</summary>
    public IReadOnlyList<(DateTime StartLocal, DateTime EndLocal, int CloudCoverPercent)> GetNightCloudCover()
    {
        if (Forecast is not { } forecast || _context.Night is not { } night)
        {
            return [];
        }

        var zone = _context.TimeZone;
        return forecast
            .Between(_context.ToUtc(night.WindowStartLocal), _context.ToUtc(night.WindowEndLocal))
            .Select(h => (
                Max(TimeZoneInfo.ConvertTimeFromUtc(h.TimeUtc, zone), night.WindowStartLocal),
                Min(TimeZoneInfo.ConvertTimeFromUtc(h.TimeUtc.AddHours(1), zone), night.WindowEndLocal),
                h.CloudCoverPercent))
            .ToList();

        static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
    }

    /// <summary>
    /// Seeing, transparency, dew and wind over the dark night (the whole night window when it never
    /// gets dark), for the rig's mount; null when the night is unknown.
    /// </summary>
    public NightConditions? GetNightConditions(MountKind? mount)
    {
        if (_context.Night is not { } night || _context.InputError is not null)
        {
            return null;
        }

        var (start, end) = night.HasAstronomicalDarkness
            ? (night.AstronomicalDuskLocal!.Value, night.AstronomicalDawnLocal!.Value)
            : (night.WindowStartLocal, night.WindowEndLocal);
        return NightConditions.Create(_context.ToUtc(start), _context.ToUtc(end), Forecast, AstroForecast, mount);
    }

    /// <summary>
    /// Fetches what is needed (no forecast for this location, or older than its cache duration).
    /// Never throws: failures end up in <see cref="LastError"/> / <see cref="AstroLastError"/>.
    /// </summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (_context.InputError is not null || IsFetching)
        {
            OnChanged();
            return;
        }

        var location = RoundedLocation;
        IsFetching = true;
        try
        {
            await Task.WhenAll(
                _weather.RefreshAsync(location, _context.Latitude, _context.Longitude, Now, force, _logger),
                _astro.RefreshAsync(location, _context.Latitude, _context.Longitude, Now, force, _logger));
        }
        finally
        {
            IsFetching = false;
        }

        OnChanged();

        // If the location changed during the request, fetch the new one.
        if (RoundedLocation != location && LastError is null && AstroLastError is null)
        {
            await RefreshAsync();
        }
    }

    /// <summary>Fire-and-forget version for recalculations: logs any unexpected error.</summary>
    public async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error while refreshing the forecast.", ex);
        }
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Forecast));
        OnPropertyChanged(nameof(AstroForecast));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One forecast service: the last result, its error and when it was last tried.</summary>
    private sealed class Slot<T>(
        Func<double, double, Task<T>>? fetch,
        TimeSpan cacheDuration,
        Func<T, DateTime> fetchedAt,
        Func<T, (double Latitude, double Longitude)> locationOf)
        where T : class
    {
        private T? _value;
        private DateTime _lastAttemptUtc = DateTime.MinValue;
        private (double Latitude, double Longitude)? _lastAttemptLocation;

        public bool Enabled { get; set; }
        public string? Error { get; private set; }

        public T? For((double Latitude, double Longitude) location) =>
            Enabled && _value is not null && Round(locationOf(_value)) == location ? _value : null;

        public async Task RefreshAsync(
            (double Latitude, double Longitude) location, double latitude, double longitude, DateTime now, bool force, IAppLogger logger)
        {
            if (!Enabled || fetch is null)
            {
                return;
            }

            var current = For(location);
            var fresh = current is not null && now - fetchedAt(current) < cacheDuration;
            var triedRecently = _lastAttemptLocation == location && now - _lastAttemptUtc < RetryDelay;
            if (!force && (fresh || triedRecently))
            {
                return;
            }

            _lastAttemptUtc = now;
            _lastAttemptLocation = location;
            try
            {
                _value = await fetch(latitude, longitude);
                Error = null;
            }
            catch (WeatherForecastException ex)
            {
                logger.LogInfo($"Forecast unavailable: {ex.Message}");
                Error = ex.Message;
            }
        }

        private static (double, double) Round((double Latitude, double Longitude) value) =>
            (Math.Round(value.Latitude, 2), Math.Round(value.Longitude, 2));
    }
}
