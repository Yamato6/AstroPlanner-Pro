using System.Globalization;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using AstroPlanner.UI.Charts;
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
            if (_viewModel is not null) UpdateSkyMap(_viewModel.Sky.Map);
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
            _viewModel.Sky.Map.PropertyChanged -= OnSkyMapPropertyChanged;
            _viewModel.Sky.FocusSearchRequested -= OnSkyFocusSearch;
            _viewModel.Sky.OpenDetailsRequested -= OnSkyOpenDetails;
            _viewModel.Sky.Modes.ShowSkyRequested -= OnShowSkyRequested;
            _viewModel.Locations.PropertyChanged -= OnLocationsPropertyChanged;
            _viewModel.Plan.TimelineChanged -= OnPlanTimelineChanged;
            _viewModel.Settings.Theme.Changed -= OnThemeChanged;
            _viewModel.Logbook.Changed -= OnLogbookChanged;
        }

        _viewModel = e.NewValue as ShellViewModel;

        if (_viewModel is not null)
        {
            _viewModel.Detail.PropertyChanged += OnDetailPropertyChanged;
            _viewModel.Sky.Map.PropertyChanged += OnSkyMapPropertyChanged;
            _viewModel.Sky.FocusSearchRequested += OnSkyFocusSearch;
            _viewModel.Sky.OpenDetailsRequested += OnSkyOpenDetails;
            _viewModel.Sky.Modes.ShowSkyRequested += OnShowSkyRequested;
            _viewModel.Locations.PropertyChanged += OnLocationsPropertyChanged;
            _viewModel.Plan.TimelineChanged += OnPlanTimelineChanged;
            _viewModel.Settings.Theme.Changed += OnThemeChanged;
            _viewModel.Logbook.Changed += OnLogbookChanged;
        }
    }

    /// <summary>The charts read their colors when drawn: redraw them with the new theme's.</summary>
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null) return;
        UpdateChart(_viewModel.Detail);
        UpdateSkyMap(_viewModel.Sky.Map);
        UpdateHorizonPlot(_viewModel.Locations);
        UpdateTimeline(_viewModel.Plan.Timeline);
        UpdatePredictionPlot(_viewModel.Logbook);
    }

    private void OnLogbookChanged() => Dispatcher.BeginInvoke(() =>
    {
        if (_viewModel is not null) UpdatePredictionPlot(_viewModel.Logbook);
    });

    private void OnPlanTimelineChanged(object? sender, EventArgs e) => UpdateTimeline(_viewModel?.Plan.Timeline);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        await _viewModel.Settings.LoadCitiesCommand.ExecuteAsync(null);
        await _viewModel.LoadCommand.ExecuteAsync(null);
        UpdateChart(_viewModel.Detail);
        UpdateSkyMap(_viewModel.Sky.Map);
        PredictionPlot.UserInputProcessor.IsEnabled = false;
        UpdatePredictionPlot(_viewModel.Logbook);
        if (SkyQuery().Length > 0)
        {
            Tabs.SelectedItem = SkyTab;
        }

        await _viewModel.CheckForUpdatesAsync(AppBuild.Version);
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
    /// Keyboard shortcuts that open dialogs or move the focus: Ctrl+F search, Ctrl+, settings,
    /// Ctrl+P print the plan, Ctrl+E export the plan, F1 about, and the sky keys. F5 (now) and
    /// Ctrl+Shift+N (night vision) are key bindings to view-model commands in the XAML.
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
                _viewModel?.Sky.Time.TogglePlayCommand.Execute(null);
                break;
            case Key.OemQuestion or Key.Divide when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                OnSkyFocusSearch(this, EventArgs.Empty);
                break;
            case Key.D1 or Key.NumPad1 when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.Modes.IsDomeMode = true;
                break;
            case Key.D2 or Key.NumPad2 when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.Modes.IsAllSkyMode = true;
                break;
            case Key.F when none && Tabs.SelectedItem == SkyTab && Keyboard.FocusedElement is not TextBox:
                if (_viewModel is not null) _viewModel.Sky.Camera.IsFovVisible = !_viewModel.Sky.Camera.IsFovVisible;
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
            FileName = _viewModel.Sky.Modes.SuggestedImageFileName()
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


    private void OnSkyMapPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SkyMapViewModel.MapVersion) && _viewModel is not null)
        {
            UpdateSkyMap(_viewModel.Sky.Map);
        }
    }

    /// <summary>
    /// Plan notes are saved when leaving the field; with a keyboard shortcut the focus does not
    /// leave, so the edit is committed by hand before exporting or printing.
    /// </summary>
    private static void CommitFocusedTextBox() =>
        (Keyboard.FocusedElement as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

    private void OnExportPlanClicked(object sender, RoutedEventArgs e) =>
        ExportPlan(Strings.PlanExportDialogTitle, Strings.CsvFileFilter, "csv", (plan, path) => plan.Export(path));

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
            FileName = _viewModel.Plan.SuggestedFileName(extension)
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
            FileName = LogbookViewModel.SuggestedExportFileName(DateTime.Now)
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
        _viewModel.Sky.Map.SelectNearestOnSkyMap(coordinates.X, coordinates.Y);
    }

    // ─── Charts (drawn by Charts/*; the colors come from the current theme) ───────────────

    private ChartPalette Palette => new(this);

    private void UpdateSkyMap(SkyMapViewModel map) => SkyMapChart.Draw(SkyPlot, map, Palette);

    private void UpdateChart(ObjectDetailViewModel detail) => AltitudeChart.Draw(AltitudePlot, detail, Palette);

    private void UpdateTimeline(PlanTimeline? timeline) =>
        PlanTimelineChart.Draw(TimelinePlot, timeline, _viewModel?.ObservationDateTime, Palette);

    private void UpdateHorizonPlot(LocationsViewModel locations) => HorizonChart.Draw(HorizonPlot, locations, Palette);

    private void UpdatePredictionPlot(LogbookViewModel logbook) => PredictionChart.Draw(PredictionPlot, logbook, Palette);
}
