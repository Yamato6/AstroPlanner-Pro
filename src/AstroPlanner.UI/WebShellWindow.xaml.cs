using System.Windows;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI;

/// <summary>
/// The v3 planetarium window (<c>--ui=web</c>, in development until v3.0.0): the web page draws
/// everything over the full-window sky. It drives the same <see cref="ShellViewModel"/> as the v2
/// window, so the sky shows the same site, time and selection.
/// </summary>
public partial class WebShellWindow : Window
{
    private readonly IAppLogger _logger;

    public WebShellWindow(IAppLogger logger)
    {
        _logger = logger;
        InitializeComponent();
        NativeWindows.UseDarkTitleBar(this);
        SkyHostView.Unavailable += OnSkyUnavailable;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel) return;

        await viewModel.Settings.LoadCitiesCommand.ExecuteAsync(null);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Sky.Attach(SkyHostView);
        await SkyHostView.StartAsync(_logger, SkyLaunchOptions.Query(Environment.GetCommandLineArgs()), SkyPage.Planetarium);
    }

    /// <summary>Without WebView2 (or without the embedded pages) there is nothing to show.</summary>
    private void OnSkyUnavailable(object? sender, string reason)
    {
        _logger.LogInfo($"The planetarium UI cannot start ({reason}).");
        MessageBox.Show(this, Strings.WebUiUnavailable, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    }
}
