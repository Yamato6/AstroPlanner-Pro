using System.Windows;
using System.Windows.Media;
using AstroPlanner.Core.Imaging.Feasibility;

namespace AstroPlanner.UI.Charts;

/// <summary>
/// Theme colors (Themes/Dark.xaml, Themes/Night.xaml) for ScottPlot: the charts share the UI
/// palette. Read at drawing time, so a chart redrawn after a theme change takes the new colors.
/// </summary>
internal sealed class ChartPalette(FrameworkElement resources)
{
    /// <summary>The color of a brush key; magenta for a misspelled key, visible at a glance.</summary>
    public ScottPlot.Color this[string key] =>
        resources.TryFindResource(key) is SolidColorBrush { Color: var c }
            ? new ScottPlot.Color(c.R, c.G, c.B, c.A)
            : ScottPlot.Colors.Magenta;

    /// <summary>The color of a feasibility verdict (as in the tables).</summary>
    public ScottPlot.Color Verdict(FeasibilityVerdict? verdict) => verdict switch
    {
        FeasibilityVerdict.Excellent => this["SuccessBrush"],
        FeasibilityVerdict.Good => this["SuccessSoftBrush"],
        FeasibilityVerdict.Marginal => this["WarningBrush"],
        FeasibilityVerdict.Unlikely => this["DangerBrush"],
        _ => this["PlaceholderTextBrush"]
    };
}
