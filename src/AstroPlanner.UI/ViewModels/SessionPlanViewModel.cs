using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// "My plan" and tonight's session (spec §4.6): the objects picked for the night in priority order,
/// scheduled across the night with the active rig (<see cref="SessionScheduler"/>), with the
/// recommended subs, a timeline, a checklist and the user's notes. Persisted between sessions.
/// </summary>
public sealed partial class SessionPlanViewModel : ObservableObject
{
    private readonly IObservationPlanService _planService;
    private readonly IAstronomyService _astronomy;
    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly TargetsViewModel _targets;
    private readonly IAppLogger _logger;
    private readonly EquipmentState? _equipment;
    private readonly TimeProvider _time;
    private readonly FeasibilityCalculator _calculator;

    /// <summary>Usable samples per object id in the last schedule (for <see cref="AutoPrioritizeCommand"/>).</summary>
    private readonly Dictionary<string, int> _usableSamples = new(StringComparer.Ordinal);

    private readonly SatelliteState? _satellites;
    private int _crossingsVersion;

    /// <summary>The background search for satellite crossings of the planned blocks (tests await it).</summary>
    public Task CrossingsTask { get; private set; } = Task.CompletedTask;

    /// <summary>True while values are set by code, so that the change callbacks do not save or reschedule.</summary>
    private bool _updating;

