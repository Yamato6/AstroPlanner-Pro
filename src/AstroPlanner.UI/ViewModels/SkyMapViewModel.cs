using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// The 2D polar chart the sky tab falls back to without WebView2: the objects above the horizon
/// (plan, selection and planets first, then the brightest), the Moon and the minimum altitude.
/// The window redraws it whenever <see cref="MapVersion"/> changes.
/// </summary>
public sealed partial class SkyMapViewModel : ObservableObject
{
    /// <summary>Most objects drawn on the 2D chart: with the full catalog thousands are above the horizon.</summary>
    public const int MaximumPoints = 300;

    /// <summary>How many of the brightest objects get a label (plan, selection and planets always do).</summary>
    public const int MaximumLabels = 60;

    private readonly IObserverContext _context;
    private readonly Func<IEnumerable<CelestialObjectRowViewModel>> _rows;
    private readonly Func<CelestialObjectRowViewModel?> _selected;
    private readonly Action<CelestialObjectRowViewModel> _select;
    private readonly Func<IEnumerable<string>> _planIds;

    /// <param name="rows">The rows that pass the Targets filters.</param>
    /// <param name="selected">The selected row, or null.</param>
    /// <param name="select">Selects a row in Targets.</param>
    /// <param name="planIds">Ids of the objects in My plan.</param>
    internal SkyMapViewModel(IObserverContext context, Func<IEnumerable<CelestialObjectRowViewModel>> rows,
        Func<CelestialObjectRowViewModel?> selected, Action<CelestialObjectRowViewModel> select, Func<IEnumerable<string>> planIds)
    {
        _context = context;
        _rows = rows;
        _selected = selected;
        _select = select;
        _planIds = planIds;
        _context.Recalculated += (_, _) =>
        {
            OnPropertyChanged(nameof(Moon));
            OnPropertyChanged(nameof(MoonIlluminationText));
            OnPropertyChanged(nameof(MinimumAltitude));
            Invalidate();
        };
    }

    /// <summary>Changes whenever the 2D chart must be redrawn.</summary>
    [ObservableProperty]
    public partial int MapVersion { get; private set; }

    public MoonState? Moon => _context.Moon;

    public string MoonIlluminationText => DisplayNames.MoonIllumination(_context.Moon);

    public double MinimumAltitude => _context.MinimumAltitude;

    /// <summary>Asks the window to redraw the chart.</summary>
    internal void Invalidate() => MapVersion++;

    /// <summary>
    /// Objects above the horizon that pass the filters: those in the plan, the selected one and
    /// the planets first, then the brightest, up to <see cref="MaximumPoints"/>.
    /// </summary>
    public IReadOnlyList<SkyMapPoint> GetSkyMapPoints()
    {
        var planIds = _planIds().ToHashSet();
        var selected = _selected();
        bool IsHighlighted(CelestialObjectRowViewModel item) =>
            item.IsPlanet || planIds.Contains(item.Id) || ReferenceEquals(item, selected);

        return _rows()
            .Where(item => item.AltitudeDegrees > 0)
            .OrderByDescending(IsHighlighted)
            .ThenBy(item => double.IsNaN(item.ApparentMagnitude) ? double.MaxValue : item.ApparentMagnitude)
            .Take(MaximumPoints)
            .Select((item, rank) => new SkyMapPoint(
                item.Id, item.AltitudeDegrees, item.AzimuthDegrees, item.ApparentMagnitude, item.IsVisible,
                planIds.Contains(item.Id), ReferenceEquals(item, selected), item.IsPlanet,
                HasLabel: IsHighlighted(item) || rank < MaximumLabels))
            .ToList();
    }

    /// <summary>
    /// Selects the object nearest to (x, y), in projection units (degrees), if it is closer
    /// than <paramref name="maximumDistance"/>.
    /// </summary>
    public bool SelectNearestOnSkyMap(double x, double y, double maximumDistance = 6)
    {
        var nearest = GetSkyMapPoints()
            .Select(point => (point.Id, Distance: Math.Sqrt(Math.Pow(point.Position.X - x, 2) + Math.Pow(point.Position.Y - y, 2))))
            .Where(candidate => candidate.Distance <= maximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (nearest.Id is null)
        {
            return false;
        }

        _select(_rows().First(item => item.Id == nearest.Id));
        return true;
    }
}
