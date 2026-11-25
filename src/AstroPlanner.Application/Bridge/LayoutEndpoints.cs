using AstroPlanner.Application.Services;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// <c>layout.load</c> / <c>layout.save</c>: the planetarium UI's floating windows, restored at the
/// next start (spec §3.3). Only the page changes the layout, so there is no change event.
/// </summary>
public sealed class LayoutEndpoints(IInterfaceStore store, IAppLogger logger) : IEndpointModule
{
    private readonly IInterfaceStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IAppLogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        rpc.Register(AppMethods.LayoutLoad, (_, _) => Task.FromResult(Load()));
        rpc.Register(AppMethods.LayoutSave, (request, _) =>
        {
            Save(request);
            return Task.FromResult(new NoParameters());
        });
    }

    /// <summary>The saved layout; rows of windows this version does not have are left out.</summary>
    public LayoutSnapshot Load()
    {
        try
        {
            return new LayoutSnapshot([.. _store.LoadLayouts().Where(l => KnownIds.Contains(l.Id)).Take(32).Select(ToContract)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            // The windows open where they open by default.
            _logger.LogError("Could not read the window layout.", ex);
            return new LayoutSnapshot([]);
        }
    }

    public void Save(LayoutSnapshot layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        try
        {
            _store.SaveLayouts([.. layout.Windows.Select(FromContract)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            // Losing a layout is not worth an error in the page: the next save tries again.
            _logger.LogError("Could not save the window layout.", ex);
        }
    }

    private static readonly HashSet<string> KnownIds =
    [
        WindowIds.Tonight, WindowIds.Targets, WindowIds.Plan, WindowIds.Logbook, WindowIds.Equipment,
        WindowIds.Sites, WindowIds.MilkyWay, WindowIds.Settings, WindowIds.Help,
    ];

    private static WindowLayoutState ToContract(WindowLayout layout) => new(
        layout.Id,
        layout.IsOpen,
        layout.IsMinimized,
        layout.Arrangement switch
        {
            WindowArrangement.Maximized => WindowArrangements.Maximized,
            WindowArrangement.DockLeft => WindowArrangements.DockLeft,
            WindowArrangement.DockRight => WindowArrangements.DockRight,
            _ => WindowArrangements.Floating,
        },
        Math.Clamp(layout.X, -20000, 20000),
        Math.Clamp(layout.Y, -20000, 20000),
        Math.Clamp(layout.Width, 1, 20000),
        Math.Clamp(layout.Height, 1, 20000),
        Math.Clamp(layout.DockWidth, 1, 20000),
        Math.Clamp(layout.ZOrder, 0, 10000));

    private static WindowLayout FromContract(WindowLayoutState state) => new(
        state.Id,
        state.IsOpen,
        state.IsMinimized,
        state.Arrangement switch
        {
            WindowArrangements.Maximized => WindowArrangement.Maximized,
            WindowArrangements.DockLeft => WindowArrangement.DockLeft,
            WindowArrangements.DockRight => WindowArrangement.DockRight,
            _ => WindowArrangement.Floating,
        },
        state.X,
        state.Y,
        state.Width,
        state.Height,
        state.DockWidth,
        state.ZOrder);

    public void Dispose()
    {
    }
}
