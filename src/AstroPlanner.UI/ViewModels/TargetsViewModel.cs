using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Catalog browser: loads the catalog, keeps every object's position for the chosen time
/// and filters the table. Filtering never recalculates positions: positions are only
/// recalculated when the time, the location or the minimum altitude change.
/// </summary>
public sealed partial class TargetsViewModel : ObservableValidator
{
    private static readonly FilterOption AllConstellations = new("", "Todas");

    private readonly ICelestialRepository _repository;
    private readonly IAstronomyService _astronomy;
    private readonly IObserverContext _context;
    private readonly ISettingsService _settings;
    private readonly LogbookViewModel _logbook;
    private readonly IAppLogger _logger;
    private readonly List<CelestialObjectRowViewModel> _allObjects = [];
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
    public ObservableCollection<CelestialObjectRowViewModel> Objects { get; } = [];

    /// <summary>Type filter options: the value is the internal name, the label is translated.</summary>
    public IReadOnlyList<FilterOption> TypeFilters { get; } =
    [
        new(DisplayNames.AllTypes, "Todos"),
        .. Enum.GetValues<CelestialObjectType>().Select(type => new FilterOption(type.ToString(), DisplayNames.Of(type)))
    ];

    /// <summary>Constellations in the catalog; the first option ("" = all) clears the filter.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<FilterOption> ConstellationFilters { get; private set; } = [AllConstellations];

    public int CatalogCount => _allObjects.Count;

    public int VisibleObjectsCount => _allObjects.Count(item => item.IsVisible);

    /// <summary>Hides objects fainter than this magnitude.</summary>
    [ObservableProperty, NotifyDataErrorInfo]
    [Range(-2, 20, ErrorMessage = "La magnitud máxima debe estar entre -2 y 20.")]
    public partial double MaximumMagnitude { get; set; } = 12;

    /// <summary>Search text: id, name or constellation.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial bool OnlyVisible { get; set; }

    [ObservableProperty]
    public partial string TypeFilter { get; set; } = DisplayNames.AllTypes;

    [ObservableProperty]
    public partial string ConstellationFilter { get; set; } = "";

    [ObservableProperty]
    public partial CelestialObjectRowViewModel? SelectedObject { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Invalid-input warning; while it is set the table keeps the last valid calculation.</summary>
    [ObservableProperty]
    public partial string? ValidationMessage { get; private set; }

    /// <summary>First invalid input (observer values or maximum magnitude), evaluated now.</summary>
    public string? InputError =>
        _context.InputError
        ?? (double.IsNaN(MaximumMagnitude) || MaximumMagnitude is < -2 or > 20
            ? "La magnitud máxima debe estar entre -2 y 20."
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

            ConstellationFilters =
            [
                AllConstellations,
                .. _allObjects.Select(o => o.Constellation).Distinct().Order().Select(c => new FilterOption(c, c))
            ];

            CatalogLoaded?.Invoke(this, EventArgs.Empty);
            RefreshObservedMarks();
            _context.Recalculate();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            _logger.LogError("No se pudo cargar el catálogo Messier.", ex);
            ErrorMessage = $"No se pudo cargar el catálogo: {ex.Message}";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ResetFilters()
    {
        OnlyVisible = false;
        TypeFilter = DisplayNames.AllTypes;
        ConstellationFilter = "";
        SearchText = "";
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
    partial void OnTypeFilterChanged(string value) => ApplyFilter();
    partial void OnConstellationFilterChanged(string value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnMaximumMagnitudeChanged(double value) => ApplyFilter();

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
                _astronomy.GetApparentMagnitude(item.Target, utc));
        }

        OnPropertyChanged(nameof(VisibleObjectsCount));
        ApplyFilter();
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

        var filtered = _allObjects
            .Where(item =>
                (!OnlyVisible || item.IsVisible)
                && (TypeFilter == DisplayNames.AllTypes || item.Type.ToString() == TypeFilter)
                && (string.IsNullOrWhiteSpace(ConstellationFilter)
                    || item.Constellation.Equals(ConstellationFilter, StringComparison.OrdinalIgnoreCase))
                && item.ApparentMagnitude <= MaximumMagnitude
                && CatalogSearch.Matches(item, SearchText))
            .OrderByDescending(item => item.AltitudeDegrees);

        _isFiltering = true;
        try
        {
            Objects.Clear();
            foreach (var item in filtered)
            {
                Objects.Add(item);
            }

            SelectedObject = SelectedObject is not null && Objects.Contains(SelectedObject)
                ? SelectedObject
                : Objects.FirstOrDefault();
        }
        finally
        {
            _isFiltering = false;
        }

        SelectionOrTableChanged?.Invoke(this, EventArgs.Empty);
    }
}
