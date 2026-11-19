using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// All-sky chart: the objects above the horizon that pass the table filters, the plan,
/// the selection and the Moon. Clicking selects the nearest object.
/// </summary>
public sealed partial class SkyViewModel : ObservableObject
{
    private readonly IObserverContext _context;
    private readonly TargetsViewModel _targets;
    private readonly SessionPlanViewModel _plan;

    public SkyViewModel(IObserverContext context, TargetsViewModel targets, SessionPlanViewModel plan)
    {
        _context = context;
        _targets = targets;
        _plan = plan;
        _targets.SelectionOrTableChanged += (_, _) => MapVersion++;
        _plan.Changed += (_, _) => MapVersion++;
    }

    /// <summary>Changes whenever the chart must be redrawn.</summary>
    [ObservableProperty]
    public partial int MapVersion { get; private set; }

    public MoonState? Moon => _context.Moon;

    public string MoonIlluminationText => DisplayNames.MoonIllumination(_context.Moon);

    public double MinimumAltitude => _context.MinimumAltitude;

    /// <summary>Most objects drawn on the chart: with the full catalog thousands are above the horizon.</summary>
    public const int MaximumPoints = 300;

    /// <summary>How many of the brightest objects get a label (plan, selection and planets always do).</summary>
    public const int MaximumLabels = 60;

    /// <summary>
    /// Objects above the horizon that pass the filters: those in the plan, the selected one and
    /// the planets first, then the brightest, up to <see cref="MaximumPoints"/>.
    /// </summary>
    public IReadOnlyList<SkyMapPoint> GetSkyMapPoints()
    {
        var planIds = _plan.Items.Select(item => item.Id).ToHashSet();
        var selected = _targets.SelectedObject;
        bool IsHighlighted(CelestialObjectRowViewModel item) =>
            item.IsPlanet || planIds.Contains(item.Id) || ReferenceEquals(item, selected);

        return _targets.Objects
            .Where(item => item.AltitudeDegrees > 0)
            .OrderByDescending(IsHighlighted)
            .ThenBy(item => double.IsNaN(item.ApparentMagnitude) ? double.MaxValue : item.ApparentMagnitude)
            .Take(MaximumPoints)
            .Select((item, rank) => new SkyMapPoint(
                item.Id,
                item.AltitudeDegrees,
                item.AzimuthDegrees,
                item.ApparentMagnitude,
                item.IsVisible,
                planIds.Contains(item.Id),
                ReferenceEquals(item, selected),
                item.IsPlanet,
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
            .Select(point => (point.Id, Distance: Math.Sqrt(
                Math.Pow(point.Position.X - x, 2) + Math.Pow(point.Position.Y - y, 2))))
            .Where(candidate => candidate.Distance <= maximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (nearest.Id is null)
        {
            return false;
        }

        _targets.SelectedObject = _targets.Objects.First(item => item.Id == nearest.Id);
        return true;
    }
}
