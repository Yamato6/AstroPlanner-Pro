using System.Globalization;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.Application.ViewModels;
using ScottPlot.WPF;

namespace AstroPlanner.UI.Charts;

/// <summary>The 2D sky chart shown without WebView2.</summary>
internal static class SkyMapChart
{
    /// <summary>
    /// Polar sky chart: altitude circles every 30°, cardinal points, objects above the horizon
    /// (size by magnitude), My plan, selection and the Moon.
    /// </summary>
    public static void Draw(WpfPlot chart, SkyMapViewModel viewModel, ChartPalette theme)
    {
        var plot = chart.Plot;
        plot.Clear();
        plot.FigureBackground.Color = theme["SurfaceBrush"];
        plot.DataBackground.Color = theme["SurfaceBrush"];
        plot.Axes.Frameless();
        plot.HideGrid();

        var gridColor = theme["NightBorderBrush"];
        var horizon = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius);
        horizon.LineColor = theme["SkyHorizonBrush"];
        horizon.LineWidth = 2;
        horizon.FillColor = theme["SkyFillBrush"];

        foreach (var altitude in new[] { 30.0, 60.0 })
        {
            var ring = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius - altitude);
            ring.LineColor = gridColor;
            ring.LineWidth = 1;
        }

        // Minimum altitude: inside this circle an object counts as visible.
        var threshold = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius - viewModel.MinimumAltitude);
        threshold.LineColor = theme["WarningBrush"].WithAlpha(0.6);
        threshold.LineWidth = 1;
        threshold.LinePattern = ScottPlot.LinePattern.Dashed;

        foreach (var azimuth in new[] { 0.0, 45.0, 90.0, 135.0 })
        {
            var (x1, y1) = SkyProjection.ToXY(0, azimuth);
            var (x2, y2) = SkyProjection.ToXY(0, azimuth + 180);
            var line = plot.Add.Line(x1, y1, x2, y2);
            line.LineColor = gridColor;
            line.LineWidth = 1;
        }

        foreach (var (label, azimuth) in new[] { (Strings.SkyNorth, 0.0), (Strings.SkyEast, 90.0), (Strings.SkySouth, 180.0), (Strings.SkyWest, 270.0) })
        {
            var (x, y) = SkyProjection.ToXY(-8, azimuth);
            var text = plot.Add.Text(label, x, y);
            text.LabelFontColor = theme["LabelTextBrush"];
            text.LabelFontSize = 16;
            text.LabelBold = true;
            text.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
        }

        // The Moon goes first (underneath): its disc must not hide nearby objects.
        if (viewModel.Moon is { IsAboveHorizon: true } moon)
        {
            var (x, y) = SkyProjection.ToXY(moon.Horizontal.AltitudeDegrees, moon.Horizontal.AzimuthDegrees);
            plot.Add.Marker(x, y, ScottPlot.MarkerShape.FilledCircle, 22, theme["MoonBrush"].WithAlpha(0.85));
            var label = plot.Add.Text(DisplayNames.Format(Strings.MoonLabelFormat, viewModel.MoonIlluminationText), x, y - 6);
            label.LabelFontColor = theme["MoonBrush"];
            label.LabelFontSize = 12;
            label.LabelAlignment = ScottPlot.Alignment.UpperCenter;
        }

        foreach (var point in viewModel.GetSkyMapPoints().OrderBy(p => p.IsInPlan || p.IsSelected || p.IsPlanet))
        {
            var (x, y) = point.Position;
            float size;
            if (point.IsPlanet)
            {
                // Planets: golden diamond (orange when in My plan), with their name.
                size = 13;
                plot.Add.Marker(x, y, ScottPlot.MarkerShape.FilledDiamond, size,
                    theme[point.IsInPlan ? "WarningBrush" : "PlanetBrush"]);
            }
            else
            {
                var color = point.IsInPlan ? "WarningBrush" : point.IsVisible ? "AccentBrush" : "PlaceholderTextBrush";
                size = double.IsNaN(point.Magnitude) ? 4 : (float)Math.Clamp(13 - point.Magnitude, 4, 10);
                plot.Add.Marker(x, y, ScottPlot.MarkerShape.FilledCircle, size, theme[color]);
            }

            if (point.IsSelected)
            {
                plot.Add.Marker(x, y, ScottPlot.MarkerShape.OpenCircle, size + 12, theme["HighlightTextBrush"]);
            }

            if (!point.HasLabel)
            {
                continue;
            }

            var label = plot.Add.Text(point.Id, x + 2.5, y);
            var highlighted = point.IsInPlan || point.IsSelected || point.IsPlanet;
            label.LabelFontColor = theme[point.IsPlanet ? "PlanetBrush" : highlighted ? "StrongTextBrush" : "SkyLabelBrush"];
            label.LabelFontSize = highlighted ? 12 : 10;
            label.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
        }

        // Full, undistorted circle: the short side of the chart spans ±100 and the long side
        // grows in proportion (SquareUnits used to crop the short one).
        var width = Math.Max(chart.ActualWidth, 1);
        var height = Math.Max(chart.ActualHeight, 1);
        const double halfSpan = 100;
        plot.Axes.SetLimits(
            -halfSpan * Math.Max(1, width / height),
            halfSpan * Math.Max(1, width / height),
            -halfSpan * Math.Max(1, height / width),
            halfSpan * Math.Max(1, height / width));
        chart.Refresh();
    }
}
