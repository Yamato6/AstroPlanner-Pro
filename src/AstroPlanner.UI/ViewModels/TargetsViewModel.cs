using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Planning;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Catalog browser: loads the catalog, keeps every object's position for the chosen time and
/// its ephemeris for the night, and filters the table. Filtering never recalculates anything:
/// positions are recalculated when the time, the location, the horizon or the minimum altitude
/// change, and the night ephemerides (in the background) only when the night itself changes.
/// </summary>
public sealed partial class TargetsViewModel : ObservableValidator, IDisposable
{
    /// <summary>Values of <see cref="CatalogFilter"/>.</summary>
    public const string CatalogAll = "All", CatalogMessier = "M", CatalogCaldwell = "C", CatalogNgc = "NGC",
        CatalogIc = "IC", CatalogSolarSystem = "SolarSystem", CatalogStars = "Stars";

    private static readonly FilterOption AllConstellations = new("", Strings.FilterAllConstellations);

    private readonly ICelestialRepository _repository;
    private readonly IAstronomyService _astronomy;
    private readonly NightEphemerisCalculator _nightCalculator;
    private readonly FeasibilityCalculator _feasibilityCalculator;
    private readonly EquipmentState? _equipment;
    private readonly WeatherState? _weather;
    private CancellationTokenSource? _feasibilityCancellation;
    private FeasibilityKey? _feasibilityKey;
    private readonly IObserverContext _context;
    private readonly ISettingsService _settings;
    private readonly LogbookViewModel _logbook;
    private readonly IAppLogger _logger;
    private readonly List<CelestialObjectRowViewModel> _allObjects = [];
    private CancellationTokenSource? _nightCancellation;
    private NightKey? _nightKey;
    private bool _isFiltering;

    /// <summary>
    /// Object selected from elsewhere (the sky view) that does not pass the filters: it stays in
    /// the table until another object is selected, so that the grid and the detail can show it.
    /// </summary>
    private CelestialObjectRowViewModel? _pinned;

