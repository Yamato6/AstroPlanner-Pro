using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Main window: owns the feature view models, the global top bar (date/time, live mode,
/// active location) and the update banner. Feature view models share state through
/// <see cref="IObserverContext"/>, never through references to each other's internals.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    /// <summary>How often the time is refreshed in live mode.</summary>
    public static readonly TimeSpan LiveUpdateInterval = TimeSpan.FromMinutes(1);

    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly IUpdateChecker? _updateChecker;
    private SynchronizationContext? _uiContext;
    private ITimer? _liveTimer;
    private bool _isApplyingLiveTime;

    public ShellViewModel(
        IObserverContext context,
        WeatherState weather,
        TonightViewModel tonight,
        TargetsViewModel targets,
        SkyViewModel sky,
        ObjectDetailViewModel detail,
        SessionPlanViewModel plan,
        LogbookViewModel logbook,
        LocationsViewModel locations,
        SettingsViewModel settings,
        IAppLogger logger,
        TimeProvider? timeProvider = null,
        IUpdateChecker? updateChecker = null,
        EquipmentViewModel? equipment = null)
    {
        Equipment = equipment;
        _context = context;
        _weather = weather;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _updateChecker = updateChecker;
        Tonight = tonight;
        Targets = targets;
        Sky = sky;
        Detail = detail;
        Plan = plan;
        Logbook = logbook;
        Locations = locations;
        Settings = settings;

        Targets.CatalogLoaded += (_, _) => Logbook.UseCatalog(Targets.AllObjects.Select(row => row.Target));
        _context.PropertyChanged += OnContextPropertyChanged;
        _context.Recalculated += (_, _) => OnPropertyChanged(nameof(LocationSummary));
        Settings.PropertyChanged += OnSettingsPropertyChanged;
        Settings.SettingsSaved += OnSettingsSaved;

        // The persisted settings win over runtime values.
        _weather.IsEnabled = settings.WeatherForecastEnabled;
        _context.LocationName = DescribeLocation();
        _context.ZenithSqm = locations.ActiveLocation?.ZenithSqm;
        _context.Darkness = settings.AllowNauticalTwilight ? DarknessDefinition.Nautical : DarknessDefinition.Astronomical;
        _context.ExtinctionCoefficient = settings.ExtinctionCoefficient;
        _context.Initialize(settings.Latitude, settings.Longitude, settings.Elevation, settings.TimeZoneId, settings.MinimumAltitude,
            horizonMask: locations.ActiveLocation?.HorizonMask ?? HorizonMask.Flat);
    }

    /// <summary>Shared observing state (location, time, minimum altitude).</summary>
    public IObserverContext Observer => _context;

    /// <summary>Cloud forecast for the active location.</summary>
    public WeatherState Weather => _weather;

    public TonightViewModel Tonight { get; }
    public TargetsViewModel Targets { get; }
    public SkyViewModel Sky { get; }
    public ObjectDetailViewModel Detail { get; }
    public SessionPlanViewModel Plan { get; }
    public LogbookViewModel Logbook { get; }
    public LocationsViewModel Locations { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>Equipment inventory and the active rig selector (null only in tests that do not need it).</summary>
    public EquipmentViewModel? Equipment { get; }

    /// <summary>Loads the catalog (and with it the saved plan).</summary>
    [RelayCommand]
    private Task LoadAsync() => Targets.LoadCommand.ExecuteAsync(null);

    // ─── Location ───────────────────────────────────────────────────────

    public string ActiveLocationName => _context.LocationName;

    public string LocationSummary =>
        string.Create(CultureInfo.CurrentCulture, $"{_context.Latitude:0.####}°, {_context.Longitude:0.####}°");

    private string DescribeLocation() => Locations.ActiveLocation?.Name
        ?? (Settings.IsManualLocation
            ? Strings.LocationManual
            : Settings.SelectedCity?.DisplayName ?? Strings.LocationSelectCity);

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsViewModel.SelectedCity) or nameof(SettingsViewModel.IsManualLocation))
        {
            _context.LocationName = DescribeLocation();
        }
    }

    /// <summary>Applies the settings saved in the Settings window.</summary>
    private void OnSettingsSaved(ObserverSettings settings)
    {
        Targets.MaximumMagnitude = settings.MaximumMagnitude;
        Locations.Reload();
        _context.LocationName = DescribeLocation();
        _context.ZenithSqm = Locations.ActiveLocation?.ZenithSqm;
        _context.Darkness = settings.Darkness;
        _context.ExtinctionCoefficient = settings.ExtinctionCoefficient;
        _context.Apply(
            settings.LatitudeDegrees, settings.LongitudeDegrees, settings.ElevationMeters,
            settings.TimeZoneId, settings.MinimumAltitude, Locations.ActiveLocation?.HorizonMask ?? HorizonMask.Flat);

        if (_weather.IsEnabled != settings.WeatherForecastEnabled)
        {
            _weather.IsEnabled = settings.WeatherForecastEnabled;
            _ = _weather.RefreshSafeAsync();
        }
    }

    // ─── Date and time ──────────────────────────────────────────────────

    public IReadOnlyList<int> Hours { get; } = [.. Enumerable.Range(0, 24)];
    public IReadOnlyList<int> Minutes { get; } = [.. Enumerable.Range(0, 60)];

    public DateTime ObservationDateTime
    {
        get => _context.ObservationDateTime;
        set => _context.ObservationDateTime = value;
    }

    /// <summary>Date part of <see cref="ObservationDateTime"/>, for the calendar picker.</summary>
    public DateTime ObservationDate
    {
        get => ObservationDateTime.Date;
        set => ObservationDateTime = value.Date + ObservationDateTime.TimeOfDay;
    }

    /// <summary>Hour (0-23) of <see cref="ObservationDateTime"/>.</summary>
    public int ObservationHour
    {
        get => ObservationDateTime.Hour;
        set => ObservationDateTime = ObservationDateTime.Date.AddHours(value).AddMinutes(ObservationDateTime.Minute);
    }

    /// <summary>Minute (0-59) of <see cref="ObservationDateTime"/>.</summary>
    public int ObservationMinute
    {
        get => ObservationDateTime.Minute;
        set => ObservationDateTime = ObservationDateTime.Date.AddHours(ObservationDateTime.Hour).AddMinutes(value);
    }

    /// <summary>Moves the observation time by the given minutes (e.g. ±60).</summary>
    [RelayCommand]
    private void ShiftObservationTime(string minutes)
    {
        if (int.TryParse(minutes, CultureInfo.InvariantCulture, out var delta))
        {
            ObservationDateTime = ObservationDateTime.AddMinutes(delta);
        }
    }

    /// <summary>Current time at the observer's location (not the PC's time zone).</summary>
    [RelayCommand]
    private void SetNow() => ObservationDateTime = _context.Now;

    /// <summary>Live mode: the time follows the clock and is refreshed every minute.</summary>
    [ObservableProperty]
    public partial bool IsLive { get; set; }

    partial void OnIsLiveChanged(bool value)
    {
        _liveTimer?.Dispose();
        _liveTimer = null;

        if (value)
        {
            // The timer fires on another thread; its changes go back to the UI thread through
            // this context. It is captured here (live mode is switched on from the UI) and not
            // in the constructor: the view model may be created before WPF's context exists,
            // and then the change would arrive from another thread and crash the app.
            _uiContext = SynchronizationContext.Current;
            ApplyLiveTime();
            _liveTimer = _time.CreateTimer(_ => OnLiveTimerTick(), null, LiveUpdateInterval, LiveUpdateInterval);
        }
    }

    private void OnLiveTimerTick()
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            ApplyLiveTime();
        }
        else
        {
            _uiContext.Post(_ => ApplyLiveTime(), null);
        }
    }

    private void ApplyLiveTime()
    {
        if (!IsLive)
        {
            return;
        }

        _isApplyingLiveTime = true;
        try
        {
            ObservationDateTime = _context.Now;
        }
        finally
        {
            _isApplyingLiveTime = false;
        }
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IObserverContext.ObservationDateTime):
                // Choosing a time by hand leaves live mode.
                if (IsLive && !_isApplyingLiveTime)
                {
                    IsLive = false;
                }

                OnPropertyChanged(nameof(ObservationDateTime));
                OnPropertyChanged(nameof(ObservationDate));
                OnPropertyChanged(nameof(ObservationHour));
                OnPropertyChanged(nameof(ObservationMinute));
                break;
            case nameof(IObserverContext.TimeZoneId):
                // In live mode "now" depends on the observer's time zone.
                if (IsLive)
                {
                    ApplyLiveTime();
                }

                break;
            case nameof(IObserverContext.LocationName):
                OnPropertyChanged(nameof(ActiveLocationName));
                break;
        }
    }

    // ─── Updates ────────────────────────────────────────────────────────

    /// <summary>A newer published version, if any.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateBannerText), nameof(HasUpdate))]
    public partial UpdateInfo? AvailableUpdate { get; private set; }

    public bool HasUpdate => AvailableUpdate is not null;

    public string? UpdateBannerText => AvailableUpdate is null
        ? null
        : DisplayNames.Format(Strings.UpdateAvailableFormat, AvailableUpdate.Name);

    /// <summary>Looks for a version newer than <paramref name="current"/> if enabled in Settings. Never throws.</summary>
    public async Task CheckForUpdatesAsync(AppVersion current)
    {
        if (!Settings.CheckForUpdates || _updateChecker is null)
        {
            return;
        }

        try
        {
            AvailableUpdate = await _updateChecker.CheckAsync(current);
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error while checking for updates.", ex);
        }
    }

    [RelayCommand]
    private void DismissUpdate() => AvailableUpdate = null;

    public void Dispose()
    {
        _liveTimer?.Dispose();
        _liveTimer = null;
    }
}
