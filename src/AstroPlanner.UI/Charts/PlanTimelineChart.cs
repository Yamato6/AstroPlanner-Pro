using System.Globalization;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using ScottPlot.WPF;

namespace AstroPlanner.UI.Charts;

/// <summary>The session timeline of My plan.</summary>
internal static class PlanTimelineChart
{
    /// <summary>
    /// Session timeline (spec §4.6): one row per plan item in priority order, the usable window as a
    /// thin bar, the scheduled block coloured by verdict (thick outline = locked), meridian flips as
    /// dashed ticks, over the twilight and cloud bands.
    /// </summary>
    public static void Draw(WpfPlot chart, PlanTimeline? timeline, DateTime? chosenTime, ChartPalette theme)
    {
        var plot = chart.Plot;
        plot.Clear();
        plot.FigureBackground.Color = theme["NightPanelBrush"];
        plot.DataBackground.Color = theme["SurfaceBrush"];
        plot.Axes.Color(theme["NightMutedTextBrush"]);
        plot.Grid.MajorLineColor = theme["NightBorderBrush"];
        plot.Grid.YAxisStyle.MajorLineStyle.Width = 0;
        if (timeline is null || timeline.Rows.Count == 0)
        {
            chart.Refresh();
            return;
        }

        var count = timeline.Rows.Count;
        foreach (var band in timeline.TwilightBands)
        {
            var span = plot.Add.HorizontalSpan(band.Start.ToOADate(), band.End.ToOADate());
            span.FillStyle.Color = theme["ChartTwilightBrush"].WithAlpha(0.35);
            span.LineStyle.Width = 0;
        }

        foreach (var (cloud, percent) in timeline.Clouds)
        {
            if (percent < 5) continue;
            var span = plot.Add.HorizontalSpan(cloud.Start.ToOADate(), cloud.End.ToOADate());
            span.FillStyle.Color = theme["ChartCloudBrush"].WithAlpha(percent / 100.0 * 0.45);
            span.LineStyle.Width = 0;
        }

        var ticks = new ScottPlot.TickGenerators.NumericManual();
        for (var index = 0; index < count; index++)
        {
            var row = timeline.Rows[index];
            double y = count - index;
            ticks.AddMajor(y, row.Label);
            foreach (var usable in row.Usable)
            {
                var bar = plot.Add.Rectangle(usable.Start.ToOADate(), usable.End.ToOADate(), y - 0.06, y + 0.06);
                bar.FillStyle.Color = theme["NightMutedTextBrush"].WithAlpha(0.45);
                bar.LineStyle.Width = 0;
            }

            if (row.Block is { } block)
            {
                var rectangle = plot.Add.Rectangle(block.Start.ToOADate(), block.End.ToOADate(), y - 0.32, y + 0.32);
                rectangle.FillStyle.Color = theme.Verdict(row.Verdict).WithAlpha(0.85);
                rectangle.LineStyle.Color = theme[row.Locked ? "NightTextBrush" : "NightBorderBrush"];
                rectangle.LineStyle.Width = row.Locked ? 2.5f : 1;
            }

            if (row.MeridianFlipLocal is { } flip)
            {
                var line = plot.Add.Line(flip.ToOADate(), y - 0.42, flip.ToOADate(), y + 0.42);
                line.LineColor = theme["NightTextBrush"];
                line.LineWidth = 2;
                line.LinePattern = ScottPlot.LinePattern.Dashed;
                line.MarkerSize = 0;
            }

            // Sunlit satellites crossing the field: a short tick above the block.
            foreach (var crossing in row.CrossingsLocal ?? [])
            {
                var tick = plot.Add.Line(crossing.ToOADate(), y + 0.18, crossing.ToOADate(), y + 0.46);
                tick.LineColor = theme["DangerBrush"];
                tick.LineWidth = 2;
                tick.MarkerSize = 0;
            }
        }

        if (chosenTime is { } chosen && chosen >= timeline.WindowStartLocal && chosen <= timeline.WindowEndLocal)
        {
            var nowLine = plot.Add.VerticalLine(chosen.ToOADate());
            nowLine.LineColor = theme["NightMutedTextBrush"];
            nowLine.LineWidth = 1;
            nowLine.LinePattern = ScottPlot.LinePattern.Dotted;
        }

        plot.Axes.Left.TickGenerator = ticks;
        var timeTicks = new ScottPlot.TickGenerators.DateTimeFixedInterval(
            new ScottPlot.TickGenerators.TimeUnits.Hour(), 1, null, 0, dateTime => dateTime.Date);
        timeTicks.LabelFormatter = dateTime => dateTime.ToString("HH:mm", CultureInfo.CurrentCulture);
        plot.Axes.Bottom.TickGenerator = timeTicks;
        plot.Axes.SetLimitsX(timeline.WindowStartLocal.ToOADate(), timeline.WindowEndLocal.ToOADate());
        plot.Axes.SetLimitsY(0.4, count + 0.6);
        chart.Refresh();
    }
}
