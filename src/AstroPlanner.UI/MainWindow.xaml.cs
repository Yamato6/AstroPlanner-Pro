using System.Globalization;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI;

/// <summary>
/// Minimal code-behind: it only redraws the ScottPlot charts when their data changes and opens
/// dialogs. All the logic lives in the view models (strict MVVM).
/// </summary>
public partial class MainWindow : Window
{
    private readonly IAppLogger _logger;
    private ShellViewModel? _viewModel;
    private bool _skyStarted;

    public MainWindow(IAppLogger logger)
    {
        _logger = logger;
        InitializeComponent();
        NativeWindows.UseDarkTitleBar(this);
        AltitudePlot.UserInputProcessor.IsEnabled = false;
        SkyPlot.UserInputProcessor.IsEnabled = false;
        HorizonPlot.UserInputProcessor.IsEnabled = false;
        TimelinePlot.UserInputProcessor.IsEnabled = false;
        SkyPlot.SizeChanged += (_, _) =>
        {
            if (_viewModel is not null) UpdateSkyMap(_viewModel.Sky);
        };
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Tabs.SelectionChanged += OnTabsSelectionChanged;
        SkyHostView.Unavailable += (_, reason) => _viewModel?.Sky.UseFallback(reason);
    }

    private static string SkyQuery() => SkyLaunchOptions.Query(Environment.GetCommandLineArgs());

