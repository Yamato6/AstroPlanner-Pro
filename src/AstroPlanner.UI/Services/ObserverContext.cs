using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Shared observing state: where (location, time zone), when (local wall-clock time)
/// and the minimum altitude, plus the night and Moon derived from them.
/// Feature view models read it instead of referencing each other.
/// </summary>
public interface IObserverContext : INotifyPropertyChanged, INotifyDataErrorInfo
{
    double Latitude { get; set; }
    double Longitude { get; set; }
    double Elevation { get; set; }
    string TimeZoneId { get; set; }

    /// <summary>Observation time as local wall-clock time in <see cref="TimeZone"/>.</summary>
    DateTime ObservationDateTime { get; set; }

    /// <summary>Minimum altitude (degrees) for an object to count as visible.</summary>
    double MinimumAltitude { get; set; }

    /// <summary>Display name of the active location (city or manual coordinates).</summary>
    string LocationName { get; set; }

    /// <summary>Message for the first invalid input (latitude, longitude, minimum altitude), or null.</summary>
    string? InputError { get; }

    /// <summary>Observing night (sunset → sunrise) containing <see cref="ObservationDateTime"/>.</summary>
    NightInfo? Night { get; }

    /// <summary>The Moon at <see cref="ObservationDateTime"/>.</summary>
    MoonState? Moon { get; }

    /// <summary>Day, twilight or astronomical darkness at <see cref="ObservationDateTime"/>.</summary>
    SkyCondition SkyCondition { get; }

    /// <summary>Configured time zone, falling back to the local one.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Current time in the observer's time zone (not the PC's), to the second.</summary>
    DateTime Now { get; }

    /// <summary>The observer; only meaningful while <see cref="InputError"/> is null.</summary>
    ObserverLocation Observer { get; }

    /// <summary>Converts a local wall-clock time in <see cref="TimeZone"/> to UTC.</summary>
    DateTime ToUtc(DateTime localWallClock);

    /// <summary>
    /// Sets the persisted values and the observation time (default: now), then calculates
    /// for the first time. Nothing is calculated before this call.
    /// </summary>
    void Initialize(
        double latitude, double longitude, double elevation, string timeZoneId, double minimumAltitude, DateTime? observationDateTime = null);

    /// <summary>Sets every value at once and recalculates a single time.</summary>
    void Apply(double latitude, double longitude, double elevation, string timeZoneId, double minimumAltitude);

    /// <summary>
    /// Raised after any change of location, time zone, time or minimum altitude, once the night
    /// and Moon have been recalculated (also when the input is invalid: see <see cref="InputError"/>).
    /// </summary>
    event EventHandler? Recalculated;

    /// <summary>Forces a recalculation (e.g. after the catalog has loaded).</summary>
    void Recalculate();
}

/// <summary>Default <see cref="IObserverContext"/>. Nothing is calculated until <see cref="Initialize"/>.</summary>
public sealed partial class ObserverContext(IAstronomyService astronomy, TimeProvider? timeProvider = null)
    : ObservableValidator, IObserverContext
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private bool _isInitialized;
    private bool _isBatchUpdating;

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-90, 90, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.LatitudeRange))]
    public partial double Latitude { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-180, 180, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.LongitudeRange))]
    public partial double Longitude { get; set; }

    [ObservableProperty]
    public partial double Elevation { get; set; }

    [ObservableProperty]
    public partial string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    [ObservableProperty]
    public partial DateTime ObservationDateTime { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 90, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.MinimumAltitudeRange))]
    public partial double MinimumAltitude { get; set; }

    [ObservableProperty]
    public partial string LocationName { get; set; } = "";

    [ObservableProperty]
    public partial string? InputError { get; private set; }

    [ObservableProperty]
    public partial NightInfo? Night { get; private set; }

    [ObservableProperty]
    public partial MoonState? Moon { get; private set; }

    [ObservableProperty]
    public partial SkyCondition SkyCondition { get; private set; } = SkyCondition.Dark;

    public event EventHandler? Recalculated;

    public TimeZoneInfo TimeZone => TimeZoneConversion.FindOrLocal(TimeZoneId);

    public DateTime Now
    {
        get
        {
            var now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZone).DateTime;
            return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
        }
    }

    public ObserverLocation Observer => new(Latitude, Longitude, Elevation);

    public DateTime ToUtc(DateTime localWallClock) => TimeZoneConversion.ToUtc(localWallClock, TimeZone);

    /// <remarks>
    /// Everything is assigned before calculating: half-set values (e.g. the date still at
    /// DateTime.MinValue) would produce an invalid night.
    /// </remarks>
    public void Initialize(
        double latitude, double longitude, double elevation, string timeZoneId, double minimumAltitude, DateTime? observationDateTime = null)
    {
        _isBatchUpdating = true;
        try
        {
            Latitude = latitude;
            Longitude = longitude;
            Elevation = elevation;
            TimeZoneId = timeZoneId;
            MinimumAltitude = minimumAltitude;
            ObservationDateTime = observationDateTime ?? Now;
        }
        finally
        {
            _isBatchUpdating = false;
        }

        _isInitialized = true;
        Recalculate();
    }

    public void Apply(double latitude, double longitude, double elevation, string timeZoneId, double minimumAltitude)
    {
        _isBatchUpdating = true;
        try
        {
            Latitude = latitude;
            Longitude = longitude;
            Elevation = elevation;
            MinimumAltitude = minimumAltitude;
            TimeZoneId = timeZoneId;
        }
        finally
        {
            _isBatchUpdating = false;
        }

        Recalculate();
    }

    partial void OnLatitudeChanged(double value) => RecalculateIfReady();
    partial void OnLongitudeChanged(double value) => RecalculateIfReady();
    partial void OnElevationChanged(double value) => RecalculateIfReady();
    partial void OnTimeZoneIdChanged(string value) => RecalculateIfReady();
    partial void OnObservationDateTimeChanged(DateTime value) => RecalculateIfReady();
    partial void OnMinimumAltitudeChanged(double value) => RecalculateIfReady();

    private void RecalculateIfReady()
    {
        if (!_isBatchUpdating)
        {
            Recalculate();
        }
    }

    public void Recalculate()
    {
        if (!_isInitialized)
        {
            return;
        }

        // With invalid input the previous night and Moon are kept: consumers show a
        // warning and keep their last valid results instead of emptying silently.
        InputError = GetInputError();
        if (InputError is null)
        {
            var observer = Observer;
            var utc = ToUtc(ObservationDateTime);
            Night = astronomy.GetNightInfo(observer, ObservationDateTime, TimeZone);
            Moon = astronomy.GetMoon(observer, utc);
            SkyCondition = astronomy.GetSkyCondition(observer, utc);
        }

        Recalculated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Checked here as well as with [Range] so as not to depend on the order in which
    /// the toolkit validates and notifies changes.
    /// </summary>
    private string? GetInputError()
    {
        if (double.IsNaN(Latitude) || Latitude is < -90 or > 90)
        {
            return Strings.LatitudeRangeCheckSettings;
        }

        if (double.IsNaN(Longitude) || Longitude is < -180 or > 180)
        {
            return Strings.LongitudeRangeCheckSettings;
        }

        if (double.IsNaN(MinimumAltitude) || MinimumAltitude is < 0 or > 90)
        {
            return Strings.MinimumAltitudeRange;
        }

        return null;
    }
}
