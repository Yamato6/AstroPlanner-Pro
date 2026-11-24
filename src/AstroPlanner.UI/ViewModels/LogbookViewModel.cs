using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AstroPlanner.Core.Imaging.Feasibility;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Observation logbook: logged entries (observed or photographed), objects already observed,
/// Messier marathon and Caldwell progress, and the model's predictions against the results.
/// </summary>
public sealed partial class LogbookViewModel : ObservableObject
{
    public const int MessierTotal = 110;
    public const int CaldwellTotal = 109;

    private readonly IObservationLogService _service;
    private readonly IAppLogger _logger;
    private HashSet<string> _observedIds = [];
    private HashSet<string> _photographedIds = [];
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

    /// <summary>Messier objects with at least one photographed entry.</summary>
    [ObservableProperty]
    public partial int PhotographedMessierCount { get; private set; }

    /// <summary>Caldwell objects with at least one photographed entry.</summary>
    [ObservableProperty]
    public partial int PhotographedCaldwellCount { get; private set; }

    /// <summary>"3 photographed" under the Messier progress bar.</summary>
    public string MessierPhotographedText => Format(Strings.LogbookPhotographedFormat, PhotographedMessierCount);

    public string CaldwellPhotographedText => Format(Strings.LogbookPhotographedFormat, PhotographedCaldwellCount);

    public IReadOnlySet<string> PhotographedIds => _photographedIds;

    // ─── Predictions vs results (spec §8.7) ────────────────────────────

    /// <summary>The model's predictions against the ratings of the photographed entries.</summary>
    [ObservableProperty]
    public partial PredictionStatistics Predictions { get; private set; } = PredictionComparison.Compare([]);

    public bool HasPredictions => Predictions.Points.Count > 0;

    /// <summary>One line per verdict: how many, mean rating, mean predicted score and the difference.</summary>
    public IReadOnlyList<string> PredictionLines => [.. Predictions.ByVerdict.Select(v => Format(Strings.PredictionVerdictLineFormat,
        FeasibilityText.Verdict(v.Verdict), v.Count, v.MeanRating, v.MeanPredictedScore, v.MeanDifference))];

    /// <summary>The overall comparison, or how to get one.</summary>
    public string PredictionSummaryText =>
        !HasPredictions ? Strings.PredictionsEmpty
        : Predictions.Correlation is { } r
            ? Format(Strings.PredictionsSummaryFormat, Predictions.Points.Count, Predictions.MeanDifference ?? 0, r)
            : Format(Strings.PredictionsSummaryNoCorrelationFormat, Predictions.Points.Count, Predictions.MeanDifference ?? 0,
                PredictionComparison.MinimumPointsForCorrelation);

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

