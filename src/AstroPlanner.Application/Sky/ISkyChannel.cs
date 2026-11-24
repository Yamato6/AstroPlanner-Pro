using AstroPlanner.SkyBridge;

namespace AstroPlanner.Application.Sky;

/// <summary>
/// Message channel to the sky renderer. The WebView2 host implements it; tests use a fake. Every
/// incoming message is already decoded and validated (<see cref="SkyMessageCodec.Decode"/>).
/// </summary>
public interface ISkyChannel
{
    event EventHandler<IncomingSkyMessage>? MessageReceived;

    /// <summary>Sends an encoded message (see <see cref="SkyMessageCodec"/>).</summary>
    void Post(string json);
}