    public TargetsViewModel(
        ICelestialRepository repository,
        IAstronomyService astronomy,
        IObserverContext context,
        ISettingsService settings,
        LogbookViewModel logbook,
        IAppLogger logger,
        EquipmentState? equipment = null,
        WeatherState? weather = null)
    {
        _repository = repository;
        _astronomy = astronomy;
        _nightCalculator = new NightEphemerisCalculator(astronomy);
        _feasibilityCalculator = new FeasibilityCalculator(astronomy);
        _equipment = equipment;
        _weather = weather;
        _context = context;
        _settings = settings;
        _logbook = logbook;
        _logger = logger;
        MaximumMagnitude = settings.Load().MaximumMagnitude;

        _context.Recalculated += (_, _) => RecalculateAndFilter();
        _logbook.Changed += RefreshObservedMarks;
        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) => ScheduleFeasibility();
        }

        if (_weather is not null)
        {
            _weather.Changed += (_, _) => ScheduleFeasibility();
        }
    }

    /// <summary>Raised after the feasibility of every object has been recalculated (or cleared).</summary>
    public event EventHandler? FeasibilityUpdated;

    /// <summary>The background feasibility calculation (completed when there is none).</summary>
    public Task FeasibilityTask { get; private set; } = Task.CompletedTask;

    /// <summary>True while the feasibility is being calculated in the background.</summary>
    [ObservableProperty]
    public partial bool IsCalculatingFeasibility { get; private set; }

    /// <summary>Minimum verdict filter options ("" = any).</summary>
    public IReadOnlyList<FilterOption> VerdictFilters { get; } =
    [
        new("", Strings.FilterAnyVerdict),
        new(nameof(FeasibilityVerdict.Marginal), Strings.FilterMarginalOrBetter),
        new(nameof(FeasibilityVerdict.Good), Strings.FilterGoodOrBetter),
        new(nameof(FeasibilityVerdict.Excellent), Strings.FilterExcellentOnly)
    ];

    /// <summary>Hides objects whose verdict is worse than this ("" = no filter).</summary>
    [ObservableProperty]
    public partial string VerdictFilter { get; set; } = "";

    /// <summary>Only objects that fit the active rig's frame.</summary>
    [ObservableProperty]
    public partial bool OnlyFitsFov { get; set; }

    /// <summary>Raised after the catalog has been (re)loaded, before positions are calculated.</summary>
    public event EventHandler? CatalogLoaded;

    /// <summary>
    /// Raised once whenever the table or the selected object changed: after filtering, or
    /// when the user selects another object.
    /// </summary>
    public event EventHandler? SelectionOrTableChanged;

    /// <summary>Every catalog object (filtered or not).</summary>
    public IReadOnlyList<CelestialObjectRowViewModel> AllObjects => _allObjects;

    /// <summary>Objects that pass the filters, highest first.</summary>
    public BulkObservableCollection<CelestialObjectRowViewModel> Objects { get; } = [];

    /// <summary>Type filter options: the value is the internal name, the label is translated.</summary>
    public IReadOnlyList<FilterOption> TypeFilters { get; } =
    [
        new(DisplayNames.AllTypes, Strings.FilterAllTypes),
        .. Enum.GetValues<CelestialObjectType>()
            .Select(type => new FilterOption(type.ToString(), DisplayNames.Of(type)))
            .OrderBy(option => option.Label, StringComparer.CurrentCulture)
    ];

    /// <summary>Catalog filter options.</summary>
    public IReadOnlyList<FilterOption> CatalogFilters { get; } =
    [
        new(CatalogAll, Strings.CatalogAll),
        new(CatalogMessier, Strings.CatalogMessier),
        new(CatalogCaldwell, Strings.CatalogCaldwell),
        new(CatalogNgc, Strings.CatalogNgc),
        new(CatalogIc, Strings.CatalogIc),
        new(CatalogSolarSystem, Strings.CatalogSolarSystem),
        new(CatalogStars, Strings.CatalogStars)
    ];

    /// <summary>Constellations in the catalog; the first option ("" = all) clears the filter.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FilterOption> ConstellationFilters { get; private set; } = [AllConstellations];

    public int CatalogCount => _allObjects.Count;

    public int VisibleObjectsCount => _allObjects.Count(item => item.IsVisible);

    /// <summary>Hides objects fainter than this magnitude (see <see cref="PassesMagnitude"/> for unknown ones).</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-2, 20, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.MaximumMagnitudeRange))]
    public partial double MaximumMagnitude { get; set; } = 12;

    /// <summary>Hides objects smaller than this, in arcminutes (objects of unknown size are kept).</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(0, 600, ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.MinimumSizeRange))]
    public partial double MinimumSizeArcmin { get; set; }

    /// <summary>Search text: id, cross-id, name or constellation.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyVisible { get; set; }

    /// <summary>Only objects observable for some time in darkness tonight.</summary>
    [ObservableProperty]
    public partial bool OnlyObservableTonight { get; set; }

    [ObservableProperty]
    public partial string TypeFilter { get; set; } = DisplayNames.AllTypes;

    [ObservableProperty]
    public partial string CatalogFilter { get; set; } = CatalogAll;

    [ObservableProperty]
    public partial string ConstellationFilter { get; set; } = "";

    [ObservableProperty]
    public partial CelestialObjectRowViewModel? SelectedObject { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>True while the night ephemerides are being calculated in the background.</summary>
    [ObservableProperty]
    public partial bool IsCalculatingNight { get; private set; }

    /// <summary>The background calculation of the night ephemerides (completed when there is none).</summary>
    public Task NightEphemeridesTask { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Invalid-input warning; while it is set the table keeps the last valid calculation.</summary>
    [ObservableProperty]
    public partial string? ValidationMessage { get; private set; }

    /// <summary>First invalid input (observer values or table filters), evaluated now.</summary>
    public string? InputError =>
        _context.InputError
        ?? (double.IsNaN(MaximumMagnitude) || MaximumMagnitude is < -2 or > 20
            ? Strings.MaximumMagnitudeRange
            : double.IsNaN(MinimumSizeArcmin) || MinimumSizeArcmin is < 0 or > 600
                ? Strings.MinimumSizeRange
                : null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            var objects = await _repository.GetAllAsync();
            _allObjects.Clear();
            _allObjects.AddRange(objects.Select(item => new CelestialObjectRowViewModel(item)));
            if (_pendingSmallBodies is { } pending)
            {
                _allObjects.AddRange(pending.Where(b => b.IsSmallBody).Select(b => new CelestialObjectRowViewModel(b)));
            }

            _nightKey = null;

            ConstellationFilters =
            [
                AllConstellations,
                .. ConstellationLookup.All.Select(c => c.Name).Order(StringComparer.CurrentCulture).Select(c => new FilterOption(c, c))
            ];

            CatalogLoaded?.Invoke(this, EventArgs.Empty);
            RefreshObservedMarks();
            _context.Recalculate();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            _logger.LogError("Could not load the catalog.", ex);
            ErrorMessage = DisplayNames.Format(Strings.CatalogLoadFailedFormat, ex.Message);
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Replaces the comets and asteroids of the catalog (spec §4.3) without reloading the deep-sky
    /// objects. Their night ephemerides and feasibility are recalculated; a selected comet or
    /// asteroid stays selected when it is still in the list.
    /// </summary>
    public void SetSmallBodies(IReadOnlyList<CelestialObject> bodies)
    {
        ArgumentNullException.ThrowIfNull(bodies);
        if (_allObjects.Count == 0)
        {
            _pendingSmallBodies = bodies;   // added when the catalog loads
            return;
        }

        var selectedId = SelectedObject is { Target.IsSmallBody: true } selected ? selected.Id : null;
        _allObjects.RemoveAll(row => row.Target.IsSmallBody);
        _allObjects.AddRange(bodies.Where(b => b.IsSmallBody).Select(b => new CelestialObjectRowViewModel(b)));
        _pendingSmallBodies = null;
        _nightKey = null;
        _feasibilityKey = null;
        RefreshObservedMarks();
        SmallBodiesChanged?.Invoke(this, EventArgs.Empty);
        RecalculateAndFilter();
        if (selectedId is not null)
        {
            SelectById(selectedId);
        }
    }

    private IReadOnlyList<CelestialObject>? _pendingSmallBodies;

    /// <summary>Raised when every object's night ephemeris (best window, maximum altitude) was recalculated.</summary>
    public event EventHandler? NightEphemeridesUpdated;

    /// <summary>Raised when the comets and asteroids of the catalog were replaced.</summary>
    public event EventHandler? SmallBodiesChanged;

    [RelayCommand]
    private void ResetFilters()
    {
        OnlyVisible = false;
        OnlyObservableTonight = false;
        OnlyFitsFov = false;
        VerdictFilter = "";
        TypeFilter = DisplayNames.AllTypes;
        CatalogFilter = CatalogAll;
        ConstellationFilter = "";
        SearchText = "";
        MinimumSizeArcmin = 0;
        // Back to the saved settings, not to hard-coded values.
        var saved = _settings.Load();
        MaximumMagnitude = saved.MaximumMagnitude;
        _context.MinimumAltitude = saved.MinimumAltitude;
        _context.Recalculate();
    }

    /// <summary>Marks the objects that have at least one logbook entry.</summary>
    private void RefreshObservedMarks()
    {
        foreach (var item in _allObjects)
        {
            item.IsObserved = _logbook.HasBeenObserved(item.Id);
        }
    }

    // These filters do not change positions: the table is only filtered again.
    partial void OnOnlyVisibleChanged(bool value) => ApplyFilter();
    partial void OnOnlyObservableTonightChanged(bool value) => ApplyFilter();
    partial void OnOnlyFitsFovChanged(bool value) => ApplyFilter();
    partial void OnVerdictFilterChanged(string value) => ApplyFilter();
    partial void OnTypeFilterChanged(string value) => ApplyFilter();
    partial void OnCatalogFilterChanged(string value) => ApplyFilter();
    partial void OnConstellationFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnMaximumMagnitudeChanged(double value) => ApplyFilter();
    partial void OnMinimumSizeArcminChanged(double value) => ApplyFilter();

    partial void OnSelectedObjectChanged(CelestialObjectRowViewModel? value)
    {
        if (!_isFiltering)
        {
            if (_pinned is not null && !ReferenceEquals(value, _pinned))
            {
                _pinned = null;
            }

            SelectionOrTableChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Selects the object with this id (any catalog object, even one hidden by the filters,
    /// which is then kept in the table). Returns false when the id is unknown.
    /// </summary>
    /// <summary>Id of the catalog row of the star with this HYG id (the sky's star id), or null when it is not a row.</summary>
    public string? StarRowId(int hygId) => _allObjects.FirstOrDefault(item => item.Target.Star?.HygId == hygId)?.Id;

    public bool SelectById(string id)
    {
        var row = _allObjects.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return false;
        }

        if (ReferenceEquals(row, SelectedObject))
        {
            return true;
        }

        if (!Objects.Contains(row))
        {
            _pinned = row;
            ApplyFilter();
        }

        SelectedObject = row;
        return true;
    }

    /// <summary>Recalculates every position for the current observer and time, then filters again.</summary>
    private void RecalculateAndFilter()
    {
        OnPropertyChanged(nameof(CatalogCount));
        OnPropertyChanged(nameof(VisibleObjectsCount));

        // With invalid input: warn and keep the last valid calculation instead of
        // emptying the table without explanation.
        ValidationMessage = InputError;
        if (ValidationMessage is not null || _allObjects.Count == 0 || _context.Moon is not { } moon)
        {
            return;
        }

        var observer = _context.Observer;
        var horizon = _context.HorizonMask;
        var utc = _context.ToUtc(_context.ObservationDateTime);
        foreach (var item in _allObjects)
        {
            var separation = _astronomy.GetMoonSeparationDegrees(item.Target, moon, utc);
            item.UpdatePosition(
                _astronomy.GetHorizontal(item.Target, observer, utc),
                _context.MinimumAltitude,
                separation,
                // Planets are bright enough for the Moon not to hide them.
                !item.IsPlanet && MoonInterference.Interferes(moon, separation),
                _astronomy.GetApparentMagnitude(item.Target, utc),
                horizon);

            if (item.Target.IsMoving)
            {
                item.Constellation = _astronomy.GetConstellation(item.Target, utc).Name;
            }
        }

        OnPropertyChanged(nameof(VisibleObjectsCount));
        ScheduleNightEphemerides();
        ScheduleFeasibility();
        ApplyFilter();
    }

    /// <summary>
    /// Starts the background calculation of every object's night ephemeris if the night, the
    /// site, the horizon or the minimum altitude changed (not for a new time in the same night).
    /// </summary>
    private void ScheduleNightEphemerides()
    {
        if (_context.Night is not { } night)
        {
            return;
        }

        var key = new NightKey(night.WindowStartLocal, _context.Observer, _context.TimeZoneId, _context.MinimumAltitude, _context.HorizonMask);
        if (key == _nightKey)
        {
            return;
        }

        _nightKey = key;
        _nightCancellation?.Cancel();
        _nightCancellation?.Dispose();
        _nightCancellation = new CancellationTokenSource();

        var request = new NightEphemerisRequest(_context.Observer, _context.TimeZone, night, _context.MinimumAltitude, _context.HorizonMask);
        NightEphemeridesTask = CalculateNightAsync(key, request, [.. _allObjects], _nightCancellation.Token);
    }

    private async Task CalculateNightAsync(
        NightKey key,
        NightEphemerisRequest request,
        List<CelestialObjectRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        IsCalculatingNight = true;
        try
        {
            var targets = rows.Select(row => row.Target).ToList();
            var ephemerides = await Task.Run(() => _nightCalculator.Calculate(targets, request, cancellationToken), cancellationToken);

            // Back on the UI thread; a newer calculation may have started meanwhile.
            if (key != _nightKey)
            {
                return;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                rows[index].Night = ephemerides[index];
            }

            NightEphemeridesUpdated?.Invoke(this, EventArgs.Empty);

            if (OnlyObservableTonight)
            {
                ApplyFilter();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer night.
        }
        finally
        {
            if (key == _nightKey)
            {
                IsCalculatingNight = false;
            }
        }
    }

    /// <summary>
    /// Starts the background feasibility calculation (spec §8) when anything it depends on changed:
    /// night, site, horizon, minimum altitude, sky quality, darkness, rig or forecast.
    /// </summary>
    private void ScheduleFeasibility()
    {
        if (_allObjects.Count == 0 || _context.Night is not { } night || _context.InputError is not null)
        {
            return;
        }

        var rig = _equipment?.ActiveRig;
        var forecast = FeasibilityRequests.Forecast(_weather);
        var key = new FeasibilityKey(night.WindowStartLocal, _context.Observer, _context.TimeZoneId, _context.MinimumAltitude,
            _context.HorizonMask, rig, _context.EffectiveZenithSqm, _context.Darkness, _context.ExtinctionCoefficient, forecast?.FetchedAtUtc,
            _weather?.AstroForecast?.FetchedAtUtc);
        if (key == _feasibilityKey)
        {
            return;
        }

        _feasibilityKey = key;
        _feasibilityCancellation?.Cancel();
        _feasibilityCancellation?.Dispose();
        _feasibilityCancellation = new CancellationTokenSource();

        if (rig is null)
        {
            foreach (var row in _allObjects)
            {
                row.Feasibility = null;
            }

            FeasibilityTask = Task.CompletedTask;
            FeasibilityUpdated?.Invoke(this, EventArgs.Empty);
            return;
        }

        var request = FeasibilityRequests.Create(_context, rig, _weather)!;
        FeasibilityTask = CalculateFeasibilityAsync(key, request, [.. _allObjects], _feasibilityCancellation.Token);
    }

    private async Task CalculateFeasibilityAsync(
        FeasibilityKey key,
        FeasibilityRequest request,
        List<CelestialObjectRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        IsCalculatingFeasibility = true;
        try
        {
            var targets = rows.Select(row => row.Target).ToList();
            var reports = await Task.Run(() => _feasibilityCalculator.EvaluateAll(targets, request, cancellationToken), cancellationToken);
            if (key != _feasibilityKey)
            {
                return;
            }

            for (var index = 0; index < rows.Count; index++)
            {
                rows[index].Feasibility = reports[index];
            }

            if (OnlyFitsFov || VerdictFilter.Length > 0)
            {
                ApplyFilter();
            }

            FeasibilityUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request.
        }
        finally
        {
            if (key == _feasibilityKey)
            {
                IsCalculatingFeasibility = false;
            }
        }
    }

    /// <summary>Filters and sorts the table with the positions already calculated.</summary>
    private void ApplyFilter()
    {
        if (_allObjects.Count == 0)
        {
            return;
        }

        ValidationMessage = InputError;
        if (ValidationMessage is not null)
        {
            return;
        }

        var query = CatalogSearch.Parse(SearchText);
        var allTypes = TypeFilter == DisplayNames.AllTypes;
        var type = allTypes ? default : Enum.TryParse<CelestialObjectType>(TypeFilter, out var parsed) ? parsed : default;
        FeasibilityVerdict? minimumVerdict = Enum.TryParse<FeasibilityVerdict>(VerdictFilter, out var verdict) ? verdict : null;
        var filtered = _allObjects
            .Where(item =>
                (!OnlyVisible || item.IsVisible)
                && (!OnlyObservableTonight || item.IsObservableTonight)
                && (!OnlyFitsFov || item.FitsFov)
                && (minimumVerdict is null || item.Verdict >= minimumVerdict)
                && (allTypes || item.Type == type)
                && IsInSelectedCatalog(item)
                && (string.IsNullOrWhiteSpace(ConstellationFilter)
                    || item.Constellation.Equals(ConstellationFilter, StringComparison.OrdinalIgnoreCase))
                && PassesMagnitude(item)
                && !(item.MajorAxisArcmin < MinimumSizeArcmin)
                && CatalogSearch.Matches(item, query))
            .OrderByDescending(item => item.AltitudeDegrees)
            .ToList();

        if (_pinned is not null && !filtered.Contains(_pinned))
        {
            filtered.Insert(0, _pinned);
        }

        _isFiltering = true;
        try
        {
            Objects.ReplaceAll(filtered);

            SelectedObject = SelectedObject is not null && filtered.Contains(SelectedObject)
                ? SelectedObject
                : filtered.FirstOrDefault();
        }
        finally
        {
            _isFiltering = false;
        }

        SelectionOrTableChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Objects fainter than <see cref="MaximumMagnitude"/> are hidden. An unknown magnitude only
    /// passes for nebulae and associations, whose integrated magnitude is often not defined
    /// (e.g. the Horsehead); faint galaxies and stars without one are hidden.
    /// </summary>
    private bool PassesMagnitude(CelestialObjectRowViewModel item) => double.IsNaN(item.ApparentMagnitude)
        ? item.Type is CelestialObjectType.DarkNebula or CelestialObjectType.EmissionNebula or CelestialObjectType.HiiRegion
            or CelestialObjectType.ReflectionNebula or CelestialObjectType.DiffuseNebula or CelestialObjectType.SupernovaRemnant
            or CelestialObjectType.ClusterWithNebula or CelestialObjectType.StellarAssociation or CelestialObjectType.PlanetaryNebula
        : item.ApparentMagnitude <= MaximumMagnitude;

    private bool IsInSelectedCatalog(CelestialObjectRowViewModel item) => CatalogFilter switch
    {
        CatalogMessier => item.Target.MessierNumber is not null,
        CatalogCaldwell => item.Target.CaldwellNumber is not null,
        CatalogNgc => item.Target.IsInCatalog("NGC"),
        CatalogIc => item.Target.IsInCatalog("IC"),
        CatalogSolarSystem => item.Target.IsMoving,
        CatalogStars => item.Target.IsStar,
        _ => true
    };

    public void Dispose()
    {
        _nightCancellation?.Cancel();
        _nightCancellation?.Dispose();
        _nightCancellation = null;
        _feasibilityCancellation?.Cancel();
        _feasibilityCancellation?.Dispose();
        _feasibilityCancellation = null;
    }

    /// <summary>What the night ephemerides depend on.</summary>
    private sealed record NightKey(DateTime WindowStart, ObserverLocation Observer, string TimeZoneId, double MinimumAltitude, HorizonMask Horizon);

    /// <summary>What the feasibility depends on.</summary>
    private sealed record FeasibilityKey(
        DateTime WindowStart, ObserverLocation Observer, string TimeZoneId, double MinimumAltitude, HorizonMask Horizon,
        RigSetup? Rig, double ZenithSqm, DarknessDefinition Darkness, double Extinction, DateTime? ForecastFetchedUtc,
        DateTime? AstroForecastFetchedUtc);
}
