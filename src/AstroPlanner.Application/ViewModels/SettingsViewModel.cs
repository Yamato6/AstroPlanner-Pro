using AstroPlanner.Core.Imaging.Feasibility;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;
using static AstroPlanner.Application.Services.DisplayNames;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// Settings window: latitude, longitude, elevation, time zone, minimum altitude and online
/// features. Persists the settings and notifies the shell through <see cref="SettingsSaved"/>.
/// </summary>
public sealed partial class SettingsViewModel : ObservableValidator
{
    private readonly ISettingsService _settingsService;
    private readonly ICityRepository _cityRepository;
    private readonly IAppLogger _logger;
    private string? _activeLocationId;
    private (double Latitude, double Longitude, double Elevation, string TimeZoneId) _activeLocationValues;

    private readonly IOnlineCache? _cache;

    public SettingsViewModel(ISettingsService settingsService, ICityRepository cityRepository, IAppLogger logger, IOnlineCache? cache = null,
        ThemeState? theme = null)
    {
        _settingsService = settingsService;
        _cityRepository = cityRepository;
        _logger = logger;
        _cache = cache;
        Theme = theme ?? new ThemeState();
        var settings = _settingsService.Load();
        LoadFrom(settings);
        Theme.Apply(settings.Theme);
    }

    /// <summary>The theme in use; the saved one is applied at startup and on save.</summary>
    public ThemeState Theme { get; }

    /// <summary>Switches between the dark and the night-vision theme and saves only that choice.</summary>
    public void SetTheme(AppTheme theme)
    {
        try
        {
            var settings = _settingsService.Load();
            settings.Theme = theme;
            _settingsService.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The theme still changes for this session.
            _logger.LogError("Could not save the theme.", ex);
        }

        IsNightTheme = theme == AppTheme.Night;
        Theme.Apply(theme);
    }

    /// <summary>Whether there is a cache of online answers to show and clear.</summary>
    public bool HasCache => _cache is not null;

    /// <summary>"Cache: 12.3 MB".</summary>
    public string CacheSizeText => _cache is null ? "" : DisplayNames.Format(Strings.SettingsCacheSizeFormat, _cache.SizeBytes / 1048576.0);

    /// <summary>Deletes the cached online answers (forecasts; images and descriptions from M4b).</summary>
    [RelayCommand]
    private void ClearCache()
    {
        _cache?.Clear();
        OnPropertyChanged(nameof(CacheSizeText));
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

    /// <summary>Fetch the seeing and transparency forecast (7Timer!).</summary>
    [ObservableProperty]
    public partial bool AstroForecastEnabled { get; set; } = true;

    /// <summary>Framing preview from survey tiles (CDS, ESA ESASky).</summary>
    [ObservableProperty]
    public partial bool SurveyImagesEnabled { get; set; } = true;

    /// <summary>Object descriptions from Wikipedia.</summary>
    [ObservableProperty]
    public partial bool WikipediaEnabled { get; set; } = true;

    /// <summary>Online name resolution (CDS Sesame) in the sky search.</summary>
    [ObservableProperty]
    public partial bool NameResolverEnabled { get; set; } = true;

    /// <summary>Comets and bright asteroids (Minor Planet Center).</summary>
    [ObservableProperty]
    public partial bool SmallBodiesEnabled { get; set; } = true;

    /// <summary>Bright satellites and space stations (CelesTrak).</summary>
    [ObservableProperty]
    public partial bool SatellitesEnabled { get; set; } = true;

    /// <summary>Check for a new version at startup.</summary>
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; } = true;

    /// <summary>Count nautical twilight (Sun below −12°) as imaging time instead of only astronomical darkness.</summary>
    [ObservableProperty]
    public partial bool AllowNauticalTwilight { get; set; }

    /// <summary>V extinction coefficient, mag/air mass.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 1.5, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.ExtinctionRange))]
    public partial double ExtinctionCoefficient { get; set; } = 0.25;

    /// <summary>Red night-vision theme (Settings › Display).</summary>
    [ObservableProperty]
    public partial bool IsNightTheme { get; set; }

    // ─── Advanced: calibration of the feasibility model (spec §8.7) ─────────

    /// <summary>Seeing FWHM without a forecast, arcseconds.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0.5, 8, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedSeeingRange))]
    public partial double TuningSeeingArcsec { get; set; }

    /// <summary>Core brightening of extended objects, magnitudes.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 3, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedCoreBrighteningRange))]
    public partial double TuningCoreBrighteningMagnitudes { get; set; }

    /// <summary>Overhead between subs, seconds.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 60, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedOverheadRange))]
    public partial double TuningOverheadSeconds { get; set; }

