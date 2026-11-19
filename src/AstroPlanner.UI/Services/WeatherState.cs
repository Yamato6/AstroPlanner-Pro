using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>
/// The cloud forecast for the active location, shared by the dashboard, the plan, the
/// object detail and the altitude chart. Optional: when it fails the app keeps working
/// and says so. Re-fetches only when the forecast is older than
/// <see cref="CacheDuration"/> or the location moved more than ~1 km.
/// </summary>
public sealed partial class WeatherState : ObservableObject
{
    /// <summary>The forecast is reused for this long for the same location.</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <summary>After a failure there is no retry before this delay (unless the location changes).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private readonly IObserverContext _context;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly IWeatherForecastService? _service;
    private WeatherForecast? _forecast;
    private DateTime _lastFailedAttemptUtc = DateTime.MinValue;
    private (double Latitude, double Longitude)? _lastFailedLocation;

    public WeatherState(
        IObserverContext context, IAppLogger logger, TimeProvider? timeProvider = null, IWeatherForecastService? service = null)
    {
        _context = context;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _service = service;
        _context.Recalculated += (_, _) =>
        {
            if (_context.InputError is null)
            {
                _ = RefreshSafeAsync();
            }
        };
    }

    /// <summary>Raised whenever the forecast, its error or <see cref="IsEnabled"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether the forecast is enabled in Settings. Changing it does not fetch by itself.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>A request is in progress.</summary>
    [ObservableProperty]
    public partial bool IsFetching { get; private set; }

    /// <summary>Reason of the last failure, or null.</summary>
    public string? LastError { get; private set; }

    /// <summary>Forecast for the current location (rounded to ~1 km), or null.</summary>
    public WeatherForecast? Forecast =>
        IsEnabled && _forecast is not null
        && (Math.Round(_forecast.Latitude, 2), Math.Round(_forecast.Longitude, 2)) == RoundedLocation
            ? _forecast
            : null;

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
    /// Fetches the forecast if needed (none for this location, or older than
    /// <see cref="CacheDuration"/>). Never throws: failures end up in <see cref="LastError"/>.
    /// </summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (!IsEnabled || _service is null || _context.InputError is not null)
        {
            OnChanged();
            return;
        }

        var location = RoundedLocation;
        var now = _time.GetUtcNow().UtcDateTime;
        var current = Forecast;
        var recentFailure = _lastFailedLocation == location && now - _lastFailedAttemptUtc < RetryDelay;
        if (IsFetching
            || (!force && current is not null && now - current.FetchedAtUtc < CacheDuration)
            || (!force && current is null && recentFailure))
        {
            OnChanged();
            return;
        }

        IsFetching = true;
        try
        {
            _forecast = await _service.GetForecastAsync(_context.Latitude, _context.Longitude);
            LastError = null;
            _lastFailedLocation = null;
        }
        catch (WeatherForecastException ex)
        {
            _logger.LogInfo($"Pronóstico no disponible: {ex.Message}");
            LastError = ex.Message;
            _lastFailedAttemptUtc = now;
            _lastFailedLocation = location;
        }
        finally
        {
            IsFetching = false;
        }

        OnChanged();

        // If the location changed during the request, fetch the new one.
        if (RoundedLocation != location && LastError is null)
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
            _logger.LogError("Error inesperado al actualizar el pronóstico.", ex);
        }
    }

    private void OnChanged()
    {
        OnPropertyChanged(nameof(Forecast));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
