using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The app-channel endpoints of one area of the web UI (one window), over the same view models
/// and state services as the classic window. Every area follows one pattern (docs/bridge.md):
/// <list type="bullet">
/// <item><c>x.get</c> returns an immutable snapshot (a contract record) with a revision;</item>
/// <item>commands are methods that call the view models;</item>
/// <item>one <c>x.changed {revision}</c> event, coalesced by a <see cref="ChangeFeed"/>, tells
/// the page to fetch the snapshot again.</item>
/// </list>
/// Disposing unsubscribes from the view models and stops the events.
/// </summary>
public interface IEndpointModule : IDisposable
{
    /// <summary>Registers the methods and starts publishing the events.</summary>
    void Register(RpcDispatcher rpc);
}
