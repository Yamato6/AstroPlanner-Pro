using System.Windows;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;

namespace AstroPlanner.UI;

/// <summary>
/// The v3 planetarium window (<c>--ui=web</c>, in development until v3.0.0): the web page draws
/// everything over the full-window sky. It drives the same <see cref="ShellViewModel"/> as the v2
/// window, so the sky shows the same site, time and selection. It is also the page's
/// <see cref="IAppHost"/>: full screen (F11), the data folder and the browser.
/// </summary>
public partial class WebShellWindow : Window, IAppHost
{
    private readonly IAppLogger _logger;
    private readonly IInterfaceStore _interfaceStore;
    private readonly ISurveyTileSource _tiles;
    private readonly ISizedCache _tileCache;
    private AppEndpoints? _endpoints;
    private FrameState? _beforeFullScreen;

    public WebShellWindow(IAppLogger logger, IInterfaceStore interfaceStore, ISurveyTileSource tiles, ISizedCache tileCache)
    {
        _logger = logger;
        _interfaceStore = interfaceStore;
        _tiles = tiles;
        _tileCache = tileCache;
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
        _endpoints = new AppEndpoints(viewModel, this, _interfaceStore, SkyHostView.Post, _logger, tiles: _tiles, tileCache: _tileCache);
        // Survey tiles reach the page only through the host (https://tiles.astroplanner/, spec §5.4).
        SkyHostView.TileProxy = new SurveyTileProxy(_tiles, viewModel.Online);
        await SkyHostView.StartAsync(_logger, SkyLaunchOptions.Query(Environment.GetCommandLineArgs()), SkyPage.Planetarium);
        SkyHostView.FocusPage();
    }

    public bool IsFullScreen => _beforeFullScreen is not null;

    public event EventHandler? FullScreenChanged;

    /// <summary>Full screen = no frame, maximised over the taskbar; leaving it restores the frame and the state.</summary>
    public void SetFullScreen(bool fullScreen)
    {
        if (fullScreen == IsFullScreen)
        {
            return;
        }

        if (fullScreen)
        {
            _beforeFullScreen = new FrameState(WindowStyle, ResizeMode, WindowState);
            // A maximised window keeps the taskbar visible: go through Normal so the frameless one covers it.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            var before = _beforeFullScreen!;
            _beforeFullScreen = null;
            WindowState = WindowState.Normal;
            WindowStyle = before.Style;
            ResizeMode = before.ResizeMode;
            WindowState = before.State;
            NativeWindows.UseDarkTitleBar(this);
        }

        FullScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    public string DataFolder => AppPaths.DataDirectory;

    public void OpenDataFolder() => SafeLinks.OpenFolder(AppPaths.DataDirectory);

    public bool OpenLink(Uri link) => SafeLinks.Open(link);

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

    private sealed record FrameState(WindowStyle Style, ResizeMode ResizeMode, WindowState State);
}
