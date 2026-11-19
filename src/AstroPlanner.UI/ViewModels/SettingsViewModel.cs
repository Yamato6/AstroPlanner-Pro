using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Settings window: latitude, longitude, elevation, time zone, minimum altitude and online
/// features. Persists the settings and notifies the shell through <see cref="SettingsSaved"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableValidator
{
    private readonly ISettingsService _settingsService;
    private readonly ICityRepository _cityRepository;
    private readonly IAppLogger _logger;

    public SettingsViewModel(ISettingsService settingsService, ICityRepository cityRepository, IAppLogger logger)
    {
        _settingsService = settingsService;
        _cityRepository = cityRepository;
        _logger = logger;
        LoadFrom(_settingsService.Load());
    }

    /// <summary>Raised after saving; the shell applies the new settings and recalculates.</summary>
    public event Action<ObserverSettings>? SettingsSaved;

    public IReadOnlyList<TimeZoneInfo> AvailableTimeZones { get; } = TimeZoneInfo.GetSystemTimeZones().ToList();

    public IReadOnlyList<City> Cities { get; private set; } = [];

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-90, 90, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.LatitudeRange))]
    public partial double Latitude { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-180, 180, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.LongitudeRange))]
    public partial double Longitude { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-500, 9000, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.ElevationRange))]
    public partial double Elevation { get; set; }

    [ObservableProperty]
    public partial string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 90, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.MinimumAltitudeRange))]
    public partial double MinimumAltitude { get; set; } = 20;

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-2, 20, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.MaximumMagnitudeRange))]
    public partial double MaximumMagnitude { get; set; } = 12;

    /// <summary>Fetch the cloud forecast (Open-Meteo).</summary>
    [ObservableProperty]
    public partial bool WeatherForecastEnabled { get; set; } = true;

    /// <summary>Check for a new version at startup.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; } = true;

    [ObservableProperty]
    public partial City? SelectedCity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCitySelectionEnabled), nameof(IsManualFieldsEnabled))]
    public partial bool IsManualLocation { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    public bool IsCitySelectionEnabled => !IsManualLocation;

    public bool IsManualFieldsEnabled => IsManualLocation;

    /// <summary>Selected time zone, to bind a ComboBox directly.</summary>
    public TimeZoneInfo? SelectedTimeZone
    {
        get => AvailableTimeZones.FirstOrDefault(z => z.Id == TimeZoneId) ?? TimeZoneInfo.Local;
        set
        {
            if (value is null) return;
            TimeZoneId = value.Id;
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task LoadCitiesAsync()
    {
        try
        {
            Cities = await _cityRepository.GetAllAsync();
            OnPropertyChanged(nameof(Cities));

            if (!IsManualLocation && SelectedCity is null)
            {
                SelectedCity = Cities.FirstOrDefault(city => city.Id == _settingsService.Load().CityId)
                    ?? (Cities.Count > 0 ? Cities[0] : null);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            _logger.LogError("Could not load the city list.", ex);
            HasError = true;
            StatusMessage = Format(Strings.SettingsCitiesLoadFailedFormat, ex.Message);
        }
    }

    [RelayCommand]
    private void UseManualLocation()
    {
        IsManualLocation = true;
        SelectedCity = null;
        StatusMessage = Strings.SettingsManualModeOn;
        HasError = false;
    }

    partial void OnSelectedCityChanged(City? value)
    {
        if (value is null || IsManualLocation) return;

        Latitude = value.LatitudeDegrees;
        Longitude = value.LongitudeDegrees;
        Elevation = value.ElevationMeters;
        TimeZoneId = value.TimeZoneId;
        OnPropertyChanged(nameof(SelectedTimeZone));
    }

    partial void OnIsManualLocationChanged(bool value)
    {
        if (!value && SelectedCity is null && Cities.Count > 0)
        {
            SelectedCity = Cities[0];
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            ValidateAllProperties();
            if (HasErrors)
            {
                HasError = true;
                StatusMessage = GetErrors().First().ErrorMessage;
                return;
            }

            var settings = new ObserverSettings
            {
                CityId = IsManualLocation ? null : SelectedCity?.Id,
                IsManualLocation = IsManualLocation,
                LatitudeDegrees = Latitude,
                LongitudeDegrees = Longitude,
                ElevationMeters = Elevation,
                TimeZoneId = TimeZoneId,
                MinimumAltitude = MinimumAltitude,
                MaximumMagnitude = MaximumMagnitude,
                WeatherForecastEnabled = WeatherForecastEnabled,
                CheckForUpdates = CheckForUpdates
            };

            _settingsService.Save(settings);
            HasError = false;
            StatusMessage = Strings.SettingsSaved;
            SettingsSaved?.Invoke(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the settings.", ex);
            HasError = true;
            StatusMessage = Format(Strings.SettingsSaveFailedFormat, ex.Message);
        }
    }

    [RelayCommand]
    private void LoadDefaults()
    {
        LoadFrom(new ObserverSettings());
        StatusMessage = Strings.SettingsDefaultsLoaded;
        HasError = false;
    }

    /// <summary>Discards unsaved changes and goes back to the persisted settings.</summary>
    public void RevertChanges() => Apply(_settingsService.Load());

    /// <summary>Applies settings to the observable properties.</summary>
    public void Apply(ObserverSettings settings)
    {
        LoadFrom(settings);
        StatusMessage = null;
        HasError = false;
    }

    private void LoadFrom(ObserverSettings settings)
    {
        IsManualLocation = settings.IsManualLocation;
        Latitude = settings.LatitudeDegrees;
        Longitude = settings.LongitudeDegrees;
        Elevation = settings.ElevationMeters;
        TimeZoneId = settings.TimeZoneId;
        MinimumAltitude = settings.MinimumAltitude;
        MaximumMagnitude = settings.MaximumMagnitude;
        WeatherForecastEnabled = settings.WeatherForecastEnabled;
        CheckForUpdates = settings.CheckForUpdates;
        // If the city list is already loaded the saved city is restored; otherwise
        // LoadCitiesAsync selects it once loaded.
        SelectedCity = settings.IsManualLocation
            ? null
            : Cities.FirstOrDefault(city => city.Id == settings.CityId);
        OnPropertyChanged(nameof(SelectedTimeZone));
    }
}
