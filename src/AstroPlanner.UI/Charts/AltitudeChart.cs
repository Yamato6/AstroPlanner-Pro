using System.Globalization;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using ScottPlot.WPF;

namespace AstroPlanner.UI.Charts;

/// <summary>The selected object's altitude through the night, with the Moon, twilight and clouds (Object detail).</summary>
internal static class AltitudeChart
{
    public static void Draw(WpfPlot chart, ObjectDetailViewModel viewModel, ChartPalette theme)
    {
        var points = viewModel.GetNightAltitudeCurve();

        // With invalid input the last curve is kept, like the table.
        if (points.Count == 0 && viewModel.InputError is not null)
        {
            return;
        }

        chart.Plot.Clear();
        chart.Plot.FigureBackground.Color = theme["NightPanelBrush"];
        chart.Plot.DataBackground.Color = theme["SurfaceBrush"];
        chart.Plot.Axes.Color(theme["NightMutedTextBrush"]);
        chart.Plot.Grid.MajorLineColor = theme["NightBorderBrush"];
        chart.Plot.Grid.MinorLineColor = theme["ControlRaisedBrush"];

        if (points.Count > 0)
        {
            // Background first (twilight and clouds) so that the curves are drawn on top.
            AddTwilightBands(chart, viewModel.Night, theme);
            AddCloudBands(chart, viewModel, theme);

            // X axis with real dates so that the day change shows 23:00, 00:00, 01:00, etc.
            var x = points.Select(p => p.LocalTime.ToOADate()).ToArray();
            var y = points.Select(p => p.AltitudeDegrees).ToArray();

            var moonPoints = viewModel.GetMoonAltitudeCurve();
            if (moonPoints.Count > 0)
            {
                var moonLine = chart.Plot.Add.Scatter(
                    moonPoints.Select(p => p.LocalTime.ToOADate()).ToArray(),
                    moonPoints.Select(p => p.AltitudeDegrees).ToArray());
                moonLine.LegendText = DisplayNames.Format(Strings.MoonLegendFormat, viewModel.MoonIlluminationText);
                moonLine.Color = theme["MoonBrush"].WithAlpha(0.8);
                moonLine.LineWidth = 1.5f;
                moonLine.LinePattern = ScottPlot.LinePattern.Dashed;
                moonLine.MarkerSize = 0;
            }

            var selectedTime = viewModel.ObservationDateTime.ToOADate();
            if (selectedTime >= x[0] && selectedTime <= x[^1])
            {
                var nowLine = chart.Plot.Add.VerticalLine(selectedTime);
                nowLine.LineColor = theme["NightMutedTextBrush"];
                nowLine.LineWidth = 1;
                nowLine.LinePattern = ScottPlot.LinePattern.Dotted;
                nowLine.LegendText = Strings.ChartChosenTime;
            }

            var altitudeLine = chart.Plot.Add.Scatter(x, y);
            altitudeLine.LegendText = Strings.ChartAltitude;
            altitudeLine.Color = theme["AccentBrush"];
            altitudeLine.LineWidth = 2;
            var thresholdLine = chart.Plot.Add.HorizontalLine(viewModel.MinimumAltitude);
            thresholdLine.LineColor = theme["WarningBrush"];
            thresholdLine.LineWidth = 2;
            thresholdLine.LegendText = Strings.ChartMinimumAltitude;

            chart.Plot.Axes.Title.Label.Text =
                viewModel.SelectedObject?.Title ?? string.Empty;
            chart.Plot.Axes.Bottom.Label.Text = Strings.ChartLocalTime;
            chart.Plot.Axes.Left.Label.Text = Strings.ChartAltitudeAxis;
            chart.Plot.ShowLegend();
            // Dark legend: with the default white background the Moon line is invisible.
            chart.Plot.Legend.BackgroundColor = theme["NightPanelBrush"].WithAlpha(0.92);
            chart.Plot.Legend.FontColor = theme["NightTextBrush"];
            chart.Plot.Legend.OutlineColor = theme["ControlBorderBrush"];
            var timeTicks = new ScottPlot.TickGenerators.DateTimeFixedInterval(
                new ScottPlot.TickGenerators.TimeUnits.Hour(),
                2,
                null,
                0,
                dateTime => dateTime.Date.AddHours(18));
            timeTicks.LabelFormatter = dateTime => dateTime.ToString("HH:mm", CultureInfo.CurrentCulture);
            chart.Plot.Axes.Bottom.TickGenerator = timeTicks;
            chart.Plot.Axes.SetLimitsY(-90, 90);
            chart.Plot.Axes.SetLimitsX(points[0].LocalTime.ToOADate(), points[^1].LocalTime.ToOADate());
        }

        chart.Refresh();
    }

    /// <summary>
    /// Hourly forecast cloud cover: one grey band per hour, more opaque the cloudier it is.
    /// Nothing is drawn without a forecast.
    /// </summary>
    private static void AddCloudBands(WpfPlot chart, ObjectDetailViewModel viewModel, ChartPalette theme)
    {
        var first = true;
        foreach (var (start, end, cloud) in viewModel.GetNightCloudCover())
        {
            if (cloud < 5 || end <= start)
            {
                continue;
            }

            var span = chart.Plot.Add.HorizontalSpan(start.ToOADate(), end.ToOADate());
            span.FillStyle.Color = theme["ChartCloudBrush"].WithAlpha(cloud / 100.0 * 0.45);
            span.LineStyle.Width = 0;
            if (first)
            {
                span.LegendText = Strings.ChartClouds;
                first = false;
            }
        }
    }

    /// <summary>
    /// Shades twilight: from sunset to the end of astronomical twilight and from the start of
    /// morning twilight to sunrise. If it never gets fully dark, the whole window is shaded.
    /// </summary>
    private static void AddTwilightBands(WpfPlot chart, Core.Models.NightInfo? night, ChartPalette theme)
    {
        if (night is null || night.IsFallbackWindow)
        {
            return;
        }

        var bands = night.HasAstronomicalDarkness
            ? new[]
            {
                (night.WindowStartLocal, night.AstronomicalDuskLocal!.Value),
                (night.AstronomicalDawnLocal!.Value, night.WindowEndLocal)
            }
            : new[] { (night.WindowStartLocal, night.WindowEndLocal) };

        var first = true;
        foreach (var (from, to) in bands)
        {
            var span = chart.Plot.Add.HorizontalSpan(from.ToOADate(), to.ToOADate());
            span.FillStyle.Color = theme["ChartTwilightBrush"].WithAlpha(0.35);
            span.LineStyle.Width = 0;
            if (first)
            {
                span.LegendText = Strings.ChartTwilight;
                first = false;
            }
        }
    }
}
