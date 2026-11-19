using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
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
        CatalogIc = "IC", CatalogSolarSystem = "SolarSystem";

    private static readonly FilterOption AllConstellations = new("", Strings.FilterAllConstellations);

    private readonly ICelestialRepository _repository;
    private readonly IAstronomyService _astronomy;
    private readonly NightEphemerisCalculator _nightCalculator;
    private readonly IObserverContext _context;
    private readonly ISettingsService _settings;
    private readonly LogbookViewModel _logbook;
    private readonly IAppLogger _logger;
    private readonly List<CelestialObjectRowViewModel> _allObjects = [];
    private CancellationTokenSource? _nightCancellation;
    private NightKey? _nightKey;
    private bool _isFiltering;

    public TargetsViewModel(
        ICelestialRepository repository,
        IAstronomyService astronomy,
        IObserverContext context,
        ISettingsService settings,
        LogbookViewModel logbook,
        IAppLogger logger)
    {
        _repository = repository;
        _astronomy = astronomy;
        _nightCalculator = new NightEphemerisCalculator(astronomy);
        _context = context;
        _settings = settings;
        _logbook = logbook;
        _logger = logger;
        MaximumMagnitude = settings.Load().MaximumMagnitude;

        _context.Recalculated += (_, _) => RecalculateAndFilter();
        _logbook.Changed += RefreshObservedMarks;
    }

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
        new(CatalogSolarSystem, Strings.CatalogSolarSystem)
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

    [RelayCommand]
    private void ResetFilters()
    {
        OnlyVisible = false;
        OnlyObservableTonight = false;
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
            SelectionOrTableChanged?.Invoke(this, EventArgs.Empty);
        }
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

            if (item.IsPlanet)
            {
                item.Constellation = _astronomy.GetConstellation(item.Target, utc).Name;
            }
        }

        OnPropertyChanged(nameof(VisibleObjectsCount));
        ScheduleNightEphemerides();
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
        var filtered = _allObjects
            .Where(item =>
                (!OnlyVisible || item.IsVisible)
                && (!OnlyObservableTonight || item.IsObservableTonight)
                && (allTypes || item.Type == type)
                && IsInSelectedCatalog(item)
                && (string.IsNullOrWhiteSpace(ConstellationFilter)
                    || item.Constellation.Equals(ConstellationFilter, StringComparison.OrdinalIgnoreCase))
                && PassesMagnitude(item)
                && !(item.MajorAxisArcmin < MinimumSizeArcmin)
                && CatalogSearch.Matches(item, query))
            .OrderByDescending(item => item.AltitudeDegrees)
            .ToList();

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
        CatalogSolarSystem => item.IsPlanet,
        _ => true
    };

    public void Dispose()
    {
        _nightCancellation?.Cancel();
        _nightCancellation?.Dispose();
        _nightCancellation = null;
    }

    /// <summary>What the night ephemerides depend on.</summary>
    private sealed record NightKey(DateTime WindowStart, ObserverLocation Observer, string TimeZoneId, double MinimumAltitude, HorizonMask Horizon);
}