    /// <summary>Longest suggested sub, seconds.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(10, 1800, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedMaximumSubRange))]
    public partial double TuningMaximumSubSeconds { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(1, 100, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedSnrRange))]
    public partial double TuningSnrMarginal { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(1, 100, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedSnrRange))]
    public partial double TuningSnrGood { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [Range(1, 100, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedSnrRange))]
    public partial double TuningSnrExcellent { get; set; }

    /// <summary>Pixels needed to split a double star.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(1, 5, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.AdvancedSplitPixelsRange))]
    public partial double TuningDoubleStarSplitPixels { get; set; }

    /// <summary>The calibration as edited (values equal to their default are stored as defaults).</summary>
    public FeasibilityTuning Tuning => FeasibilityTuning.From(FeasibilityConstants.Default with
    {
        SeeingArcsec = TuningSeeingArcsec,
        CoreBrighteningMagnitudes = TuningCoreBrighteningMagnitudes,
        OverheadSeconds = TuningOverheadSeconds,
        MaximumSubSeconds = TuningMaximumSubSeconds,
        SnrMarginal = TuningSnrMarginal,
        SnrGood = TuningSnrGood,
        SnrExcellent = TuningSnrExcellent,
        DoubleStarSplitPixels = TuningDoubleStarSplitPixels
    });

    /// <summary>Puts every advanced constant back to its default (saved with the other settings).</summary>
    [RelayCommand]
    private void ResetTuning() => LoadTuning(new FeasibilityTuning());

    private void LoadTuning(FeasibilityTuning tuning)
    {
        var constants = tuning.Apply(FeasibilityConstants.Default);
        TuningSeeingArcsec = constants.SeeingArcsec;
        TuningCoreBrighteningMagnitudes = constants.CoreBrighteningMagnitudes;
        TuningOverheadSeconds = constants.OverheadSeconds;
        TuningMaximumSubSeconds = constants.MaximumSubSeconds;
        TuningSnrMarginal = constants.SnrMarginal;
        TuningSnrGood = constants.SnrGood;
        TuningSnrExcellent = constants.SnrExcellent;
        TuningDoubleStarSplitPixels = constants.DoubleStarSplitPixels;
    }

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

    /// <summary>
    /// Saved location in use, or null. It is kept while the coordinates stay those of the
    /// location; editing them by hand makes the site an unsaved one.
    /// </summary>
    public string? ActiveLocationId =>
        _activeLocationId is not null && _activeLocationValues == (Latitude, Longitude, Elevation, TimeZoneId)
            ? _activeLocationId
            : null;

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

            if (!(TuningSnrMarginal < TuningSnrGood && TuningSnrGood < TuningSnrExcellent))
            {
                HasError = true;
                StatusMessage = Strings.AdvancedSnrOrder;
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
                ActiveLocationId = ActiveLocationId,
                MinimumAltitude = MinimumAltitude,
                MaximumMagnitude = MaximumMagnitude,
                WeatherForecastEnabled = WeatherForecastEnabled,
                AstroForecastEnabled = AstroForecastEnabled,
                SurveyImagesEnabled = SurveyImagesEnabled,
                WikipediaEnabled = WikipediaEnabled,
                NameResolverEnabled = NameResolverEnabled,
                SmallBodiesEnabled = SmallBodiesEnabled,
                SatellitesEnabled = SatellitesEnabled,
                CheckForUpdates = CheckForUpdates,
                Darkness = AllowNauticalTwilight ? DarknessDefinition.Nautical : DarknessDefinition.Astronomical,
                ExtinctionCoefficient = ExtinctionCoefficient,
                Theme = IsNightTheme ? AppTheme.Night : AppTheme.Dark,
                Tuning = Tuning
            };

            _settingsService.Save(settings);
            HasError = false;
            StatusMessage = Strings.SettingsSaved;
            Theme.Apply(settings.Theme);
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

    /// <summary>Makes a saved location the active site and saves the settings.</summary>
    public void UseLocation(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        IsManualLocation = true;
        SelectedCity = null;
        Latitude = location.LatitudeDegrees;
        Longitude = location.LongitudeDegrees;
        Elevation = location.ElevationMeters;
        TimeZoneId = location.WindowsTimeZoneId;
        OnPropertyChanged(nameof(SelectedTimeZone));
        RememberActiveLocation(location.Id);
        Save();
    }

    /// <summary>Forgets the active saved location (e.g. it was deleted) and saves the settings.</summary>
    public void ClearActiveLocation()
    {
        _activeLocationId = null;
        Save();
    }

    private void RememberActiveLocation(string? id)
    {
        _activeLocationId = id;
        _activeLocationValues = (Latitude, Longitude, Elevation, TimeZoneId);
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
        AstroForecastEnabled = settings.AstroForecastEnabled;
        SurveyImagesEnabled = settings.SurveyImagesEnabled;
        WikipediaEnabled = settings.WikipediaEnabled;
        NameResolverEnabled = settings.NameResolverEnabled;
        SmallBodiesEnabled = settings.SmallBodiesEnabled;
        SatellitesEnabled = settings.SatellitesEnabled;
        CheckForUpdates = settings.CheckForUpdates;
        AllowNauticalTwilight = settings.Darkness == DarknessDefinition.Nautical;
        ExtinctionCoefficient = settings.ExtinctionCoefficient;
        IsNightTheme = settings.Theme == AppTheme.Night;
        LoadTuning(settings.Tuning ?? new FeasibilityTuning());
        RememberActiveLocation(settings.ActiveLocationId);
        // If the city list is already loaded the saved city is restored; otherwise
        // LoadCitiesAsync selects it once loaded.
        SelectedCity = settings.IsManualLocation
            ? null
            : Cities.FirstOrDefault(city => city.Id == settings.CityId);
        OnPropertyChanged(nameof(SelectedTimeZone));
    }
}
