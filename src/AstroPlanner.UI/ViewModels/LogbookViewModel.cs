using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Observation logbook: logged entries, objects already observed and Messier marathon and
/// Caldwell progress.
/// </summary>
public sealed partial class LogbookViewModel : ObservableObject
{
    public const int MessierTotal = 110;
    public const int CaldwellTotal = 109;

    private readonly IObservationLogService _service;
    private readonly IAppLogger _logger;
    private HashSet<string> _observedIds = [];
    private HashSet<string> _caldwellIds = [];

    public LogbookViewModel(IObservationLogService service, IAppLogger logger)
    {
        _service = service;
        _logger = logger;
        foreach (var entry in _service.Load().OrderByDescending(e => e.ObservedAtLocal))
        {
            Entries.Add(entry);
        }

        RefreshSummary();
    }

    /// <summary>Raised when entries are added or deleted.</summary>
    public event Action? Changed;

    /// <summary>Entries, newest first.</summary>
    public ObservableCollection<ObservationLogEntry> Entries { get; } = [];

    public IReadOnlySet<string> ObservedIds => _observedIds;

    [ObservableProperty]
    public partial int ObservedMessierCount { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    public string MessierProgressText => string.Create(CultureInfo.CurrentCulture, $"{ObservedMessierCount} / {MessierTotal}");

    public double MessierProgressPercent => 100.0 * ObservedMessierCount / MessierTotal;

    [ObservableProperty]
    public partial int ObservedCaldwellCount { get; private set; }

    public string CaldwellProgressText => string.Create(CultureInfo.CurrentCulture, $"{ObservedCaldwellCount} / {CaldwellTotal}");

    public double CaldwellProgressPercent => 100.0 * ObservedCaldwellCount / CaldwellTotal;

    /// <summary>
    /// Tells the logbook which catalog objects are Caldwell objects (their ids are NGC/IC
    /// numbers, so the id alone does not say it).
    /// </summary>
    public void UseCatalog(IEnumerable<CelestialObject> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _caldwellIds = catalog.Where(o => o.CaldwellNumber is not null).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        RefreshSummary();
    }

    [GeneratedRegex(@"^M\d+$")]
    private static partial Regex MessierId();

    public bool HasBeenObserved(string objectId) => _observedIds.Contains(objectId);

    /// <summary>Draft of a new entry for <paramref name="objectId"/>, with the equipment of the last one.</summary>
    public LogEntryDraftViewModel CreateDraft(string objectId, string objectName, DateTime observedAtLocal, string location) =>
        new(objectId, objectName, observedAtLocal, location, Entries.FirstOrDefault()?.Equipment ?? "");

    /// <summary>Saves the draft as an entry; false if it is invalid or could not be saved.</summary>
    public bool Add(LogEntryDraftViewModel draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        draft.Validate();
        if (draft.HasErrors || draft.ObservedAtLocal is not { } observedAt)
        {
            return false;
        }

        var entry = new ObservationLogEntry(
            Guid.NewGuid(),
            draft.ObjectId,
            draft.ObjectName,
            observedAt,
            draft.Location.Trim(),
            draft.Equipment.Trim(),
            draft.Seeing,
            draft.Transparency,
            draft.Rating,
            draft.Notes.Trim());

        // Keep the newest-to-oldest order.
        var index = 0;
        while (index < Entries.Count && Entries[index].ObservedAtLocal > entry.ObservedAtLocal)
        {
            index++;
        }

        Entries.Insert(index, entry);
        if (!Save())
        {
            Entries.Remove(entry);
            return false;
        }

        StatusMessage = Format(Strings.LogbookAddedFormat, entry.ObjectId);
        RefreshSummary();
        return true;
    }

    [RelayCommand]
    private void Delete(ObservationLogEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        var index = Entries.IndexOf(entry);
        if (index < 0)
        {
            return;
        }

        Entries.RemoveAt(index);
        if (!Save())
        {
            Entries.Insert(index, entry);
            return;
        }

        StatusMessage = Format(Strings.LogbookDeletedFormat, entry.ObjectId);
        RefreshSummary();
    }

    public string BuildCsv(CultureInfo culture)
    {
        var csv = new CsvBuilder(culture).AddRow([Strings.CsvDate, Strings.CsvTime, Strings.CsvId, Strings.CsvName,
            Strings.CsvLocation, Strings.CsvEquipment, Strings.CsvSeeing, Strings.CsvTransparency, Strings.CsvRating,
            Strings.CsvNotes]);
        foreach (var entry in Entries.OrderBy(e => e.ObservedAtLocal))
        {
            csv.AddRow([
                entry.ObservedAtLocal.ToString("yyyy-MM-dd", culture),
                entry.ObservedAtLocal.ToString("HH:mm", culture),
                entry.ObjectId,
                entry.ObjectName,
                entry.Location,
                entry.Equipment,
                entry.Seeing.ToString(culture),
                entry.Transparency.ToString(culture),
                entry.Rating.ToString(culture),
                entry.Notes
            ]);
        }

        return csv.ToString();
    }

    /// <summary>Exports the logbook to CSV (UTF-8 with BOM so that Excel keeps accents).</summary>
    public bool Export(string filePath)
    {
        try
        {
            File.WriteAllText(filePath, BuildCsv(CultureInfo.CurrentCulture), new UTF8Encoding(true));
            StatusMessage = Format(Strings.LogbookExportedFormat, Path.GetFileName(filePath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not export the logbook.", ex);
            StatusMessage = Format(Strings.LogbookExportFailedFormat, ex.Message);
            return false;
        }
    }

    private bool Save()
    {
        try
        {
            _service.Save(Entries);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the logbook.", ex);
            StatusMessage = Format(Strings.LogbookSaveFailedFormat, ex.Message);
            return false;
        }
    }

    private void RefreshSummary()
    {
        _observedIds = Entries.Select(e => e.ObjectId).ToHashSet(StringComparer.Ordinal);
        ObservedMessierCount = _observedIds.Count(id => MessierId().IsMatch(id));
        ObservedCaldwellCount = _observedIds.Count(_caldwellIds.Contains);
        OnPropertyChanged(nameof(ObservedIds));
        OnPropertyChanged(nameof(MessierProgressText));
        OnPropertyChanged(nameof(MessierProgressPercent));
        OnPropertyChanged(nameof(CaldwellProgressText));
        OnPropertyChanged(nameof(CaldwellProgressPercent));
        Changed?.Invoke();
    }
}

/// <summary>Data of the "Log observation" window.</summary>
public sealed partial class LogEntryDraftViewModel : ObservableValidator
{
    public LogEntryDraftViewModel(
        string objectId, string objectName, DateTime observedAtLocal, string location, string equipment)
    {
        ObjectId = objectId;
        ObjectName = objectName;
        ObservedDate = observedAtLocal.Date;
        ObservedTime = observedAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
        Location = location;
        Equipment = equipment;
    }

    public string ObjectId { get; }
    public string ObjectName { get; }

    /// <summary>"M42 — Orion Nebula"; for planets the id already is the name ("Jupiter").</summary>
    public string Title => ObjectId == ObjectName ? ObjectId : $"{ObjectId} — {ObjectName}";

    /// <summary>1-5 scale for seeing, transparency and rating.</summary>
    public IReadOnlyList<int> Scale { get; } = [1, 2, 3, 4, 5];

    [ObservableProperty]
    public partial DateTime ObservedDate { get; set; }

    [ObservableProperty, NotifyDataErrorInfo]
    [RegularExpression(@"^([01]?\d|2[0-3]):[0-5]\d$",
        ErrorMessageResourceType = typeof(Strings), ErrorMessageResourceName = nameof(Strings.LogTimeFormat))]
    public partial string ObservedTime { get; set; }

    [ObservableProperty]
    public partial string Location { get; set; }

    [ObservableProperty]
    public partial string Equipment { get; set; }

    [ObservableProperty]
    public partial int Seeing { get; set; } = 3;

    [ObservableProperty]
    public partial int Transparency { get; set; } = 3;

    [ObservableProperty]
    public partial int Rating { get; set; } = 3;

    [ObservableProperty]
    public partial string Notes { get; set; } = "";

    /// <summary>Local date and time of the observation, or null if the time is invalid.</summary>
    public DateTime? ObservedAtLocal =>
        TimeSpan.TryParseExact(ObservedTime, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time)
            && time < TimeSpan.FromDays(1)
            ? ObservedDate.Date + time
            : null;

    public void Validate() => ValidateAllProperties();
}
