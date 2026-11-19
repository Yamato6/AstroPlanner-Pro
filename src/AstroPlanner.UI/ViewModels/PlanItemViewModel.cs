using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Row of "My plan": the catalog object (with its current position), the user's note and
/// tonight's culmination time, which orders the plan.
/// </summary>
public sealed partial class PlanItemViewModel : ObservableObject
{
    /// <summary>Null while the constructor sets the initial note, so that it is not saved again.</summary>
    private readonly Action<PlanItemViewModel>? _onNoteChanged;

    public PlanItemViewModel(CelestialObjectRowViewModel row, string note, Action<PlanItemViewModel> onNoteChanged)
    {
        Row = row;
        Note = note;
        _onNoteChanged = onNoteChanged;
    }

    /// <summary>Catalog row; the same instance as in the targets table.</summary>
    public CelestialObjectRowViewModel Row { get; }

    public string Id => Row.Id;

    [ObservableProperty]
    public partial string Note { get; set; }

    /// <summary>Best moment: maximum altitude within the night (local time).</summary>
    [ObservableProperty]
    public partial DateTime? CulminationLocal { get; set; }

    [ObservableProperty]
    public partial double MaximumAltitudeDegrees { get; set; }

    /// <summary>Forecast cloud cover (%) at culmination; null without a forecast.</summary>
    [ObservableProperty]
    public partial int? CloudCoverAtCulmination { get; set; }

    partial void OnNoteChanged(string value) => _onNoteChanged?.Invoke(this);
}
