using AstroPlanner.SkyBridge;
using AstroPlanner.SkyBridge.App;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Services;

/// <summary>
/// What the planetarium UI can ask the host (bridge v2, docs/bridge.md): the methods it may call
/// and the events it receives, wired to the same view models as the classic window. R0b exposes
/// <c>app.info</c> and <c>theme.changed</c>; the windows of R1–R4 add their own methods here.
/// </summary>
public sealed class AppEndpoints
{
    private readonly ThemeState _theme;

    public AppEndpoints(ShellViewModel shell, Action<string> send, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(logger);
        _theme = shell.Settings.Theme;
        Dispatcher = new RpcDispatcher(send, (message, exception) =>
        {
            if (exception is null)
            {
                logger.LogInfo(message);
            }
            else
            {
                logger.LogError(message, exception);
            }
        });

        Dispatcher.Register(AppMethods.AppInfo, (_, _) => Task.FromResult(new AppInfoResponse(
            AppBuild.Version.ToString(), _theme.SkyTheme, SkyMessageCodec.ProtocolVersion, AppMessageCodec.ProtocolVersion)));
        _theme.Changed += OnThemeChanged;
    }

    public RpcDispatcher Dispatcher { get; }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        Dispatcher.Publish(AppEvents.ThemeChanged, new ThemeChangedEvent(_theme.SkyTheme));

    /// <summary>The window closed: stop pushing events and cancel the requests still running.</summary>
    public void Stop()
    {
        _theme.Changed -= OnThemeChanged;
        Dispatcher.CancelAll();
    }
}
