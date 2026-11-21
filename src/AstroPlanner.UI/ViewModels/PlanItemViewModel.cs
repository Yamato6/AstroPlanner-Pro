using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Planning;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Row of "My plan": the catalog object (with its current position), the user's note, its
/// priority (the list order), tonight's culmination and its block in the session schedule.
/// </summary>
public sealed partial class PlanItemViewModel : ObservableObject
{
    /// <summary>Null while the constructor sets the initial values, so that they are not saved again.</summary>
    private readonly Action<PlanItemViewModel>? _onNoteChanged;
    private readonly Action<PlanItemViewModel>? _onLockChanged;

    public PlanItemViewModel(CelestialObjectRowViewModel row, string note, Action<PlanItemViewModel> onNoteChanged,
        Action<PlanItemViewModel>? onLockChanged = null)
    {
        Row = row;
        Note = note;
        _onNoteChanged = onNoteChanged;
        _onLockChanged = onLockChanged;
    }

    /// <summary>Catalog row; the same instance as in the targets table.</summary>
    public CelestialObjectRowViewModel Row { get; }

    public string Id => Row.Id;

    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>1 = scheduled first (the list order).</summary>
    [ObservableProperty]
    public partial int Priority { get; set; }

    /// <summary>Best moment: maximum altitude within the night (local time).</summary>
    [ObservableProperty]
    public partial DateTime? CulminationLocal { get; set; }

    [ObservableProperty]
    public partial double MaximumAltitudeDegrees { get; set; }

    /// <summary>Forecast cloud cover (%) at culmination; null without a forecast.</summary>
    [ObservableProperty]
    public partial int? CloudCoverAtCulmination { get; set; }

    /// <summary>The user pinned the block: the scheduler keeps it (<see cref="LockedStartLocal"/>–<see cref="LockedEndLocal"/>).</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    public DateTime? LockedStartLocal { get; set; }
    public DateTime? LockedEndLocal { get; set; }

    /// <summary>Feasibility tonight with the active rig; null without one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VerdictText), nameof(Verdict))]
    public partial FeasibilityReport? Feasibility { get; set; }

    /// <summary>Block in tonight's schedule; null when not scheduled (no rig, no night).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartLocal), nameof(EndLocal), nameof(SubsText), nameof(IntegrationText),
        nameof(MeridianFlipLocal), nameof(StatusText), nameof(HasBlock))]
    public partial ScheduledTarget? Schedule { get; set; }

    public FeasibilityVerdict? Verdict => Feasibility?.Verdict;

    /// <summary>Sunlit satellites crossing the rig's field during the block (computed in the background).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CrossingsSummary), nameof(CrossingsText))]
    public partial IReadOnlyList<PlanCrossing> Crossings { get; set; } = [];

    /// <summary>How many satellites cross the field, empty when none.</summary>
    public string CrossingsSummary => Crossings.Count == 0 ? string.Empty : Crossings.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>One line per crossing ("ISS (ZARYA) crosses the field 21:14:05–21:14:12, mag −2.9"); null when none.</summary>
    public string? CrossingsText => Crossings.Count == 0 ? null : string.Join(Environment.NewLine, Crossings.Select(c => c.Text));

    public string VerdictText => Feasibility is { } report ? FeasibilityText.Verdict(report.Verdict) : string.Empty;

    public bool HasBlock => Schedule?.HasBlock == true;

    public DateTime? StartLocal => Schedule?.StartLocal;

    public DateTime? EndLocal => Schedule?.EndLocal;

    public DateTime? MeridianFlipLocal => Schedule?.MeridianFlipLocal;

    /// <summary>"120 × 30 s at ISO 800"; empty without subs.</summary>
    public string SubsText => Schedule is { SubCount: > 0 } s ? FeasibilityText.Subs(s.SubCount, s.SubSeconds, s.Iso, s.Gain) : string.Empty;

    /// <summary>"1h 00m"; empty without subs.</summary>
    public string IntegrationText => Schedule is { SubCount: > 0 } s ? DisplayNames.Duration(s.Integration) : string.Empty;

    /// <summary>Why there is no block, or that it is locked; empty for an ordinary scheduled block.</summary>
    public string StatusText => Schedule?.Status switch
    {
        ScheduleStatus.Locked => Strings.PlanStatusLocked,
        ScheduleStatus.NotPossibleTonight => Strings.PlanStatusNotPossible,
        ScheduleStatus.NoFreeTime => Strings.PlanStatusNoFreeTime,
        _ => string.Empty
    };

    partial void OnNoteChanged(string value) => _onNoteChanged?.Invoke(this);

    partial void OnIsLockedChanged(bool value) => _onLockChanged?.Invoke(this);
}
