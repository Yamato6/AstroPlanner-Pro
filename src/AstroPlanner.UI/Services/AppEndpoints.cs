using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.UI.Services;

/// <summary>
/// What the planetarium UI can ask the host (bridge v2, docs/bridge.md): one dispatcher and the
/// endpoint modules of every area, wired to the same view models as the classic window. The
/// modules live in AstroPlanner.Application; the windows of R1–R4 add theirs here.
/// </summary>
public sealed class AppEndpoints
{
    private readonly IEndpointModule[] _modules;

    /// <param name="host">What only the window can do (full screen, data folder, browser).</param>
    /// <param name="store">The planetarium UI's window layout and preferences.</param>
    /// <param name="ui">The view models' thread; defaults to the calling (UI) thread.</param>
    /// <param name="time">Clock of the change-event coalescing; defaults to the system clock.</param>
    public AppEndpoints(ShellViewModel shell, IAppHost host, IInterfaceStore store, Action<string> send, IAppLogger logger,
        IUiDispatcher? ui = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
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

        ui ??= SynchronizationContextUiDispatcher.ForCurrentThread();
        time ??= TimeProvider.System;
        var selection = new SelectionEndpoints(shell, ui, time);
        _modules =
        [
            new AppInfoEndpoints(shell.Settings.Theme, AppBuild.Version, host),
            new ObserverEndpoints(shell.Observer, ui, time),
            new SkyEndpoints(shell, ui, time),
            new NightEndpoints(shell.Observer, shell.Weather, ui, time),
            selection,
            new SearchEndpoints(shell, selection),
            new InterfaceEndpoints(shell.Settings, store, host, logger, ui, time),
            new LayoutEndpoints(store, logger),
            new LandscapeEndpoints(shell.Locations, shell.Observer, shell.Sky.Media, ui, time),
        ];
        foreach (var module in _modules)
        {
            module.Register(Dispatcher);
        }
    }

    public RpcDispatcher Dispatcher { get; }

    /// <summary>The window closed: stop pushing events and cancel the requests still running.</summary>
    public void Stop()
    {
        foreach (var module in _modules)
        {
            module.Dispose();
        }

        Dispatcher.CancelAll();
    }
}
