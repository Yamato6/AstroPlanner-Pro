using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;

namespace AstroPlanner.Application.ViewModels;

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
    public string SqmText => Location.ZenithSqm is { } sqm
        ? DisplayNames.Format(Strings.LocationsSqmFormat, sqm, SkyBrightness.BortleFromSqm(sqm))
        : Strings.LocationsSqmUnknown;
}

/// <summary>An editable point of the horizon profile (azimuth and altitude in degrees).</summary>
public sealed partial class HorizonPointRow(double azimuth, double altitude, Action changed) : ObservableObject
{
    [ObservableProperty]
    public partial double Azimuth { get; set; } = azimuth;

    [ObservableProperty]
    public partial double Altitude { get; set; } = altitude;

    partial void OnAzimuthChanged(double value) => changed();

    partial void OnAltitudeChanged(double value) => changed();
}

/// <summary>
/// Saved observing sites (spec §4.8): save the site in use, switch to another one, delete, and
/// edit a site's horizon profile and measured sky brightness (SQM).
/// The active site lives in the settings (<see cref="SettingsViewModel.ActiveLocationId"/>);
/// switching goes through the settings so that everything recalculates once.
/// </summary>
public sealed partial class LocationsViewModel : ObservableObject
{
    private readonly ILocationRepository _repository;
    private readonly SettingsViewModel _settings;
    private readonly IObserverContext _context;
    private readonly IAppLogger _logger;
    private readonly ILightPollutionAtlas? _atlas;

