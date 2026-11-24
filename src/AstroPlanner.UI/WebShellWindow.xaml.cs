using System.Windows;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.UI;

/// <summary>
/// The v3 planetarium window (<c>--ui=web</c>, in development until v3.0.0): the web page draws
/// everything over the full-window sky. It drives the same <see cref="ShellViewModel"/> as the v2
/// window, so the sky shows the same site, time and selection.
/// </summary>
public partial class WebShellWindow : Window
{
    private readonly IAppLogger _logger;
    private AppEndpoints? _endpoints;

    public WebShellWindow(IAppLogger logger)
    {
        _logger = logger;
        InitializeComponent();
        NativeWindows.UseDarkTitleBar(this);
        SkyHostView.Unavailable += OnSkyUnavailable;
        SkyHostView.AppMessageReceived += OnAppMessage;
        Loaded += OnLoaded;
        // The page is the whole window: the keyboard belongs to it whenever the window is active.
        Activated += (_, _) => SkyHostView.FocusPage();
        Closed += (_, _) => _endpoints?.Stop();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ShellViewModel viewModel) return;

        await viewModel.Settings.LoadCitiesCommand.ExecuteAsync(null);
        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Sky.Attach(SkyHostView);
        // The app channel (bridge v2): requests and events of the planetarium UI.
        _endpoints = new AppEndpoints(viewModel, SkyHostView.Post, _logger);
        await SkyHostView.StartAsync(_logger, SkyLaunchOptions.Query(Environment.GetCommandLineArgs()), SkyPage.Planetarium);
        SkyHostView.FocusPage();
    }

    /// <summary>A message of the app channel; whatever happens, the page cannot take the window down.</summary>
    private async void OnAppMessage(object? sender, string json)
    {
        try
        {
            if (_endpoints is not null)
            {
                await _endpoints.Dispatcher.HandleAsync(json);
            }
        }
#pragma warning disable CA1031 // async void event handler: an escaping exception would end the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError("An app message could not be handled.", ex);
        }
    }

    /// <summary>Without WebView2 (or without the embedded pages) there is nothing to show.</summary>
    private void OnSkyUnavailable(object? sender, string reason)
    {
        _logger.LogInfo($"The planetarium UI cannot start ({reason}).");
        MessageBox.Show(this, Strings.WebUiUnavailable, Strings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        Close();
    }
}
