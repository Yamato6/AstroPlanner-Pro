using AstroPlanner.Application.Services;
using AstroPlanner.SkyBridge;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary><c>app.info</c> (versions, theme) and <c>theme.changed</c>.</summary>
public sealed class AppInfoEndpoints : IEndpointModule
{
    private readonly ThemeState _theme;
    private readonly AppVersion _version;
    private RpcDispatcher? _rpc;

    /// <param name="version">The executable's version (the host knows it, not this library).</param>
    public AppInfoEndpoints(ThemeState theme, AppVersion version)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(version);
        _theme = theme;
        _version = version;
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.AppInfo, (_, _) => Task.FromResult(new AppInfoResponse(
            _version.ToString(), _theme.SkyTheme, SkyMessageCodec.ProtocolVersion, AppMessageCodec.ProtocolVersion)));
        _theme.Changed += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) =>
        _rpc?.Publish(AppEvents.ThemeChanged, new ThemeChangedEvent(_theme.SkyTheme));

    public void Dispose()
    {
        _theme.Changed -= OnThemeChanged;
        _rpc = null;
    }
}
