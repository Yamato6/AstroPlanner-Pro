using System.Globalization;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using ScottPlot.WPF;

namespace AstroPlanner.UI.Charts;

/// <summary>Predictions vs results (Logbook).</summary>
internal static class PredictionChart
{
    /// <summary>
    /// Predictions vs results (spec §8.7): one point per photographed entry, predicted score
    /// against the rating, colored by verdict; the dashed line is where a perfect model would put them.
    /// </summary>
    public static void Draw(WpfPlot chart, LogbookViewModel logbook, ChartPalette theme)
    {
        var plot = chart.Plot;
        plot.Clear();
        plot.FigureBackground.Color = theme["NightPanelBrush"];
        plot.DataBackground.Color = theme["SurfaceBrush"];
        plot.Axes.Color(theme["NightMutedTextBrush"]);
        plot.Grid.MajorLineColor = theme["NightBorderBrush"];
        plot.Axes.Bottom.Label.Text = Strings.PredictionsAxisScore;
        plot.Axes.Left.Label.Text = Strings.PredictionsAxisRating;
        plot.Axes.Bottom.Label.ForeColor = theme["NightMutedTextBrush"];
        plot.Axes.Left.Label.ForeColor = theme["NightMutedTextBrush"];

        var ideal = plot.Add.Line(0, 1, 100, 5);
        ideal.LineColor = theme["NightMutedTextBrush"].WithAlpha(0.6);
        ideal.LinePattern = ScottPlot.LinePattern.Dashed;
        foreach (var point in logbook.Predictions.Points)
        {
            plot.Add.Marker(point.PredictedScore, point.Rating, ScottPlot.MarkerShape.FilledCircle, 9, theme.Verdict(point.Verdict));
        }

        plot.Axes.SetLimitsX(0, 100);
        plot.Axes.SetLimitsY(0.5, 5.5);
        chart.Refresh();
    }
}
