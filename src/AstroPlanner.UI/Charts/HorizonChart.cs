using System.Globalization;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using ScottPlot.WPF;

namespace AstroPlanner.UI.Charts;

/// <summary>A saved site's horizon profile (Locations).</summary>
internal static class HorizonChart
{
    /// <summary>Horizon profile preview: altitude against azimuth, the obstructed part filled.</summary>
    public static void Draw(WpfPlot chart, LocationsViewModel viewModel, ChartPalette theme)
    {
        var plot = chart.Plot;
        plot.Clear();
        plot.FigureBackground.Color = theme["SurfaceBrush"];
        plot.DataBackground.Color = theme["NightPanelBrush"];
        plot.Axes.Color(theme["NightMutedTextBrush"]);
        plot.Grid.MajorLineColor = theme["NightBorderBrush"];

        var points = viewModel.GetHorizonPreview();
        if (points.Count > 0)
        {
            var xs = points.Select(p => p.AzimuthDegrees).ToArray();
            var ys = points.Select(p => p.AltitudeDegrees).ToArray();
            var fill = plot.Add.FillY(xs, ys, xs.Select(_ => -10.0).ToArray());
            fill.FillColor = theme["SkyHorizonBrush"].WithAlpha(0.45);
            fill.LineColor = theme["WarningBrush"];
            fill.LineWidth = 1.5f;
        }

        plot.Axes.Bottom.Label.Text = Strings.HorizonPreviewAzimuth;
        plot.Axes.Left.Label.Text = Strings.HorizonPreviewAltitude;
        plot.Axes.SetLimits(0, 360, -10, Math.Max(30, points.Count > 0 ? points.Max(p => p.AltitudeDegrees) + 5 : 30));
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericFixedInterval(45);
        chart.Refresh();
    }
}