    private void OnTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, Tabs))
        {
            return;
        }

        if (Tabs.SelectedItem == SkyTab)
        {
            StartSky();
        }

        // The framing preview and the description are fetched only while the detail is on screen.
        if (_viewModel is not null)
        {
            _viewModel.Detail.IsShown = Tabs.SelectedItem == DetailTab;
        }
    }

    /// <summary>WebView2 starts the first time the Sky tab is shown (it costs memory and ~1 s).</summary>
    private async void StartSky()
    {
        if (_skyStarted || _viewModel is null)
        {
            return;
        }

        _skyStarted = true;
        _viewModel.Sky.Attach(SkyHostView);
        await SkyHostView.StartAsync(_logger, SkyQuery());
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Unsubscribe from the previous view model (if any).
        if (_viewModel is not null)
        {
            _viewModel.Detail.PropertyChanged -= OnDetailPropertyChanged;
            _viewModel.Sky.PropertyChanged -= OnSkyPropertyChanged;
            _viewModel.Sky.FocusSearchRequested -= OnSkyFocusSearch;
            _viewModel.Sky.OpenDetailsRequested -= OnSkyOpenDetails;
            _viewModel.Sky.ShowSkyRequested -= OnShowSkyRequested;
            _viewModel.Locations.PropertyChanged -= OnLocationsPropertyChanged;
            _viewModel.Plan.TimelineChanged -= OnPlanTimelineChanged;
        }

        _viewModel = e.NewValue as ShellViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Detail.PropertyChanged += OnDetailPropertyChanged;
            _viewModel.Sky.PropertyChanged += OnSkyPropertyChanged;
            _viewModel.Sky.FocusSearchRequested += OnSkyFocusSearch;
            _viewModel.Sky.OpenDetailsRequested += OnSkyOpenDetails;
            _viewModel.Sky.ShowSkyRequested += OnShowSkyRequested;
            _viewModel.Locations.PropertyChanged += OnLocationsPropertyChanged;
            _viewModel.Plan.TimelineChanged += OnPlanTimelineChanged;
        }
    }

    private void OnPlanTimelineChanged(object? sender, EventArgs e) => UpdateTimeline(_viewModel?.Plan.Timeline);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.Settings.LoadCitiesCommand.ExecuteAsync(null);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        UpdateChart(_viewModel.Detail);
        UpdateSkyMap(_viewModel.Sky);
        if (SkyQuery().Length > 0)
        {
            Tabs.SelectedItem = SkyTab;
        }

        await _viewModel.CheckForUpdatesAsync(AppVersion.Current);
    }

    /// <summary>
    /// Restores the size and position of the previous session (fitted to the current desktop)
    /// and saves them on close. Called before Show().
    /// </summary>
    public void RememberPlacement(WindowPlacementStore store)
    {
        if (store.Load() is { } saved)
        {
            var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var placement = saved.FitTo(virtualScreen, SystemParameters.WorkArea, MinWidth, MinHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            if (placement.IsMaximized)
            {
                WindowState = WindowState.Maximized;
            }
        }

        // Maximized and then minimized must come back maximized next time.
        var wasMaximized = WindowState == WindowState.Maximized;
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                wasMaximized = WindowState == WindowState.Maximized;
            }
        };

        Closing += (_, _) =>
        {
            // RestoreBounds: the "normal" size even while maximized or minimized.
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (!bounds.IsEmpty)
            {
                store.Save(new WindowPlacement(bounds.Left, bounds.Top, bounds.Width, bounds.Height, wasMaximized));
            }
        };
    }

    /// <summary>Opens UI links in the browser (allowed sites only, see <see cref="SafeLinks"/>).</summary>
    private void OnLinkClicked(object sender, RequestNavigateEventArgs e)
    {
        SafeLinks.Open(e.Uri);
        e.Handled = true;
    }

    private void OnAboutClicked(object sender, RoutedEventArgs e) => ShowAbout();

    private void ShowAbout() => new AboutWindow { Owner = this }.ShowDialog();

    /// <summary>Opens the Milky Way core planner for the active site.</summary>
    private void OnMilkyWayClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.Tonight.CreateMilkyWayPlanner() is { } planner)
        {
            new MilkyWayWindow(planner) { Owner = this }.ShowDialog();
        }
    }

    /// <summary>
    /// Keyboard shortcuts: Ctrl+F search, F5 now, Ctrl+, settings, Ctrl+P print the plan,
    /// Ctrl+E export the plan, F1 about.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        var none = Keyboard.Modifiers == ModifierKeys.None;
        switch (e.Key)
        {
            case Key.F when ctrl:
                Tabs.SelectedItem = PlannerTab;
                // Right after switching tabs the box is not on screen yet: focus it once it is.
                Dispatcher.BeginInvoke(() =>
                {
                    SearchBox.Focus();
                    SearchBox.SelectAll();
                }, DispatcherPriority.Input);
                break;
            case Key.F5 when none:
                _viewModel?.SetNowCommand.Execute(null);
                break;
            case Key.OemComma when ctrl:
                OnSettingsClicked(sender, e);
                break;
            case Key.P when ctrl:
                Tabs.SelectedItem = PlanTab;   // with an empty plan, at least the user sees why nothing is printed
                OnPrintPlanClicked(sender, e);
                break;
            case Key.E when ctrl:
                Tabs.SelectedItem = PlanTab;
                OnExportPlanClicked(sender, e);
                break;
            case Key.F1 when none:
                ShowAbout();
                break;
            // Sky tab (when the focus is not in a text box; inside the renderer it sends them itself).
            case Key.Space when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                _viewModel?.Sky.TogglePlayCommand.Execute(null);
                break;
            case Key.OemQuestion or Key.Divide when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                OnSkyFocusSearch(this, EventArgs.Empty);
                break;
            case Key.D1 or Key.NumPad1 when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.IsDomeMode = true;
                break;
            case Key.D2 or Key.NumPad2 when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.IsAllSkyMode = true;
                break;
            case Key.F when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.IsFovVisible = !_viewModel.Sky.IsFovVisible;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;

        var window = new SettingsWindow(_viewModel.Settings)
        {
            Owner = this
        };

        // Cancelling or closing with the X must not leave half-applied changes in memory.
        if (window.ShowDialog() != true)
        {
            _viewModel.Settings.RevertChanges();
        }
    }

    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The chart depends on the selected object, the time, the observer, the time zone,
        // the minimum altitude and the forecast: the detail bumps ChartVersion for all of them.
        if (e.PropertyName is nameof(ObjectDetailViewModel.ChartVersion) && _viewModel is not null)
        {
            UpdateChart(_viewModel.Detail);
        }
    }

    private void OnSkyFocusSearch(object? sender, EventArgs e)
    {
        Tabs.SelectedItem = SkyTab;
        Dispatcher.BeginInvoke(() =>
        {
            SkySearchBox.Focus();
            SkySearchBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnSkyOpenDetails(object? sender, EventArgs e) => Tabs.SelectedItem = DetailTab;

    /// <summary>Double-click on a best target: select it and show its detail (with the feasibility breakdown).</summary>
    private void OnBestTargetDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: CelestialObjectRowViewModel row } && DataContext is ShellViewModel shell && shell.Targets.SelectById(row.Id))
        {
            Tabs.SelectedItem = DetailTab;
        }
    }

    private void OnShowSkyRequested(object? sender, EventArgs e) => Tabs.SelectedItem = SkyTab;

    /// <summary>Saves what the renderer shows (e.g. a finder chart) as a PNG.</summary>
    private async void OnSkyExportClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || !SkyHostView.IsReady) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.SkyExportDialogTitle,
            Filter = Strings.PngFileFilter,
            FileName = _viewModel.Sky.SuggestedImageFileName()
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await SkyHostView.CapturePngAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            _logger.LogError("Could not export the sky view.", ex);
            MessageBox.Show(this, DisplayNames.Format(Strings.SkyExportFailedFormat, ex.Message), Strings.SkyExportDialogTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLocationsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LocationsViewModel.HorizonVersion) && _viewModel is not null)
        {
            UpdateHorizonPlot(_viewModel.Locations);
        }
    }

    /// <summary>Horizon profile preview: altitude against azimuth, the obstructed part filled.</summary>
    private void UpdateHorizonPlot(LocationsViewModel viewModel)
    {
        var plot = HorizonPlot.Plot;
        plot.Clear();
        plot.FigureBackground.Color = Theme("SurfaceBrush");
        plot.DataBackground.Color = Theme("NightPanelBrush");
        plot.Axes.Color(Theme("NightMutedTextBrush"));
        plot.Grid.MajorLineColor = Theme("NightBorderBrush");

        var points = viewModel.GetHorizonPreview();
        if (points.Count > 0)
        {
            var xs = points.Select(p => p.AzimuthDegrees).ToArray();
            var ys = points.Select(p => p.AltitudeDegrees).ToArray();
            var fill = plot.Add.FillY(xs, ys, xs.Select(_ => -10.0).ToArray());
            fill.FillColor = Theme("SkyHorizonBrush").WithAlpha(0.45);
            fill.LineColor = Theme("WarningBrush");
            fill.LineWidth = 1.5f;
        }

        plot.Axes.Bottom.Label.Text = Strings.HorizonPreviewAzimuth;
        plot.Axes.Left.Label.Text = Strings.HorizonPreviewAltitude;
        plot.Axes.SetLimits(0, 360, -10, Math.Max(30, points.Count > 0 ? points.Max(p => p.AltitudeDegrees) + 5 : 30));
        plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericFixedInterval(45);
        HorizonPlot.Refresh();
    }

    private void OnSkyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SkyViewModel.MapVersion) && _viewModel is not null)
        {
            UpdateSkyMap(_viewModel.Sky);
        }
    }

    /// <summary>
    /// Plan notes are saved when leaving the field; with a keyboard shortcut the focus does not
    /// leave, so the edit is committed by hand before exporting or printing.
    /// </summary>
    private static void CommitFocusedTextBox() =>
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private void OnExportPlanClicked(object sender, RoutedEventArgs e)
    {
        CommitFocusedTextBox();
        if (_viewModel is null || _viewModel.Plan.Items.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.PlanExportDialogTitle,
            Filter = Strings.CsvFileFilter,
            FileName = string.Create(CultureInfo.InvariantCulture, $"AstroPlannerPro-plan-{_viewModel.ObservationDate:yyyy-MM-dd}.csv")
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Plan.Export(dialog.FileName);
        }
    }

    private void OnExportPlanPdfClicked(object sender, RoutedEventArgs e) =>
        ExportPlan(Strings.PlanExportPdfDialogTitle, Strings.PdfFileFilter, "pdf", (plan, path) => plan.ExportPdf(path));

    private void OnExportPlanIcsClicked(object sender, RoutedEventArgs e) =>
        ExportPlan(Strings.PlanExportIcsDialogTitle, Strings.IcsFileFilter, "ics", (plan, path) => plan.ExportIcs(path));

    private void ExportPlan(string title, string filter, string extension, Func<SessionPlanViewModel, string, bool> export)
    {
        CommitFocusedTextBox();
        if (_viewModel is null || _viewModel.Plan.Items.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = string.Create(CultureInfo.InvariantCulture, $"AstroPlannerPro-plan-{_viewModel.ObservationDate:yyyy-MM-dd}.{extension}")
        };

        if (dialog.ShowDialog(this) == true)
        {
            export(_viewModel.Plan, dialog.FileName);
        }
    }

    private void OnLogObservationClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.Detail.CreateLogDraft() is not { } draft) return;

        var window = new LogEntryWindow(draft) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _viewModel.Logbook.Add(draft);
        }
    }

    private void OnExportLogbookClicked(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null || _viewModel.Logbook.Entries.Count == 0) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Strings.LogbookExportDialogTitle,
            Filter = Strings.CsvFileFilter,
            FileName = string.Create(CultureInfo.InvariantCulture, $"AstroPlannerPro-logbook-{DateTime.Now:yyyy-MM-dd}.csv")
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Logbook.Export(dialog.FileName);
        }
    }

    private void OnPrintPlanClicked(object sender, RoutedEventArgs e)
    {
        CommitFocusedTextBox();
        if (_viewModel is null || _viewModel.Plan.Items.Count == 0) return;

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;

        var document = PlanPrintDocument.Create(_viewModel.Plan.ChronologicalItems, _viewModel.Plan.SummaryText);
        document.PageWidth = dialog.PrintableAreaWidth;
        document.PageHeight = dialog.PrintableAreaHeight;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, Strings.PlanPrintJobName);
    }

    private void OnSkyPlotClicked(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel is null) return;

        // WPF pixels → ScottPlot pixels (display scale) → chart coordinates.
        var position = e.GetPosition(SkyPlot);
        var scale = SkyPlot.DisplayScale;
        var coordinates = SkyPlot.Plot.GetCoordinates(
            new ScottPlot.Pixel((float)(position.X * scale), (float)(position.Y * scale)));
        _viewModel.Sky.SelectNearestOnSkyMap(coordinates.X, coordinates.Y);
    }

    /// <summary>Theme color (Themes/Dark.xaml) for ScottPlot: the charts share the UI palette.</summary>
    private ScottPlot.Color Theme(string key) =>
        TryFindResource(key) is SolidColorBrush { Color: var c }
            ? new ScottPlot.Color(c.R, c.G, c.B, c.A)
            : ScottPlot.Colors.Magenta;   // misspelled key: visible at a glance

    /// <summary>
    /// Polar sky chart: altitude circles every 30°, cardinal points, objects above the horizon
    /// (size by magnitude), My plan, selection and the Moon.
    /// </summary>
    private void UpdateSkyMap(SkyViewModel viewModel)
    {
        var plot = SkyPlot.Plot;
        plot.Clear();
        plot.FigureBackground.Color = Theme("SurfaceBrush");
        plot.DataBackground.Color = Theme("SurfaceBrush");
        plot.Axes.Frameless();
        plot.HideGrid();

        var gridColor = Theme("NightBorderBrush");
        var horizon = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius);
        horizon.LineColor = Theme("SkyHorizonBrush");
        horizon.LineWidth = 2;
        horizon.FillColor = Theme("SkyFillBrush");

        foreach (var altitude in new[] { 30.0, 60.0 })
        {
            var ring = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius - altitude);
            ring.LineColor = gridColor;
            ring.LineWidth = 1;
        }

        // Minimum altitude: inside this circle an object counts as visible.
        var threshold = plot.Add.Circle(0, 0, SkyProjection.HorizonRadius - viewModel.MinimumAltitude);
        threshold.LineColor = Theme("WarningBrush").WithAlpha(0.6);
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
            text.LabelFontColor = Theme("LabelTextBrush");
            text.LabelFontSize = 16;
            text.LabelBold = true;
            text.LabelAlignment = ScottPlot.Alignment.MiddleCenter;
        }

        // The Moon goes first (underneath): its disc must not hide nearby objects.
        if (viewModel.Moon is { IsAboveHorizon: true } moon)
        {
            var (x, y) = SkyProjection.ToXY(moon.Horizontal.AltitudeDegrees, moon.Horizontal.AzimuthDegrees);
            plot.Add.Marker(x, y, ScottPlot.MarkerShape.FilledCircle, 22, Theme("MoonBrush").WithAlpha(0.85));
            var label = plot.Add.Text(DisplayNames.Format(Strings.MoonLabelFormat, viewModel.MoonIlluminationText), x, y - 6);
            label.LabelFontColor = Theme("MoonBrush");
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
                    Theme(point.IsInPlan ? "WarningBrush" : "PlanetBrush"));
            }
            else
            {
                var color = point.IsInPlan ? "WarningBrush" : point.IsVisible ? "AccentBrush" : "PlaceholderTextBrush";
                size = double.IsNaN(point.Magnitude) ? 4 : (float)Math.Clamp(13 - point.Magnitude, 4, 10);
                plot.Add.Marker(x, y, ScottPlot.MarkerShape.FilledCircle, size, Theme(color));
            }

            if (point.IsSelected)
            {
                plot.Add.Marker(x, y, ScottPlot.MarkerShape.OpenCircle, size + 12, ScottPlot.Colors.White);
            }

            if (!point.HasLabel)
            {
                continue;
            }

            var label = plot.Add.Text(point.Id, x + 2.5, y);
            var highlighted = point.IsInPlan || point.IsSelected || point.IsPlanet;
            label.LabelFontColor = Theme(point.IsPlanet ? "PlanetBrush" : highlighted ? "StrongTextBrush" : "SkyLabelBrush");
            label.LabelFontSize = highlighted ? 12 : 10;
            label.LabelAlignment = ScottPlot.Alignment.MiddleLeft;
        }

        // Full, undistorted circle: the short side of the chart spans ±100 and the long side
        // grows in proportion (SquareUnits used to crop the short one).
        var width = Math.Max(SkyPlot.ActualWidth, 1);
        var height = Math.Max(SkyPlot.ActualHeight, 1);
        const double halfSpan = 100;
        plot.Axes.SetLimits(
            -halfSpan * Math.Max(1, width / height),
            halfSpan * Math.Max(1, width / height),
            -halfSpan * Math.Max(1, height / width),
            halfSpan * Math.Max(1, height / width));
        SkyPlot.Refresh();
    }

    private void UpdateChart(ObjectDetailViewModel viewModel)
    {
        var points = viewModel.GetNightAltitudeCurve();

        // With invalid input the last curve is kept, like the table.
        if (points.Count == 0 && viewModel.InputError is not null)
        {
            return;
        }

        AltitudePlot.Plot.Clear();
        AltitudePlot.Plot.FigureBackground.Color = Theme("NightPanelBrush");
        AltitudePlot.Plot.DataBackground.Color = Theme("SurfaceBrush");
        AltitudePlot.Plot.Axes.Color(Theme("NightMutedTextBrush"));
        AltitudePlot.Plot.Grid.MajorLineColor = Theme("NightBorderBrush");
        AltitudePlot.Plot.Grid.MinorLineColor = Theme("ControlRaisedBrush");

        if (points.Count > 0)
        {
            // Background first (twilight and clouds) so that the curves are drawn on top.
            AddTwilightBands(viewModel.Night);
            AddCloudBands(viewModel);

            // X axis with real dates so that the day change shows 23:00, 00:00, 01:00, etc.
            var x = points.Select(p => p.LocalTime.ToOADate()).ToArray();
            var y = points.Select(p => p.AltitudeDegrees).ToArray();

            var moonPoints = viewModel.GetMoonAltitudeCurve();
            if (moonPoints.Count > 0)
            {
                var moonLine = AltitudePlot.Plot.Add.Scatter(
                    moonPoints.Select(p => p.LocalTime.ToOADate()).ToArray(),
                    moonPoints.Select(p => p.AltitudeDegrees).ToArray());
                moonLine.LegendText = DisplayNames.Format(Strings.MoonLegendFormat, viewModel.MoonIlluminationText);
                moonLine.Color = Theme("MoonBrush").WithAlpha(0.8);
                moonLine.LineWidth = 1.5f;
                moonLine.LinePattern = ScottPlot.LinePattern.Dashed;
                moonLine.MarkerSize = 0;
            }

            var selectedTime = viewModel.ObservationDateTime.ToOADate();
            if (selectedTime >= x[0] && selectedTime <= x[^1])
            {
                var nowLine = AltitudePlot.Plot.Add.VerticalLine(selectedTime);
                nowLine.LineColor = Theme("NightMutedTextBrush");
                nowLine.LineWidth = 1;
                nowLine.LinePattern = ScottPlot.LinePattern.Dotted;
                nowLine.LegendText = Strings.ChartChosenTime;
            }

            var altitudeLine = AltitudePlot.Plot.Add.Scatter(x, y);
            altitudeLine.LegendText = Strings.ChartAltitude;
            altitudeLine.Color = Theme("AccentBrush");
            altitudeLine.LineWidth = 2;
            var thresholdLine = AltitudePlot.Plot.Add.HorizontalLine(viewModel.MinimumAltitude);
            thresholdLine.LineColor = Theme("WarningBrush");
            thresholdLine.LineWidth = 2;
            thresholdLine.LegendText = Strings.ChartMinimumAltitude;

            AltitudePlot.Plot.Axes.Title.Label.Text =
                viewModel.SelectedObject?.Title ?? string.Empty;
            AltitudePlot.Plot.Axes.Bottom.Label.Text = Strings.ChartLocalTime;
            AltitudePlot.Plot.Axes.Left.Label.Text = Strings.ChartAltitudeAxis;
            AltitudePlot.Plot.ShowLegend();
            // Dark legend: with the default white background the Moon line is invisible.
            AltitudePlot.Plot.Legend.BackgroundColor = Theme("NightPanelBrush").WithAlpha(0.92);
            AltitudePlot.Plot.Legend.FontColor = Theme("NightTextBrush");
            AltitudePlot.Plot.Legend.OutlineColor = Theme("ControlBorderBrush");
            var timeTicks = new ScottPlot.TickGenerators.DateTimeFixedInterval(
                new ScottPlot.TickGenerators.TimeUnits.Hour(),
                2,
                null,
                0,
                dateTime => dateTime.Date.AddHours(18));
            timeTicks.LabelFormatter = dateTime => dateTime.ToString("HH:mm", CultureInfo.CurrentCulture);
            AltitudePlot.Plot.Axes.Bottom.TickGenerator = timeTicks;
            AltitudePlot.Plot.Axes.SetLimitsY(-90, 90);
            AltitudePlot.Plot.Axes.SetLimitsX(points[0].LocalTime.ToOADate(), points[^1].LocalTime.ToOADate());
        }

        AltitudePlot.Refresh();
    }

    /// <summary>
    /// Hourly forecast cloud cover: one grey band per hour, more opaque the cloudier it is.
    /// Nothing is drawn without a forecast.
    /// </summary>
    private void AddCloudBands(ObjectDetailViewModel viewModel)
    {
        var first = true;
        foreach (var (start, end, cloud) in viewModel.GetNightCloudCover())
        {
            if (cloud < 5 || end <= start)
            {
                continue;
            }

            var span = AltitudePlot.Plot.Add.HorizontalSpan(start.ToOADate(), end.ToOADate());
            span.FillStyle.Color = Theme("ChartCloudBrush").WithAlpha(cloud / 100.0 * 0.45);
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
    private void AddTwilightBands(Core.Models.NightInfo? night)
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
            var span = AltitudePlot.Plot.Add.HorizontalSpan(from.ToOADate(), to.ToOADate());
            span.FillStyle.Color = Theme("ChartTwilightBrush").WithAlpha(0.35);
            span.LineStyle.Width = 0;
            if (first)
            {
                span.LegendText = Strings.ChartTwilight;
                first = false;
            }
        }
    }

    /// <summary>
    /// Session timeline (spec §4.6): one row per plan item in priority order, the usable window as a
    /// thin bar, the scheduled block coloured by verdict (thick outline = locked), meridian flips as
    /// dashed ticks, over the twilight and cloud bands.
    /// </summary>
    private void UpdateTimeline(PlanTimeline? timeline)
    {
        var plot = TimelinePlot.Plot;
        plot.Clear();
        plot.FigureBackground.Color = Theme("NightPanelBrush");
        plot.DataBackground.Color = Theme("SurfaceBrush");
        plot.Axes.Color(Theme("NightMutedTextBrush"));
        plot.Grid.MajorLineColor = Theme("NightBorderBrush");
        plot.Grid.YAxisStyle.MajorLineStyle.Width = 0;
        if (timeline is null || timeline.Rows.Count == 0)
        {
            TimelinePlot.Refresh();
            return;
        }

        var count = timeline.Rows.Count;
        foreach (var band in timeline.TwilightBands)
        {
            var span = plot.Add.HorizontalSpan(band.Start.ToOADate(), band.End.ToOADate());
            span.FillStyle.Color = Theme("ChartTwilightBrush").WithAlpha(0.35);
            span.LineStyle.Width = 0;
        }

        foreach (var (cloud, percent) in timeline.Clouds)
        {
            if (percent < 5) continue;
            var span = plot.Add.HorizontalSpan(cloud.Start.ToOADate(), cloud.End.ToOADate());
            span.FillStyle.Color = Theme("ChartCloudBrush").WithAlpha(percent / 100.0 * 0.45);
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
                bar.FillStyle.Color = Theme("NightMutedTextBrush").WithAlpha(0.45);
                bar.LineStyle.Width = 0;
            }

            if (row.Block is { } block)
            {
                var rectangle = plot.Add.Rectangle(block.Start.ToOADate(), block.End.ToOADate(), y - 0.32, y + 0.32);
                rectangle.FillStyle.Color = VerdictColor(row.Verdict).WithAlpha(0.85);
                rectangle.LineStyle.Color = Theme(row.Locked ? "NightTextBrush" : "NightBorderBrush");
                rectangle.LineStyle.Width = row.Locked ? 2.5f : 1;
            }

            if (row.MeridianFlipLocal is { } flip)
            {
                var line = plot.Add.Line(flip.ToOADate(), y - 0.42, flip.ToOADate(), y + 0.42);
                line.LineColor = Theme("NightTextBrush");
                line.LineWidth = 2;
                line.LinePattern = ScottPlot.LinePattern.Dashed;
                line.MarkerSize = 0;
            }

            // Sunlit satellites crossing the field: a short tick above the block.
            foreach (var crossing in row.CrossingsLocal ?? [])
            {
                var tick = plot.Add.Line(crossing.ToOADate(), y + 0.18, crossing.ToOADate(), y + 0.46);
                tick.LineColor = Theme("DangerBrush");
                tick.LineWidth = 2;
                tick.MarkerSize = 0;
            }
        }

        DateTime? now = _viewModel?.ObservationDateTime;
        if (now is { } chosen && chosen >= timeline.WindowStartLocal && chosen <= timeline.WindowEndLocal)
        {
            var nowLine = plot.Add.VerticalLine(chosen.ToOADate());
            nowLine.LineColor = Theme("NightMutedTextBrush");
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
        TimelinePlot.Refresh();

        ScottPlot.Color VerdictColor(Core.Imaging.Feasibility.FeasibilityVerdict? verdict) => verdict switch
        {
            Core.Imaging.Feasibility.FeasibilityVerdict.Excellent => Theme("SuccessBrush"),
            Core.Imaging.Feasibility.FeasibilityVerdict.Good => Theme("SuccessSoftBrush"),
            Core.Imaging.Feasibility.FeasibilityVerdict.Marginal => Theme("WarningBrush"),
            Core.Imaging.Feasibility.FeasibilityVerdict.Unlikely => Theme("DangerBrush"),
            _ => Theme("PlaceholderTextBrush")
        };
    }
}