    public LocationsViewModel(ILocationRepository repository, SettingsViewModel settings, IObserverContext context, IAppLogger logger,
        ILightPollutionAtlas? atlas = null)
    {
        _atlas = atlas;
        _repository = repository;
        _settings = settings;
        _context = context;
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
        var selectedId = SelectedLocation?.Id;
        try
        {
            var activeId = _settings.ActiveLocationId;
            Locations.Clear();
            foreach (var location in _repository.GetAll())
            {
                Locations.Add(new LocationItemViewModel(location, location.Id == activeId));
            }

            SelectedLocation = Locations.FirstOrDefault(l => l.Id == selectedId);
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

    // ---- site details: horizon profile and SQM (spec §4.2 [P1], §4.8) ---------------------------

    /// <summary>Location whose details are edited (the row selected in the table).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(SaveDetailsCommand), nameof(RevertDetailsCommand), nameof(AddHorizonPointCommand), nameof(ClearHorizonCommand))]
    public partial LocationItemViewModel? SelectedLocation { get; set; }

    partial void OnSelectedLocationChanged(LocationItemViewModel? value)
    {
        LoadDetails();
        OnPropertyChanged(nameof(AtlasSqmText));
    }

    /// <summary>
    /// Zenith sky quality of the selected location from the World Atlas 2015 grid (used when no
    /// SQM is typed), or why there is none.
    /// </summary>
    public string? AtlasSqmText => SelectedLocation?.Location is not { } location ? null
        : _atlas?.ArtificialBrightness(location.LatitudeDegrees, location.LongitudeDegrees) is { } artificial
            ? DisplayNames.Format(Strings.LocationsAtlasSqmFormat, SkyBrightness.SqmFromArtificial(artificial),
                SkyBrightness.BortleFromSqm(SkyBrightness.SqmFromArtificial(artificial)))
            : Strings.LocationsAtlasNone;

    public bool HasSelection => SelectedLocation is not null;

    /// <summary>Points of the horizon profile being edited.</summary>
    public ObservableCollection<HorizonPointRow> HorizonPoints { get; } = [];

    /// <summary>Measured zenith brightness typed by the user (empty = unknown).</summary>
    [ObservableProperty]
    public partial string ZenithSqmText { get; set; } = "";

    partial void OnZenithSqmTextChanged(string value) => OnPropertyChanged(nameof(BortleText));

    /// <summary>Approximate Bortle class of the SQM typed (display only).</summary>
    public string? BortleText => ParseSqm(ZenithSqmText, out var sqm) && sqm is { } value
        ? DisplayNames.Format(Strings.LocationsBortleFormat, SkyBrightness.BortleFromSqm(value))
        : null;

    [ObservableProperty]
    public partial string? DetailsMessage { get; private set; }

    [ObservableProperty]
    public partial bool DetailsHasError { get; private set; }

    /// <summary>Changes whenever the horizon preview must be redrawn.</summary>
    [ObservableProperty]
    public partial int HorizonVersion { get; private set; }

    private void LoadDetails()
    {
        HorizonPoints.Clear();
        DetailsMessage = null;
        DetailsHasError = false;
        if (SelectedLocation?.Location is { } location)
        {
            foreach (var point in location.HorizonMask.Points)
            {
                HorizonPoints.Add(new HorizonPointRow(point.AzimuthDegrees, point.AltitudeDegrees, OnHorizonEdited));
            }

            ZenithSqmText = location.ZenithSqm?.ToString("0.00", CultureInfo.CurrentCulture) ?? "";
        }
        else
        {
            ZenithSqmText = "";
        }

        HorizonVersion++;
    }

    private void OnHorizonEdited() => HorizonVersion++;

    /// <summary>Adds a point 30° after the last one (or north at 10°).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void AddHorizonPoint()
    {
        var azimuth = HorizonPoints.Count == 0 ? 0 : (HorizonPoints.Max(p => p.Azimuth) + 30) % 360;
        while (HorizonPoints.Any(p => p.Azimuth == azimuth))
        {
            azimuth = (azimuth + 1) % 360;
        }

        HorizonPoints.Add(new HorizonPointRow(azimuth, 10, OnHorizonEdited));
        HorizonVersion++;
    }

    [RelayCommand]
    private void RemoveHorizonPoint(HorizonPointRow? row)
    {
        if (row is not null && HorizonPoints.Remove(row))
        {
            HorizonVersion++;
        }
    }

    /// <summary>Back to a flat horizon.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ClearHorizon()
    {
        HorizonPoints.Clear();
        HorizonVersion++;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RevertDetails() => LoadDetails();

    /// <summary>
    /// The profile being edited as a mask, or null with the reason when a point is out of range
    /// or two share an azimuth.
    /// </summary>
    public HorizonMask? BuildHorizonMask(out string? error)
    {
        error = null;
        foreach (var point in HorizonPoints)
        {
            if (!double.IsFinite(point.Azimuth) || point.Azimuth is < 0 or >= 360)
            {
                error = DisplayNames.Format(Strings.HorizonAzimuthRangeFormat, point.Azimuth);
                return null;
            }

            if (!double.IsFinite(point.Altitude) || point.Altitude is < -10 or > 90)
            {
                error = DisplayNames.Format(Strings.HorizonAltitudeRangeFormat, point.Altitude);
                return null;
            }
        }

        if (HorizonPoints.GroupBy(p => p.Azimuth).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
        {
            error = DisplayNames.Format(Strings.HorizonDuplicateAzimuthFormat, duplicate.Key);
            return null;
        }

        return new HorizonMask(HorizonPoints.Select(p => new HorizonPoint(p.Azimuth, p.Altitude)));
    }

    /// <summary>The profile sampled every degree for the preview (empty while it is invalid).</summary>
    public IReadOnlyList<(double AzimuthDegrees, double AltitudeDegrees)> GetHorizonPreview()
    {
        var mask = BuildHorizonMask(out _);
        return mask is null ? [] : [.. Enumerable.Range(0, 361).Select(az => ((double)az, mask.AltitudeAt(az)))];
    }

    private static bool ParseSqm(string text, out double? sqm)
    {
        sqm = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if ((double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
             || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            && value is >= SkyBrightness.MinimumSqm and <= SkyBrightness.MaximumSqm)
        {
            sqm = value;
            return true;
        }

        return false;
    }

    /// <summary>Saves the profile and the SQM; if the site is in use, the planner picks them up at once.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SaveDetails()
    {
        if (SelectedLocation?.Location is not { } location)
        {
            return;
        }

        var mask = BuildHorizonMask(out var error);
        if (mask is null)
        {
            DetailsMessage = error;
            DetailsHasError = true;
            return;
        }

        if (!ParseSqm(ZenithSqmText, out var sqm))
        {
            DetailsMessage = DisplayNames.Format(Strings.LocationsSqmRangeFormat, SkyBrightness.MinimumSqm, SkyBrightness.MaximumSqm);
            DetailsHasError = true;
            return;
        }

        var updated = location with { HorizonMask = mask, ZenithSqm = sqm };
        try
        {
            _repository.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Data.Common.DbException)
        {
            _logger.LogError("Could not save the location details.", ex);
            DetailsMessage = ex.Message;
            DetailsHasError = true;
            return;
        }

        var isActive = SelectedLocation.IsActive;
        Reload();
        if (isActive)
        {
            _context.ZenithSqm = sqm;
            _context.HorizonMask = mask;
        }

        DetailsMessage = Strings.LocationsDetailsSaved;
        DetailsHasError = false;
    }
}