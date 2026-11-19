using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Detail of the selected object: tonight's events, current position, Moon and clouds,
/// the data for its altitude chart, and the plan/logbook actions.
/// </summary>
public sealed partial class ObjectDetailViewModel : ObservableObject
{
    private static readonly TimeSpan CurveStep = TimeSpan.FromMinutes(20);

    private readonly IAstronomyService _astronomy;
    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly TargetsViewModel _targets;
    private readonly SessionPlanViewModel _plan;
    private readonly LogbookViewModel _logbook;

    public ObjectDetailViewModel(
        IAstronomyService astronomy,
        IObserverContext context,
        WeatherState weather,
        TargetsViewModel targets,
        SessionPlanViewModel plan,
        LogbookViewModel logbook)
    {
        _astronomy = astronomy;
        _context = context;
        _weather = weather;
        _targets = targets;
        _plan = plan;
        _logbook = logbook;

        _targets.SelectionOrTableChanged += (_, _) =>
        {
            UpdateObservation();
            OnPropertyChanged(nameof(SelectedObject));
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsInPlan));
            ChartVersion++;
        };
        _plan.Changed += (_, _) => OnPropertyChanged(nameof(IsInPlan));
        _weather.Changed += (_, _) =>
        {
            UpdateObservation();
            ChartVersion++;
        };
        _context.Recalculated += (_, _) =>
        {
            OnPropertyChanged(nameof(ChartTitle));
            ChartVersion++;
        };
    }

    public CelestialObjectRowViewModel? SelectedObject => _targets.SelectedObject;

    public bool HasSelection => SelectedObject is not null;

    /// <summary>
    /// Detail of the selected object. Calculated once per change, not on every binding read.
    /// </summary>
    [ObservableProperty]
    public partial CelestialObjectObservation? Observation { get; private set; }

    public bool IsInPlan => _plan.Contains(SelectedObject);

    /// <summary>Changes whenever the altitude chart must be redrawn.</summary>
    [ObservableProperty]
    public partial int ChartVersion { get; private set; }

    public string ChartTitle => _context.Night is not { } night
        ? Strings.ChartTitle
        : DisplayNames.Format(Strings.ChartTitleRangeFormat, night.WindowStartLocal, night.WindowEndLocal);

    /// <summary>Invalid input (observer values or maximum magnitude), or null.</summary>
    public string? InputError => _targets.InputError;

    public NightInfo? Night => _context.Night;

    public DateTime ObservationDateTime => _context.ObservationDateTime;

    public double MinimumAltitude => _context.MinimumAltitude;

    public string MoonIlluminationText => DisplayNames.MoonIllumination(_context.Moon);

    [RelayCommand]
    private void AddToPlan() => _plan.Add(SelectedObject);

    [RelayCommand]
    private void RemoveFromPlan() => _plan.Remove(SelectedObject);

    /// <summary>Logbook draft for the selected object at the chosen time.</summary>
    public LogEntryDraftViewModel? CreateLogDraft() =>
        SelectedObject is null
            ? null
            : _logbook.CreateDraft(SelectedObject.Id, SelectedObject.CommonName, _context.ObservationDateTime, _context.LocationName);

    /// <summary>
    /// Altitude of the selected object from sunset to sunrise, in the observer's local
    /// time, for the transit curve.
    /// </summary>
    public IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetNightAltitudeCurve()
    {
        if (SelectedObject is null || _context.Night is not { } night || InputError is not null)
        {
            return [];
        }

        return _astronomy.GetNightAltitudeSeries(
            SelectedObject.Target, _context.Observer, night.WindowStartLocal, night.WindowEndLocal, _context.TimeZone, CurveStep);
    }

    /// <summary>Altitude of the Moon over the same window as <see cref="GetNightAltitudeCurve"/>.</summary>
    public IReadOnlyList<(DateTime LocalTime, double AltitudeDegrees)> GetMoonAltitudeCurve()
    {
        if (_context.Night is not { } night || InputError is not null)
        {
            return [];
        }

        return _astronomy.GetMoonAltitudeSeries(
            _context.Observer, night.WindowStartLocal, night.WindowEndLocal, _context.TimeZone, CurveStep);
    }

    /// <summary>Hourly cloud cover for the chart (empty without a forecast).</summary>
    public IReadOnlyList<(DateTime StartLocal, DateTime EndLocal, int CloudCoverPercent)> GetNightCloudCover() =>
        _weather.GetNightCloudCover();

    public CelestialObjectObservation? GetObservation(CelestialObjectRowViewModel? item)
    {
        if (item is null || _context.Night is not { } night || InputError is not null)
        {
            return null;
        }

        var observer = _context.Observer;
        var events = _astronomy.GetNightEvents(
            item.Target, observer, night.WindowStartLocal, night.WindowEndLocal, _context.TimeZone, _context.MinimumAltitude);

        // Position at the exact chosen time, same as in the table.
        var utc = _context.ToUtc(_context.ObservationDateTime);
        var current = _astronomy.GetHorizontal(item.Target, observer, utc);
        var moon = _context.Moon;
        double? moonSeparation = moon is null ? null : _astronomy.GetMoonSeparationDegrees(item.Target, moon, utc);
        var moonInterferes = !item.IsPlanet
            && moon is not null && moonSeparation is not null && MoonInterference.Interferes(moon, moonSeparation.Value);

        var nightEphemeris = new NightEphemerisCalculator(_astronomy).Calculate(item.Target,
            new NightEphemerisRequest(observer, _context.TimeZone, night, _context.MinimumAltitude, _context.HorizonMask));
        var bestWindow = nightEphemeris.IsObservableInDarkness
            ? DisplayNames.Format(Strings.DetailBestWindowFormat,
                DisplayNames.Window(nightEphemeris.BestWindowStartLocal, nightEphemeris.BestWindowEndLocal),
                DisplayNames.Duration(nightEphemeris.TimeAboveMinimumInDarkness))
            : Strings.DetailBestWindowNone;
        var horizon = _context.HorizonMask;

        return new CelestialObjectObservation
        {
            Target = item.Target,
            Coordinates = current,
            IsVisible = current.AltitudeDegrees >= _context.MinimumAltitude
                        && (horizon.IsFlat || current.AltitudeDegrees >= horizon.AltitudeAt(current.AzimuthDegrees)),
            Constellation = _astronomy.GetConstellation(item.Target, utc).Name,
            BestWindowText = bestWindow,
            RiseTimeLocal = events.RiseLocal,
            VisibilityStartTimeLocal = events.ThresholdCrossingLocal,
            BestObservationTimeLocal = events.CulminationLocal,
            SetTimeLocal = events.SetLocal,
            MaximumAltitudeDegrees = events.MaximumAltitudeDegrees,
            Magnitude = _astronomy.GetApparentMagnitude(item.Target, utc),
            Planet = _astronomy.GetPlanetPosition(item.Target, utc),
            MoonSeparationDegrees = moonSeparation,
            CloudCoverAtCulmination = _weather.CloudCoverAtLocal(events.CulminationLocal),
            MoonWarning = moonInterferes
                ? DisplayNames.Format(Strings.MoonWarningFormat, moon!.IlluminatedFraction * 100, moonSeparation)
                : null
        };
    }

    private void UpdateObservation() => Observation = GetObservation(SelectedObject);
}
