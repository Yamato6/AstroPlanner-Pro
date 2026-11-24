using System.Text.Json;
using AstroPlanner.SkyBridge.App;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.Tests;

/// <summary>The planetarium UI's endpoints answer from the same state as the classic window.</summary>
public sealed class AppEndpointsTests
{
    [Fact]
    public async Task AppInfo_ReturnsTheVersionAndTheCurrentTheme()
    {
        var shell = TestShell.Create();
        var sent = new List<string>();
        var endpoints = new AppEndpoints(shell, sent.Add, new NullLogger());

        await endpoints.Dispatcher.HandleAsync("""{"v":2,"kind":"req","id":1,"type":"app.info","payload":{}}""");

        var payload = JsonElement.Parse(sent.Single()).GetProperty("payload");
        payload.GetProperty("version").GetString().Should().Be(AppBuild.Version.ToString());
        payload.GetProperty("theme").GetString().Should().Be("dark");
        payload.GetProperty("appProtocolVersion").GetInt32().Should().Be(AppMessageCodec.ProtocolVersion);
    }

    [Fact]
    public void ThemeChanges_ArePushedAsEvents_UntilStopped()
    {
        var shell = TestShell.Create();
        var sent = new List<string>();
        var endpoints = new AppEndpoints(shell, sent.Add, new NullLogger());

        shell.IsNightTheme = true;
        endpoints.Stop();
        shell.IsNightTheme = false;

        var message = JsonElement.Parse(sent.Single());
        message.GetProperty("type").GetString().Should().Be("theme.changed");
        message.GetProperty("payload").GetProperty("theme").GetString().Should().Be("night");
    }

    [Fact]
    public async Task EveryMethodOfTheChannel_IsRegistered()
    {
        var shell = TestShell.Create();
        var sent = new List<string>();
        var endpoints = new AppEndpoints(shell, sent.Add, new NullLogger());

        foreach (var method in AppMethods.All)
        {
            sent.Clear();
            // Empty parameters: methods that take some answer bad-request, which still proves a handler is there.
            await endpoints.Dispatcher.HandleAsync($$$"""{"v":2,"kind":"req","id":1,"type":"{{{method.Name}}}","payload":{}}""");

            var response = JsonElement.Parse(sent.Single());
            var code = response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;
            code.Should().NotBe(RpcErrorCodes.UnknownMethod, $"{method.Name} must be registered");
            code.Should().NotBe(RpcErrorCodes.Internal, $"{method.Name} must not fail");
        }

        endpoints.Stop();
    }
}
