using System.Windows;
using System.Windows.Media;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI;

/// <summary>Milky Way core planner: a bar per night (core in darkness, Moon-free part on top) and the best nights.</summary>
public partial class MilkyWayWindow : Window
{
    private readonly MilkyWayPlannerViewModel _viewModel;

    public MilkyWayWindow(MilkyWayPlannerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        NativeWindows.UseDarkTitleBar(this);
        DataContext = viewModel;
        NightsPlot.UserInputProcessor.IsEnabled = false;
        Loaded += async (_, _) =>
        {
            await _viewModel.LoadAsync();
            Draw();
        };
    }

    private void Draw()
    {
        var plot = NightsPlot.Plot;
        plot.Clear();
        plot.FigureBackground.Color = Theme("NightPanelBrush");
        plot.DataBackground.Color = Theme("SurfaceBrush");
        plot.Axes.Color(Theme("NightMutedTextBrush"));
        plot.Grid.MajorLineColor = Theme("NightBorderBrush");

        foreach (var night in _viewModel.Nights)
        {
            var x = night.Date.ToDateTime(TimeOnly.MinValue).ToOADate();
            if (night.CoreInDarkness > TimeSpan.Zero)
            {
                var dark = plot.Add.Rectangle(x, x + 0.9, 0, night.CoreInDarkness.TotalHours);
                dark.FillStyle.Color = Theme("NightMutedTextBrush").WithAlpha(0.45);
                dark.LineStyle.Width = 0;
            }

            if (night.CoreMoonFree > TimeSpan.Zero)
            {
                var free = plot.Add.Rectangle(x, x + 0.9, 0, night.CoreMoonFree.TotalHours);
                free.FillStyle.Color = Theme(night.IsGood ? "SuccessSoftBrush" : "ChartTwilightBrush");
                free.LineStyle.Width = 0;
            }
        }

        plot.Axes.DateTimeTicksBottom();
        plot.Axes.Left.Label.Text = Strings.MilkyWayAxisHours;
        plot.Axes.Left.Label.ForeColor = Theme("NightMutedTextBrush");
        plot.Axes.SetLimitsY(0, Math.Max(1, _viewModel.Nights.Select(n => n.CoreInDarkness.TotalHours).DefaultIfEmpty(0).Max() * 1.1));
        NightsPlot.Refresh();
    }

    private ScottPlot.Color Theme(string key) =>
        TryFindResource(key) is SolidColorBrush { Color: var c }
            ? new ScottPlot.Color(c.R, c.G, c.B, c.A)
            : ScottPlot.Colors.Magenta;
}
