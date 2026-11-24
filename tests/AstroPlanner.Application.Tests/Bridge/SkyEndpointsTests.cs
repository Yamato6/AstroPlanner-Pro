using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>The <c>sky.*</c> methods drive the same view models as the classic Sky tab.</summary>
public sealed class SkyEndpointsTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _eventClock = new(WinterNightUtc);
    private readonly List<string> _sent = [];
    private int _nextId = 1;

    private async Task<(ShellViewModel Shell, SkyEndpoints Endpoints, RpcDispatcher Rpc)> CreateAsync()
    {
        var shell = TestShell.Create(time: new ManualTimeProvider(WinterNightUtc));
        await shell.LoadCommand.ExecuteAsync(null);
        var endpoints = new SkyEndpoints(shell, new SynchronizationContextUiDispatcher(null), _eventClock);
        var rpc = new RpcDispatcher(_sent.Add, (_, _) => { });
        endpoints.Register(rpc);
        return (shell, endpoints, rpc);
    }

    /// <summary>Calls a method and returns its answer's payload (or its error).</summary>
    private async Task<JsonElement> CallAsync(RpcDispatcher rpc, string method, string payload = "{}")
    {
        _sent.Clear();
        await rpc.HandleAsync($$"""{"v":2,"kind":"req","id":{{_nextId++}},"type":"{{method}}","payload":{{payload}}}""");
        var response = JsonElement.Parse(_sent.Single());
        return response.TryGetProperty("error", out var error) ? error : response.GetProperty("payload");
    }

    private static DateTimeOffset Clock(JsonElement snapshot) =>
        DateTimeOffset.FromUnixTimeMilliseconds(snapshot.GetProperty("clockUtcMilliseconds").GetInt64());

    [Fact]
    public async Task SkyGet_ReturnsTimeLayersModeAndSelection()
    {
        var (shell, _, rpc) = await CreateAsync();

        var snapshot = await CallAsync(rpc, "sky.get");

        Clock(snapshot).Should().Be(WinterNightUtc);
        snapshot.GetProperty("isPlaying").GetBoolean().Should().BeFalse();
        snapshot.GetProperty("rate").GetInt32().Should().Be(0);
        snapshot.GetProperty("rates").EnumerateArray().Select(r => r.GetInt32()).Should().Equal(1, 60, 600, 3600);
        snapshot.GetProperty("viewMode").GetString().Should().Be(shell.Sky.Modes.ViewMode);
        snapshot.GetProperty("layers").GetArrayLength().Should().Be(shell.Sky.Layers.LayerToggles.Count);
        var selection = snapshot.GetProperty("selection");
        if (shell.Targets.SelectedObject is { } selected)
        {
            selection.GetProperty("id").GetString().Should().Be(selected.Id);
        }
        else
        {
            selection.ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public async Task PlayPauseAndRate_FollowTheTimeViewModel()
    {
        var (shell, _, rpc) = await CreateAsync();

        var playing = await CallAsync(rpc, "sky.play");
        playing.GetProperty("isPlaying").GetBoolean().Should().BeTrue();
        playing.GetProperty("rate").GetInt32().Should().Be(60);

        var faster = await CallAsync(rpc, "sky.setRate", """{"rate":500}""");
        faster.GetProperty("rate").GetInt32().Should().Be(600, "the nearest supported rate is used");
        shell.Sky.Time.SelectedRate.Should().Be(600);

        var paused = await CallAsync(rpc, "sky.pause");
        paused.GetProperty("isPlaying").GetBoolean().Should().BeFalse();
        shell.Sky.Time.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task Step_MovesTheObservationTime()
    {
        var (shell, _, rpc) = await CreateAsync();

        var later = await CallAsync(rpc, "sky.step", """{"minutes":90}""");

        Clock(later).Should().Be(WinterNightUtc.AddMinutes(90));
        shell.Observer.ToUtc(shell.Observer.ObservationDateTime).Should().Be(WinterNightUtc.AddMinutes(90).UtcDateTime);
    }

    [Fact]
    public async Task SetLayerAndViewMode_ChangeTheSkyViewModels()
    {
        var (shell, _, rpc) = await CreateAsync();

        var snapshot = await CallAsync(rpc, "sky.setLayer", """{"key":"constellationLines","enabled":false}""");
        shell.Sky.Layers.LayerToggles.Single(t => t.Key == "constellationLines").IsEnabled.Should().BeFalse();
        snapshot.GetProperty("layers").EnumerateArray()
            .Single(l => l.GetProperty("key").GetString() == "constellationLines").GetProperty("enabled").GetBoolean().Should().BeFalse();

        snapshot = await CallAsync(rpc, "sky.setViewMode", """{"mode":"allSky"}""");
        snapshot.GetProperty("viewMode").GetString().Should().Be("allSky");
        shell.Sky.Modes.ViewMode.Should().Be("allSky");
    }

    [Theory]
    [InlineData("sky.setLayer", """{"key":"aurora","enabled":true}""")]
    [InlineData("sky.setViewMode", """{"mode":"globe"}""")]
    [InlineData("sky.step", """{"minutes":10000000}""")]
    public async Task ParametersOutsideTheContract_AreRejected(string method, string payload)
    {
        var (_, _, rpc) = await CreateAsync();

        var error = await CallAsync(rpc, method, payload);

        error.GetProperty("code").GetString().Should().Be(RpcErrorCodes.BadRequest);
    }

    [Fact]
    public async Task ASelectionMadeElsewhere_IsAnnounced_AndShownInTheSnapshot()
    {
        var (shell, endpoints, rpc) = await CreateAsync();
        _sent.Clear();

        var other = shell.Targets.SelectedObject?.Id == "M42" ? "M31" : "M42";
        shell.Targets.SelectById(other).Should().BeTrue();
        _eventClock.LastTimer!.Fire();

        var message = JsonElement.Parse(_sent.Single());
        message.GetProperty("type").GetString().Should().Be("sky.changed");
        message.GetProperty("payload").GetProperty("revision").GetInt32().Should().Be(endpoints.Revision);

        var selection = (await CallAsync(rpc, "sky.get")).GetProperty("selection");
        selection.GetProperty("id").GetString().Should().Be(other);
        selection.GetProperty("name").GetString().Should().Contain(other);
        selection.GetProperty("typeName").GetString().Should().NotBeNullOrEmpty();
    }
}
