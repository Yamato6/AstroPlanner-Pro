using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary><c>observer.get</c> answers from the observer context; changes become one <c>observer.changed</c>.</summary>
public sealed class ObserverEndpointsTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _eventClock = new(WinterNightUtc);
    private readonly List<string> _sent = [];
    private readonly List<string> _log = [];

    private async Task<(ShellViewModel Shell, ObserverEndpoints Endpoints, RpcDispatcher Rpc)> CreateAsync()
    {
        var shell = TestShell.Create(time: new ManualTimeProvider(WinterNightUtc));
        await shell.LoadCommand.ExecuteAsync(null);
        var endpoints = new ObserverEndpoints(shell.Observer, new SynchronizationContextUiDispatcher(null), _eventClock);
        var rpc = new RpcDispatcher(_sent.Add, (message, _) => _log.Add(message));
        endpoints.Register(rpc);
        return (shell, endpoints, rpc);
    }

    private async Task<JsonElement> GetAsync(RpcDispatcher rpc)
    {
        _sent.Clear();
        await rpc.HandleAsync("""{"v":2,"kind":"req","id":1,"type":"observer.get","payload":{}}""");
        var response = JsonElement.Parse(_sent.Single());
        response.TryGetProperty("error", out var error).Should().BeFalse($"the snapshot must satisfy its contract ({error})");
        return response.GetProperty("payload");
    }

    [Fact]
    public async Task ObserverGet_ReturnsTheSiteTimeNightAndMoon()
    {
        var (shell, _, rpc) = await CreateAsync();
        var observer = shell.Observer;

        var payload = await GetAsync(rpc);

        payload.GetProperty("locationName").GetString().Should().Be(observer.LocationName);
        payload.GetProperty("latitudeDegrees").GetDouble().Should().Be(observer.Latitude);
        payload.GetProperty("longitudeDegrees").GetDouble().Should().Be(observer.Longitude);
        payload.GetProperty("timeZoneId").GetString().Should().Be(ObserverEndpoints.IanaId(observer.TimeZone)).And.Contain("/", "an IANA id");
        payload.GetProperty("observationTimeLocal").GetString().Should().Be(observer.ObservationDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
        payload.GetProperty("minimumAltitudeDegrees").GetDouble().Should().Be(observer.MinimumAltitude);
        payload.GetProperty("zenithSqm").GetDouble().Should().Be(observer.EffectiveZenithSqm);
        payload.GetProperty("night").GetProperty("windowStartLocal").GetString().Should().HaveLength(19);
        payload.GetProperty("moon").GetProperty("illuminatedPercent").GetDouble().Should().BeInRange(0, 100);
        payload.GetProperty("inputError").ValueKind.Should().Be(JsonValueKind.Null);
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task Changes_AreAnnouncedOnce_AndTheNextSnapshotCarriesTheirRevision()
    {
        var (shell, endpoints, rpc) = await CreateAsync();
        var before = (await GetAsync(rpc)).GetProperty("revision").GetInt32();
        _sent.Clear();

        shell.Observer.MinimumAltitude = 25;
        shell.Observer.MinimumAltitude = 30;
        _sent.Should().BeEmpty("changes are coalesced");
        _eventClock.LastTimer!.Fire();

        var message = JsonElement.Parse(_sent.Single());
        message.GetProperty("type").GetString().Should().Be("observer.changed");
        var revision = message.GetProperty("payload").GetProperty("revision").GetInt32();
        revision.Should().BeGreaterThan(before).And.Be(endpoints.Revision);

        var payload = await GetAsync(rpc);
        payload.GetProperty("revision").GetInt32().Should().Be(revision);
        payload.GetProperty("minimumAltitudeDegrees").GetDouble().Should().Be(30);
    }

    [Fact]
    public async Task AfterDispose_ChangesSendNothing()
    {
        var (shell, endpoints, _) = await CreateAsync();
        endpoints.Dispose();
        _sent.Clear();

        shell.Observer.MinimumAltitude = 35;
        _eventClock.LastTimer?.Fire();

        _sent.Should().BeEmpty();
    }
}
