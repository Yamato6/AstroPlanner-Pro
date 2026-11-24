using System.Globalization;
using System.IO;
using System.Windows.Controls;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AstroPlanner.UI.Sky;

/// <summary>
/// Hosts the sky renderer in WebView2 (spec §5.4) and is the real <see cref="ISkyChannel"/>.
/// Security: only the two local virtual hosts can be loaded; any other navigation or new window
/// is cancelled (links on the SafeLinks allow-list open in the browser); dev tools, context
/// menus, script dialogs, zoom, autofill, browser shortcuts and host objects are off; messages
/// are accepted only from the app host and validated by <see cref="SkyMessageCodec"/>.
/// </summary>
public sealed class SkyHost : Border, ISkyChannel, IDisposable
{
    public const string AppHost = SkyHosts.AppHost;
    public const string DataHost = SkyHosts.DataHost;
    public static readonly Uri AppOrigin = new($"https://{AppHost}/");

    private readonly WebView2 _webView = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(5, 11, 22) };
    private IAppLogger? _logger;
    private bool _started;

    public SkyHost()
    {
        Child = _webView;
    }

    public event EventHandler<IncomingSkyMessage>? MessageReceived;

    /// <summary>
    /// A message of the app channel (bridge v2, <c>v: 2</c>) from the page, as raw JSON: the
    /// <see cref="AstroPlanner.SkyBridge.App.RpcDispatcher"/> validates it. Only the app origin reaches it.
    /// </summary>
    public event EventHandler<string>? AppMessageReceived;

    /// <summary>Raised (on the UI thread) when the renderer cannot run: no WebView2 runtime or no embedded renderer.</summary>
    public event EventHandler<string>? Unavailable;

    public bool IsReady => _webView.CoreWebView2 is not null;

    /// <summary>
    /// Starts WebView2 and loads <paramref name="page"/>. <paramref name="query"/> is appended to
    /// the page URL (benchmark switches); it never contains user data.
    /// </summary>
    public async Task StartAsync(IAppLogger logger, string query = "", SkyPage page = SkyPage.Classic)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _logger = logger;

        string runtimeVersion;
        try
        {
            runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            logger.LogError("WebView2 runtime not found; the sky view falls back to the 2D chart.", ex);
            Unavailable?.Invoke(this, "runtime");
            return;
        }

        SkyAssetFolders? folders;
        try
        {
            await using var zip = SkyAssets.OpenEmbedded();
            folders = zip is null ? null : await Task.Run(() => SkyAssets.Extract(zip, AppPaths.DataFile("sky"))).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            logger.LogError("Could not extract the sky renderer.", ex);
            folders = null;
        }

        if (folders is null)
        {
            logger.LogInfo("The sky renderer is not embedded in this build; using the 2D chart.");
            Unavailable?.Invoke(this, "assets");
            return;
        }

        try
        {
            // The benchmark measures the renderer, not the monitor: no vsync, no frame cap.
            var options = query.Contains("bench=1", StringComparison.Ordinal)
                ? new CoreWebView2EnvironmentOptions("--disable-gpu-vsync --disable-frame-rate-limit")
                : null;
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppPaths.DataFile("webview2"),
                options).ConfigureAwait(true);
            await _webView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            logger.LogError("WebView2 could not start; the sky view falls back to the 2D chart.", ex);
            Unavailable?.Invoke(this, "runtime");
            return;
        }

        var core = _webView.CoreWebView2;
        Configure(core.Settings);
        core.SetVirtualHostNameToFolderMapping(AppHost, folders.AppDirectory, CoreWebView2HostResourceAccessKind.DenyCors);
        // The page (app host) fetches the data host: cross-origin reads must be allowed for it.
        core.SetVirtualHostNameToFolderMapping(DataHost, folders.DataDirectory, CoreWebView2HostResourceAccessKind.Allow);
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += (_, e) => logger.LogError(string.Create(CultureInfo.InvariantCulture,
            $"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason})."));

        logger.LogInfo($"Sky renderer: WebView2 {runtimeVersion}.");
        core.Navigate(PageUri(page, query).ToString());
    }

    /// <summary>
    /// The page to load: index.html (the v2 Sky tab: the sky alone) or planetarium.html (the v3
    /// React UI over the full-window sky, opened with <c>--ui=web</c>). Both come from src/web.
    /// </summary>
    internal static Uri PageUri(SkyPage page, string query) => new(AppOrigin, page switch
    {
        SkyPage.Planetarium => "planetarium.html",
        _ => "index.html",
    } + query);

    private static void Configure(CoreWebView2Settings settings)
    {
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsWebMessageEnabled = true;
        settings.IsScriptEnabled = true;
    }

    /// <summary>True for the app's own pages; everything else is blocked.</summary>
    internal static bool IsAllowedNavigation(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
        && parsed.Scheme == Uri.UriSchemeHttps
        && string.Equals(parsed.Host, AppHost, StringComparison.OrdinalIgnoreCase)
        && parsed.IsDefaultPort;

    /// <summary>True when a web message comes from the app host.</summary>
    internal static bool IsTrustedSource(string? source) => IsAllowedNavigation(source);

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!IsAllowedNavigation(e.Uri))
        {
            e.Cancel = true;
            _logger?.LogInfo("Blocked a navigation out of the sky renderer.");
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && SafeLinks.IsAllowed(uri))
        {
            SafeLinks.Open(uri);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrustedSource(e.Source))
        {
            return;
        }

        var json = e.WebMessageAsJson;
        if (AstroPlanner.SkyBridge.App.AppMessageCodec.IsAppMessage(json))
        {
            AppMessageReceived?.Invoke(this, json);
            return;
        }

        var message = SkyMessageCodec.Decode(json, out var error);
        if (message is null)
        {
            _logger?.LogInfo("Ignored a sky renderer message: " + error);
            return;
        }

        MessageReceived?.Invoke(this, message);
    }

    public void Post(string json)
    {
        _webView.CoreWebView2?.PostWebMessageAsJson(json);
    }

    /// <summary>Saves what the renderer shows as a PNG file.</summary>
    public async Task CapturePngAsync(string path)
    {
        var core = _webView.CoreWebView2 ?? throw new InvalidOperationException("The sky renderer is not running.");
        await using var file = File.Create(path);
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file).ConfigureAwait(true);
    }

    public void Dispose() => _webView.Dispose();
}