    public SessionPlanViewModel(
        IObservationPlanService planService,
        IAstronomyService astronomy,
        IObserverContext context,
        WeatherState weather,
        TargetsViewModel targets,
        IAppLogger logger,
        EquipmentState? equipment = null,
        TimeProvider? time = null,
        SatelliteState? satellites = null)
    {
        _satellites = satellites;
        if (_satellites is not null)
        {
            _satellites.Changed += (_, _) => UpdateCrossings();
        }

        _planService = planService;
        _astronomy = astronomy;
        _context = context;
        _weather = weather;
        _targets = targets;
        _logger = logger;
        _equipment = equipment;
        _time = time ?? TimeProvider.System;
        _calculator = new FeasibilityCalculator(astronomy);

        Checklist = [.. ChecklistKeys.Select(key => new ChecklistItemViewModel(key, ChecklistText(key), OnChecklistChanged))];

        _targets.CatalogLoaded += (_, _) => Restore();
        _context.Recalculated += (_, _) => Refresh();
        _weather.Changed += (_, _) => Refresh();
        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) => Reschedule();
        }
    }

    /// <summary>Raised when an object is added or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after every reschedule (the view redraws the timeline).</summary>
    public event EventHandler? TimelineChanged;

    /// <summary>The plan in priority order (1 = scheduled first).</summary>
    public ObservableCollection<PlanItemViewModel> Items { get; } = [];

    /// <summary>Spec §4.6 reminders; ticks are saved until reset.</summary>
    public IReadOnlyList<ChecklistItemViewModel> Checklist { get; }

    /// <summary>The scheduled night for the timeline; null without a rig or a night.</summary>
    [ObservableProperty]
    public partial PlanTimeline? Timeline { get; private set; }

    /// <summary>"Dark time 9h 50m · scheduled 9h 20m · free 0h 30m", or why nothing is scheduled.</summary>
    [ObservableProperty]
    public partial string ScheduleSummary { get; private set; } = "";

    /// <summary>Result of the last export or save.</summary>
    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    public bool Contains(CelestialObjectRowViewModel? row) =>
        row is not null && Items.Any(item => item.Row == row);

    /// <summary>Adds the object (lowest priority) if it is not in the plan yet.</summary>
    public void Add(CelestialObjectRowViewModel? row)
    {
        if (row is null || Contains(row))
        {
            return;
        }

        Items.Add(CreateItem(row, ""));
        Renumber();
        UpdateCulminations();
        Reschedule();
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
            Renumber();
            Reschedule();
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raises the priority of <paramref name="item"/> by one.</summary>
    [RelayCommand]
    private void MoveUp(PlanItemViewModel? item) => Move(item, -1);

    /// <summary>Lowers the priority of <paramref name="item"/> by one.</summary>
    [RelayCommand]
    private void MoveDown(PlanItemViewModel? item) => Move(item, +1);

    private void Move(PlanItemViewModel? item, int delta)
    {
        var index = item is null ? -1 : Items.IndexOf(item);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
        Renumber();
        Reschedule();
        Save();
    }

    /// <summary>
    /// Orders the priorities so that every target fits: the ones visible for the shortest time
    /// first, then by feasibility score. Locked blocks stay where they are.
    /// </summary>
    [RelayCommand]
    private void AutoPrioritize()
    {
        var ordered = Items
            .OrderBy(item => _usableSamples.TryGetValue(item.Id, out var samples) && samples > 0 ? samples : int.MaxValue)
            .ThenByDescending(item => item.Feasibility?.Score ?? -1)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            var current = Items.IndexOf(ordered[index]);
            if (current != index)
            {
                Items.Move(current, index);
            }
        }

        Renumber();
        Reschedule();
        Save();
    }

    [RelayCommand]
    private void ResetChecklist()
    {
        _updating = true;
        foreach (var item in Checklist)
        {
            item.IsDone = false;
        }

        _updating = false;
        SaveChecklist();
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
            if (_equipment?.ActiveRig is { } rig)
            {
                parts.Add(Format(Strings.PlanSummaryRigFormat, rig.Rig.Name));
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The plan in observing order: scheduled blocks by start time, then the rest by priority.</summary>
    public IReadOnlyList<PlanItemViewModel> ChronologicalItems =>
        [.. Items.OrderBy(item => item.StartLocal ?? DateTime.MaxValue).ThenBy(item => item.Priority)];

    /// <summary>CSV content of the plan (see <see cref="PlanExport"/>).</summary>
    public string BuildCsv(CultureInfo culture) => PlanExport.BuildCsv(ChronologicalItems, culture);

    /// <summary>File name offered when the plan is exported: "AstroPlannerPro-plan-2026-01-15.pdf" (the observing date).</summary>
    public string SuggestedFileName(string extension) =>
        string.Create(CultureInfo.InvariantCulture, $"AstroPlannerPro-plan-{_context.ObservationDateTime:yyyy-MM-dd}.{extension}");

    /// <summary>iCalendar content of the scheduled blocks (see <see cref="PlanCalendarExport"/>).</summary>
    public string BuildIcs() =>
        PlanCalendarExport.BuildIcs(ChronologicalItems, _context.ToUtc, _time.GetUtcNow().UtcDateTime, _context.LocationName);

    /// <summary>Exports the plan to CSV (UTF-8 with BOM so that Excel keeps accents).</summary>
    public bool Export(string filePath) =>
        WriteFile(filePath, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(true));
            writer.Write(BuildCsv(CultureInfo.CurrentCulture));
        });

    /// <summary>Exports the scheduled blocks to an .ics calendar (spec §4.6 [P1]).</summary>
    public bool ExportIcs(string filePath) =>
        WriteFile(filePath, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(BuildIcs());
        });

    /// <summary>Exports a printable PDF: summary, timeline, table and checklist.</summary>
    public bool ExportPdf(string filePath) =>
        WriteFile(filePath, stream => PlanPdfExport.Write(stream, new PlanPdfContent(
            Strings.PlanPrintTitle, SummaryText, ScheduleSummary, Timeline, ChronologicalItems,
            [.. Checklist.Select(c => (c.Text, c.IsDone))])));

    private bool WriteFile(string filePath, Action<Stream> write)
    {
        try
        {
            using (var stream = File.Create(filePath))
            {
                write(stream);
            }

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

    private PlanItemViewModel CreateItem(CelestialObjectRowViewModel row, string note) =>
        new(row, note, OnNoteChanged, OnLockChanged);

    private void OnNoteChanged(PlanItemViewModel item) => Save();

    /// <summary>Locking pins the current block; unlocking lets the scheduler move it again.</summary>
    private void OnLockChanged(PlanItemViewModel item)
    {
        if (_updating)
        {
            return;
        }

        if (item.IsLocked)
        {
            if (!item.HasBlock)
            {
                _updating = true;
                item.IsLocked = false;
                _updating = false;
                return;
            }

            item.LockedStartLocal = item.StartLocal;
            item.LockedEndLocal = item.EndLocal;
        }
        else
        {
            item.LockedStartLocal = null;
            item.LockedEndLocal = null;
        }

        Reschedule();
        Save();
    }

    /// <summary>Rebuilds the saved plan from catalog ids.</summary>
    private void Restore()
    {
        _updating = true;
        Items.Clear();
        foreach (var entry in _planService.Load())
        {
            var row = _targets.AllObjects.FirstOrDefault(o => o.Id == entry.Id);
            if (row is not null && Items.All(item => item.Row != row))
            {
                var item = CreateItem(row, entry.Note);
                item.IsLocked = entry.Locked && entry.StartLocal is not null && entry.EndLocal is not null;
                item.LockedStartLocal = item.IsLocked ? entry.StartLocal : null;
                item.LockedEndLocal = item.IsLocked ? entry.EndLocal : null;
                Items.Add(item);
            }
        }

        foreach (var key in _planService.LoadChecklist())
        {
            if (Checklist.FirstOrDefault(c => c.Key == key) is { } done)
            {
                done.IsDone = true;
            }
        }

        _updating = false;
        Renumber();
        Refresh();
    }

    private void Save()
    {
        try
        {
            _planService.Save(Items.Select(item => new ObservationPlanEntry(item.Id, item.Note)
            {
                Locked = item.IsLocked,
                StartLocal = item.LockedStartLocal,
                EndLocal = item.LockedEndLocal
            }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the observation plan.", ex);
            StatusMessage = Format(Strings.PlanSaveFailedFormat, ex.Message);
        }
    }

    private void OnChecklistChanged(ChecklistItemViewModel item)
    {
        if (!_updating)
        {
            SaveChecklist();
        }
    }

    private void SaveChecklist()
    {
        try
        {
            _planService.SaveChecklist(Checklist.Where(c => c.IsDone).Select(c => c.Key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the checklist.", ex);
            StatusMessage = Format(Strings.PlanSaveFailedFormat, ex.Message);
        }
    }

    private void Renumber()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Priority = index + 1;
        }
    }

    private void Refresh()
    {
        UpdateCulminations();
        Reschedule();
    }

    /// <summary>Each object's culmination in the current night (no rig needed).</summary>
    private void UpdateCulminations()
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
    }

    /// <summary>Schedules the night with the active rig (spec §4.6) and rebuilds the timeline.</summary>
    private void Reschedule()
    {
        _usableSamples.Clear();
        var rig = _equipment?.ActiveRig;
        var request = rig is null || _targets.InputError is not null ? null : FeasibilityRequests.Create(_context, rig, _weather);
        if (request is null || Items.Count == 0)
        {
            foreach (var item in Items)
            {
                item.Feasibility = null;
                item.Schedule = null;
            }

            Timeline = null;
            ScheduleSummary = Items.Count == 0 ? "" : rig is null ? Strings.PlanScheduleNoRig : "";
            TimelineChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var night = FeasibilityCalculator.CreateSamples(request);
        var windowStart = ToLocal(night.TimesUtc[0]);
        var windowEnd = ToLocal(night.TimesUtc[^1].AddSeconds(night.DurationsSeconds[^1]));
        var unlocked = false;
        _updating = true;
        foreach (var item in Items.Where(i => i.IsLocked))
        {
            // A block locked on another night means nothing tonight.
            if (item.LockedStartLocal is not { } start || item.LockedEndLocal is not { } end || end <= windowStart || start >= windowEnd)
            {
                item.IsLocked = false;
                item.LockedStartLocal = null;
                item.LockedEndLocal = null;
                unlocked = true;
            }
        }

        _updating = false;

        var targets = new List<SessionTarget>(Items.Count);
        var profiles = new List<TargetNightProfile>(Items.Count);
        foreach (var item in Items)
        {
            var profile = _calculator.Profile(item.Row.Target, request, night);
            var report = _calculator.Evaluate(item.Row.Target, request, night);
            profiles.Add(profile);
            _usableSamples[item.Id] = profile.Usable.Count(u => u);
            targets.Add(new SessionTarget(item.Id, profile, report)
            {
                Locked = item.IsLocked,
                LockedStartLocal = item.LockedStartLocal,
                LockedEndLocal = item.LockedEndLocal
            });
        }

        var schedule = SessionScheduler.Schedule(targets, night, request);
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].Feasibility = targets[index].Report;
            Items[index].Schedule = schedule.Targets[index];
        }

        Timeline = PlanTimeline.Create(night, request.TimeZone, Items, profiles, _weather.GetNightCloudCover());
        var scheduled = TimeSpan.FromHours(schedule.DarkHours - schedule.IdleDarkHours);
        ScheduleSummary = Format(Strings.PlanScheduleSummaryFormat, Duration(TimeSpan.FromHours(schedule.DarkHours)),
            Duration(scheduled), Duration(TimeSpan.FromHours(schedule.IdleDarkHours)));
        if (unlocked)
        {
            Save();
        }

        TimelineChanged?.Invoke(this, EventArgs.Empty);
        UpdateCrossings();

        DateTime ToLocal(DateTime utc) => TimeZoneConversion.ToLocal(utc, request.TimeZone);
    }

    /// <summary>
    /// Looks in the background for sunlit satellites crossing the active rig's field (north up) during
    /// each scheduled block, then marks them on the items and the timeline.
    /// </summary>
    private void UpdateCrossings()
    {
        var version = ++_crossingsVersion;
        var rig = _equipment?.ActiveRig;
        if (_satellites is not { IsEnabled: true, Satellites.Count: > 0 } satellites || rig is null || Timeline is null)
        {
            foreach (var item in Items.Where(i => i.Crossings.Count > 0))
            {
                item.Crossings = [];
            }

            CrossingsTask = Task.CompletedTask;
            return;
        }

        var jobs = new List<(PlanItemViewModel Item, CameraField Field, DateTime StartUtc, DateTime EndUtc)>();
        foreach (var item in Items)
        {
            if (item.Schedule is { StartLocal: { } start, EndLocal: { } end } && end > start)
            {
                var startUtc = _context.ToUtc(start);
                var endUtc = _context.ToUtc(end);
                if (CenterJ2000(item.Row.Target, startUtc + ((endUtc - startUtc) / 2)) is { } center)
                {
                    jobs.Add((item, new CameraField(center, rig.FieldOfView.WidthDegrees, rig.FieldOfView.HeightDegrees), startUtc, endUtc));
                }
            }
        }

        CrossingsTask = ComputeCrossingsAsync(jobs, satellites.Satellites, _context.Observer, _context.TimeZone, version);
    }

    private EquatorialCoordinates? CenterJ2000(CelestialObject target, DateTime utc)
    {
        if (target.Orbit is not null)
        {
            return _astronomy.GetSmallBodyPosition(target, utc)?.AstrometricJ2000;
        }

        if (target.Planet is not null)
        {
            return _astronomy.GetPlanetPosition(target, utc) is { } planet
                ? Precession.Precess(planet.Equatorial, AstronomyMath.ToJulianDate(utc), J2000JulianDate)
                : null;
        }

        return target.Coordinates;
    }

    private const double J2000JulianDate = 2451545.0;

    private async Task ComputeCrossingsAsync(
        List<(PlanItemViewModel Item, CameraField Field, DateTime StartUtc, DateTime EndUtc)> jobs,
        IReadOnlyList<TrackedSatellite> satellites, ObserverLocation observer, TimeZoneInfo zone, int version)
    {
        try
        {
            var results = await Task.Run(() => jobs
                .Select(job => FieldCrossings.Find(satellites, observer, job.Field, job.StartUtc, job.EndUtc))
                .ToList());
            if (version != _crossingsVersion)
            {
                return;
            }

            var computed = new HashSet<PlanItemViewModel>();
            for (var index = 0; index < jobs.Count; index++)
            {
                jobs[index].Item.Crossings = [.. results[index].Select(c => new PlanCrossing(c.Name,
                    TimeZoneConversion.ToLocal(c.EnterUtc, zone), TimeZoneConversion.ToLocal(c.ExitUtc, zone), c.Magnitude))];
                computed.Add(jobs[index].Item);
            }

            foreach (var item in Items.Where(i => !computed.Contains(i) && i.Crossings.Count > 0))
            {
                item.Crossings = [];
            }

            if (Timeline is { } timeline)
            {
                Timeline = timeline with
                {
                    Rows = [.. timeline.Rows.Select(row => Items.FirstOrDefault(i => i.Id == row.Label) is { Crossings.Count: > 0 } item
                        ? row with { CrossingsLocal = [.. item.Crossings.Select(c => c.EnterLocal)] }
                        : row with { CrossingsLocal = null })]
                };
                TimelineChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not look for satellite crossings of the planned blocks.", ex);
        }
    }

    /// <summary>Keys of the checklist, in display order (saved by key).</summary>
    public static IReadOnlyList<string> ChecklistKeys { get; } =
        ["Batteries", "DewHeater", "Storage", "PolarAlignment", "Focus", "Flats", "Darks", "Bias"];

    private static string ChecklistText(string key) => key switch
    {
        "Batteries" => Strings.ChecklistBatteries,
        "DewHeater" => Strings.ChecklistDewHeater,
        "Storage" => Strings.ChecklistStorage,
        "PolarAlignment" => Strings.ChecklistPolarAlignment,
        "Focus" => Strings.ChecklistFocus,
        "Flats" => Strings.ChecklistFlats,
        "Darks" => Strings.ChecklistDarks,
        _ => Strings.ChecklistBias
    };
}

/// <summary>One reminder of the session checklist.</summary>
public sealed partial class ChecklistItemViewModel(string key, string text, Action<ChecklistItemViewModel> onChanged) : ObservableObject
{
    public string Key { get; } = key;

    public string Text { get; } = text;

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    partial void OnIsDoneChanged(bool value) => onChanged(this);
}
