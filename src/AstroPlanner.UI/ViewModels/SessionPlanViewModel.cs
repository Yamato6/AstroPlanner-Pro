using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// "My plan": the objects picked for the night, ordered by culmination time (the order
/// in which it is best to observe them), with the user's notes. Persisted between sessions.
/// </summary>
public sealed partial class SessionPlanViewModel : ObservableObject
{
    private readonly IObservationPlanService _planService;
    private readonly IAstronomyService _astronomy;
    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly TargetsViewModel _targets;
    private readonly IAppLogger _logger;

    public SessionPlanViewModel(
        IObservationPlanService planService,
        IAstronomyService astronomy,
        IObserverContext context,
        WeatherState weather,
        TargetsViewModel targets,
        IAppLogger logger)
    {
        _planService = planService;
        _astronomy = astronomy;
        _context = context;
        _weather = weather;
        _targets = targets;
        _logger = logger;

        _targets.CatalogLoaded += (_, _) => Restore();
        _context.Recalculated += (_, _) => UpdateSchedule();
        _weather.Changed += (_, _) => UpdateClouds();
    }

    /// <summary>Raised when an object is added or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>The plan, ordered by culmination time.</summary>
    public ObservableCollection<PlanItemViewModel> Items { get; } = [];

    /// <summary>Result of the last export or save.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    public bool Contains(CelestialObjectRowViewModel? row) =>
        row is not null && Items.Any(item => item.Row == row);

    /// <summary>Adds the object if it is not in the plan yet.</summary>
    public void Add(CelestialObjectRowViewModel? row)
    {
        if (row is null || Contains(row))
        {
            return;
        }

        Items.Add(new PlanItemViewModel(row, "", OnNoteChanged));
        UpdateSchedule();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the plan entry of <paramref name="row"/>, if any.</summary>
    public void Remove(CelestialObjectRowViewModel? row) =>
        RemoveCommand.Execute(Items.FirstOrDefault(item => item.Row == row));

    [RelayCommand]
    private void Remove(PlanItemViewModel? item)
    {
        if (item is not null && Items.Remove(item))
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Summary of the night for the printed plan header.</summary>
    public string SummaryText
    {
        get
        {
            var nightStart = ObservationNight.GetNightStartLocal(_context.ObservationDateTime);
            var parts = new List<string>
            {
                Format(Strings.PlanSummaryNightFormat, nightStart, nightStart.AddDays(1)),
                _context.LocationName
            };
            if (_context.Night is { HasAstronomicalDarkness: true } night)
            {
                parts.Add(Format(Strings.PlanSummaryDarkNightFormat, night.AstronomicalDuskLocal, night.AstronomicalDawnLocal));
            }

            if (_context.Moon is { } moon)
            {
                parts.Add(Format(Strings.PlanSummaryMoonFormat, MoonIllumination(moon), MoonPhase(moon.IlluminatedFraction, moon.IsWaxing)));
            }

            parts.Add(Format(Strings.PlanSummaryMinimumAltitudeFormat, _context.MinimumAltitude));
            return string.Join(" · ", parts);
        }
    }

    /// <summary>CSV content of the plan (see <see cref="PlanExport"/>).</summary>
    public string BuildCsv(CultureInfo culture) => PlanExport.BuildCsv(Items, culture);

    /// <summary>Exports the plan to CSV (UTF-8 with BOM so that Excel keeps accents).</summary>
    public bool Export(string filePath)
    {
        try
        {
            File.WriteAllText(filePath, BuildCsv(CultureInfo.CurrentCulture), new UTF8Encoding(true));
            StatusMessage = Format(Strings.PlanExportedFormat, Path.GetFileName(filePath));
            _logger.LogInfo($"Plan exported to {filePath}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not export the plan.", ex);
            StatusMessage = Format(Strings.PlanExportFailedFormat, ex.Message);
            return false;
        }
    }

    private void OnNoteChanged(PlanItemViewModel item) => Save();

    /// <summary>Rebuilds the saved plan from catalog ids.</summary>
    private void Restore()
    {
        Items.Clear();
        foreach (var entry in _planService.Load())
        {
            var row = _targets.AllObjects.FirstOrDefault(o => o.Id == entry.Id);
            if (row is not null && Items.All(item => item.Row != row))
            {
                Items.Add(new PlanItemViewModel(row, entry.Note, OnNoteChanged));
            }
        }
    }

    private void Save()
    {
        try
        {
            _planService.Save(Items.Select(item => new ObservationPlanEntry(item.Id, item.Note)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the observation plan.", ex);
            StatusMessage = Format(Strings.PlanSaveFailedFormat, ex.Message);
        }
    }

    /// <summary>
    /// Calculates each object's culmination in the current night and orders the plan by it,
    /// which is the order in which it is best to observe.
    /// </summary>
    private void UpdateSchedule()
    {
        if (_context.Night is not { } night || _targets.InputError is not null || Items.Count == 0)
        {
            return;
        }

        var observer = _context.Observer;
        foreach (var item in Items)
        {
            var events = _astronomy.GetNightEvents(
                item.Row.Target,
                observer,
                night.WindowStartLocal,
                night.WindowEndLocal,
                _context.TimeZone,
                _context.MinimumAltitude);
            item.CulminationLocal = events.CulminationLocal;
            item.MaximumAltitudeDegrees = events.MaximumAltitudeDegrees;
            item.CloudCoverAtCulmination = _weather.CloudCoverAtLocal(events.CulminationLocal);
        }

        var ordered = Items.OrderBy(item => item.CulminationLocal).ThenBy(item => item.Id, StringComparer.Ordinal).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = Items.IndexOf(ordered[index]);
            if (current != index)
            {
                Items.Move(current, index);
            }
        }
    }

    private void UpdateClouds()
    {
        foreach (var item in Items)
        {
            item.CloudCoverAtCulmination = _weather.CloudCoverAtLocal(item.CulminationLocal);
        }
    }
}