    /// <summary>
    /// Draft of a new entry with the capture pre-filled: the rig, and the subs of the plan block
    /// (else the model's recommendation). <paramref name="photographed"/> preselects "Photographed".
    /// </summary>
    public LogEntryDraftViewModel CreateDraft(string objectId, string objectName, DateTime observedAtLocal, string location,
        CaptureDetails capture, PredictionSummary? prediction, bool photographed)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var draft = new LogEntryDraftViewModel(objectId, objectName, observedAtLocal, location,
            capture.RigName.Length > 0 ? capture.RigName : Entries.FirstOrDefault()?.Equipment ?? "")
        {
            Prediction = prediction
        };
        draft.Prefill(capture);
        draft.IsPhotographed = photographed;
        return draft;
    }

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
            draft.Notes.Trim())
        {
            Kind = draft.IsPhotographed ? LogEntryKind.Photographed : LogEntryKind.Observed,
            Capture = draft.BuildCapture(),
            Prediction = draft.IsPhotographed ? draft.Prediction : null
        };

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
            Strings.CsvNotes, Strings.CsvKind, Strings.CsvRig, Strings.CsvSubSeconds, Strings.CsvIso, Strings.CsvGain,
            Strings.CsvSubCount, Strings.CsvImage, Strings.CsvPredictedVerdict, Strings.CsvPredictedScore]);
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
                entry.Notes,
                entry.IsPhotographed ? Strings.LogKindPhotographed : Strings.LogKindObserved,
                entry.Capture?.RigName ?? "",
                entry.Capture?.SubSeconds?.ToString(culture) ?? "",
                entry.Capture?.Iso?.ToString(culture) ?? "",
                entry.Capture?.Gain?.ToString(culture) ?? "",
                entry.Capture?.SubCount?.ToString(culture) ?? "",
                entry.Capture?.ImagePath ?? "",
                entry.Prediction is { } prediction ? FeasibilityText.Verdict(prediction.Verdict) : "",
                entry.Prediction?.Score.ToString(culture) ?? ""
            ]);
        }

        return csv.ToString();
    }

    /// <summary>File name offered when the logbook is exported: "AstroPlannerPro-logbook-2026-11-21.csv".</summary>
    public static string SuggestedExportFileName(DateTime today) =>
        string.Create(CultureInfo.InvariantCulture, $"AstroPlannerPro-logbook-{today:yyyy-MM-dd}.csv");

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
        _photographedIds = Entries.Where(e => e.IsPhotographed).Select(e => e.ObjectId).ToHashSet(StringComparer.Ordinal);
        ObservedMessierCount = _observedIds.Count(id => MessierId().IsMatch(id));
        ObservedCaldwellCount = _observedIds.Count(_caldwellIds.Contains);
        PhotographedMessierCount = _photographedIds.Count(id => MessierId().IsMatch(id));
        PhotographedCaldwellCount = _photographedIds.Count(_caldwellIds.Contains);
        Predictions = PredictionComparison.Compare(Entries);
        OnPropertyChanged(nameof(PhotographedIds));
        OnPropertyChanged(nameof(MessierPhotographedText));
        OnPropertyChanged(nameof(CaldwellPhotographedText));
        OnPropertyChanged(nameof(HasPredictions));
        OnPropertyChanged(nameof(PredictionLines));
        OnPropertyChanged(nameof(PredictionSummaryText));
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

    // ─── Photographed (spec §4.7) ──────────────────────────────────────

    /// <summary>The entry records an imaging session: the capture fields are saved.</summary>
    [ObservableProperty]
    public partial bool IsPhotographed { get; set; }

    /// <summary>Rig of the inventory the capture fields came from; null when typed by hand.</summary>
    public string? RigId { get; private set; }

    [ObservableProperty]
    public partial string RigName { get; set; } = "";

    [ObservableProperty, NotifyDataErrorInfo]
    [CustomValidation(typeof(LogEntryDraftViewModel), nameof(ValidateSeconds))]
    public partial string SubSecondsText { get; set; } = "";

    [ObservableProperty, NotifyDataErrorInfo]
    [CustomValidation(typeof(LogEntryDraftViewModel), nameof(ValidateCount))]
    public partial string IsoText { get; set; } = "";

    [ObservableProperty, NotifyDataErrorInfo]
    [CustomValidation(typeof(LogEntryDraftViewModel), nameof(ValidateCount))]
    public partial string GainText { get; set; } = "";

    [ObservableProperty, NotifyDataErrorInfo]
    [CustomValidation(typeof(LogEntryDraftViewModel), nameof(ValidateCount))]
    public partial string SubCountText { get; set; } = "";

    /// <summary>The stacked or best image (a file the user chose; it is not copied).</summary>
    [ObservableProperty]
    public partial string ImagePath { get; set; } = "";

    /// <summary>The model's prediction for tonight, kept with a photographed entry.</summary>
    public PredictionSummary? Prediction { get; init; }

    /// <summary>"Predicted: Good · score 64/100 · SNR 18", or that there is none.</summary>
    public string PredictionText => Prediction is { } p
        ? Format(Strings.LogEntryPredictionFormat, FeasibilityText.Verdict(p.Verdict), p.Score, p.SignalToNoise)
        : Strings.LogEntryNoPrediction;

    /// <summary>Fills the capture fields (rig, sub, ISO/gain, count).</summary>
    public void Prefill(CaptureDetails capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        RigId = capture.RigId;
        RigName = capture.RigName;
        SubSecondsText = capture.SubSeconds?.ToString("0.#", CultureInfo.CurrentCulture) ?? "";
        IsoText = capture.Iso?.ToString(CultureInfo.CurrentCulture) ?? "";
        GainText = capture.Gain?.ToString(CultureInfo.CurrentCulture) ?? "";
        SubCountText = capture.SubCount?.ToString(CultureInfo.CurrentCulture) ?? "";
        ImagePath = capture.ImagePath ?? "";
    }

    /// <summary>The capture of a photographed entry; null for an observed one.</summary>
    public CaptureDetails? BuildCapture() => !IsPhotographed
        ? null
        : new CaptureDetails
        {
            RigId = RigName.Trim().Length > 0 ? RigId : null,
            RigName = RigName.Trim(),
            SubSeconds = ParseSeconds(SubSecondsText),
            Iso = ParseCount(IsoText),
            Gain = ParseCount(GainText),
            SubCount = ParseCount(SubCountText),
            ImagePath = ImagePath.Trim().Length > 0 ? ImagePath.Trim() : null
        };

    private static double? ParseSeconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) && value > 0 && double.IsFinite(value)
            ? value
            : null;

    private static int? ParseCount(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= 0 ? value : null;

    public static ValidationResult? ValidateSeconds(string text, ValidationContext context) =>
        string.IsNullOrWhiteSpace(text) || ParseSeconds(text) is not null
            ? ValidationResult.Success
            : new ValidationResult(Strings.LogEntrySubInvalid, [context?.MemberName ?? ""]);

    public static ValidationResult? ValidateCount(string text, ValidationContext context) =>
        string.IsNullOrWhiteSpace(text) || ParseCount(text) is not null
            ? ValidationResult.Success
            : new ValidationResult(Strings.LogEntryCountInvalid, [context?.MemberName ?? ""]);

    /// <summary>Local date and time of the observation, or null if the time is invalid.</summary>
    public DateTime? ObservedAtLocal =>
        TimeSpan.TryParseExact(ObservedTime, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time)
            && time < TimeSpan.FromDays(1)
            ? ObservedDate.Date + time
            : null;

    public void Validate() => ValidateAllProperties();
}
