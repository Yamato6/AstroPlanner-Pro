using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>A saved location as shown in the Locations tab.</summary>
public sealed class LocationItemViewModel(Location location, bool isActive)
{
    public Location Location { get; } = location;
    public string Id => Location.Id;
    public string Name => Location.Name;
    public string Notes => Location.Notes;
    public bool IsActive { get; } = isActive;

    public string CoordinatesText => DisplayNames.Format(Strings.CoordinatesFormat, Location.LatitudeDegrees, Location.LongitudeDegrees);
    public string ElevationText => DisplayNames.Format(Strings.ElevationFormat, Location.ElevationMeters);
    public string TimeZoneText => Location.IanaTimeZoneId;
    public string HorizonText => Location.HorizonMask.IsFlat
        ? Strings.HorizonFlat
        : DisplayNames.Format(Strings.HorizonPointsFormat, Location.HorizonMask.Points.Count);
}

/// <summary>
/// Saved observing sites (spec §4.8): save the site in use, switch to another one, delete.
/// The active site lives in the settings (<see cref="SettingsViewModel.ActiveLocationId"/>);
/// switching goes through the settings so that everything recalculates once.
/// </summary>
public sealed partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _repository;
    private readonly SettingsViewModel _settings;
    private readonly IAppLogger _logger;

    public LocationsViewModel(ILocationRepository repository, SettingsViewModel settings, IAppLogger logger)
    {
        _repository = repository;
        _settings = settings;
        _logger = logger;
        _settings.SettingsSaved += _ => Reload();
        Reload();
    }

    public ObservableCollection<LocationItemViewModel> Locations { get; } = [];

    /// <summary>Name for the site in use when it is saved.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCurrentCommand))]
    public partial string NewLocationName { get; set; } = Strings.LocationsDefaultName;

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    /// <summary>The active saved location, or null when the site in use is not saved.</summary>
    public Location? ActiveLocation => Locations.FirstOrDefault(l => l.IsActive)?.Location;

    public void Reload()
    {
        try
        {
            var activeId = _settings.ActiveLocationId;
            Locations.Clear();
            foreach (var location in _repository.GetAll())
            {
                Locations.Add(new LocationItemViewModel(location, location.Id == activeId));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not load the saved locations.", ex);
            StatusMessage = ex.Message;
        }

        OnPropertyChanged(nameof(ActiveLocation));
    }

    private bool CanSaveCurrent() => !string.IsNullOrWhiteSpace(NewLocationName);

    /// <summary>Saves the site in use (from the settings) as a new location and makes it active.</summary>
    [RelayCommand(CanExecute = nameof(CanSaveCurrent))]
    private void SaveCurrent()
    {
        var timeZoneId = _settings.TimeZoneId;
        var location = new Location(
            Guid.NewGuid().ToString("N"),
            NewLocationName,
            _settings.Latitude,
            _settings.Longitude,
            _settings.Elevation,
            TimeZoneConversion.ToIanaId(timeZoneId) ?? "Etc/UTC",
            TimeZoneConversion.ToWindowsId(timeZoneId) ?? timeZoneId);

        _repository.Save(location);
        _settings.UseLocation(location);
        NewLocationName = Strings.LocationsDefaultName;
    }

    [RelayCommand]
    private void Use(LocationItemViewModel? item)
    {
        if (item is not null && !item.IsActive)
        {
            _settings.UseLocation(item.Location);
        }
    }

    [RelayCommand]
    private void Delete(LocationItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        _repository.Delete(item.Id);
        if (item.IsActive)
        {
            // The site stays in use, as an unsaved one.
            _settings.ClearActiveLocation();
        }
        else
        {
            Reload();
        }
    }
}
