using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>
/// The R1c areas of the planetarium: the darkness bar (<c>night.get</c>), the object card
/// (<c>selection.*</c>), the command palette (<c>search.*</c>) and setting the sky time.
/// </summary>
public sealed class PlanetariumEndpointsTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    // One clock per module: a manual clock only keeps its last timer.
    private readonly ManualTimeProvider _skyClock = new(WinterNightUtc);
    private readonly ManualTimeProvider _nightClock = new(WinterNightUtc);
    private readonly ManualTimeProvider _selectionClock = new(WinterNightUtc);
    private readonly List<string> _sent = [];
    private int _nextId = 1;

    private async Task<(ShellViewModel Shell, RpcDispatcher Rpc)> CreateAsync(INameResolver? resolver = null)
    {
        var shell = TestShell.Create(time: new ManualTimeProvider(WinterNightUtc),
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo), resolver: resolver);
        await shell.LoadAndSettleAsync();
        var ui = new SynchronizationContextUiDispatcher(null);
        var rpc = new RpcDispatcher(_sent.Add, (_, _) => { });
        var selection = new SelectionEndpoints(shell, ui, _selectionClock);
        foreach (var module in new IEndpointModule[]
                 {
                     new SkyEndpoints(shell, ui, _skyClock), new NightEndpoints(shell.Observer, shell.Weather, ui, _nightClock),
                     selection, new SearchEndpoints(shell, selection),
                 })
        {
            module.Register(rpc);
        }

        return (shell, rpc);
    }

    private async Task<JsonElement> CallAsync(RpcDispatcher rpc, string method, string payload = "{}")
    {
        _sent.Clear();
        await rpc.HandleAsync($$"""{"v":2,"kind":"req","id":{{_nextId++}},"type":"{{method}}","payload":{{payload}}}""");
        var response = JsonElement.Parse(_sent.Single(m => m.Contains("\"res\"", StringComparison.Ordinal)));
        response.TryGetProperty("error", out var error).Should().BeFalse($"{method} must answer within its contract ({error})");
        return response.GetProperty("payload");
    }

    private List<JsonElement> Events(string name) =>
        [.. _sent.Select(m => JsonElement.Parse(m)).Where(m => m.GetProperty("type").GetString() == name)];

    [Fact]
    public async Task NightGet_SamplesTheNightFromAnHourBeforeSunsetToAnHourAfterSunrise()
    {
        var (shell, rpc) = await CreateAsync();
        var night = shell.Observer.Night!;

        var timeline = await CallAsync(rpc, "night.get");

        var start = DateTimeOffset.FromUnixTimeMilliseconds(timeline.GetProperty("startUtcMilliseconds").GetInt64());
        var local = TimeZoneInfo.ConvertTimeFromUtc(start.UtcDateTime, shell.Observer.TimeZone);
        local.Minute.Should().Be(local.Minute / 10 * 10, "samples fall on whole 10 minutes of the site's clock");
        local.Second.Should().Be(0);
        (night.WindowStartLocal - NightEndpoints.Margin - local).Should().BeGreaterThanOrEqualTo(TimeSpan.Zero).And.BeLessThan(TimeSpan.FromMinutes(10));
        timeline.GetProperty("stepMinutes").GetInt32().Should().Be(NightEndpoints.StepMinutes);
        var sun = timeline.GetProperty("sunAltitudeDegrees").EnumerateArray().Select(a => a.GetDouble()).ToList();
        var moon = timeline.GetProperty("moonAltitudeDegrees").EnumerateArray().Select(a => a.GetDouble()).ToList();
        sun.Should().HaveCount(moon.Count).And.HaveCountGreaterThan(60);
        sun[0].Should().BePositive("an hour before sunset");
        sun[^1].Should().BePositive("an hour after sunrise");
        sun.Min().Should().BeLessThan(-18, "a winter night at 19° N is fully dark");
        timeline.GetProperty("moonIlluminatedPercent").GetDouble().Should().BeInRange(0, 100);
        timeline.GetProperty("cloudPercent").GetArrayLength().Should().Be(0, "there is no forecast");
    }

    [Fact]
    public async Task NightChanged_IsAnnouncedForAnotherNight_NotForAnotherTimeTheSameNight()
    {
        var (shell, _) = await CreateAsync();
        _sent.Clear();

        shell.Observer.ObservationDateTime = shell.Observer.ObservationDateTime.AddMinutes(30);
        _nightClock.LastTimer.Should().BeNull("the night did not change");

        shell.Observer.ObservationDateTime = shell.Observer.ObservationDateTime.AddDays(3);
        _nightClock.LastTimer!.Fire();
        Events("night.changed").Should().ContainSingle();
    }

    [Fact]
    public async Task SetTime_ShowsTheSkyAtThatInstant_AndPauses()
    {
        var (shell, rpc) = await CreateAsync();
        await CallAsync(rpc, "sky.play");
        var target = WinterNightUtc.AddHours(3).ToUnixTimeMilliseconds();

        var sky = await CallAsync(rpc, "sky.setTime", $$"""{"utcMilliseconds":{{target}}}""");

        sky.GetProperty("isPlaying").GetBoolean().Should().BeFalse();
        sky.GetProperty("clockUtcMilliseconds").GetInt64().Should().Be(target);
        shell.Observer.ToUtc(shell.Observer.ObservationDateTime).Should().Be(WinterNightUtc.AddHours(3).UtcDateTime);
    }

    [Fact]
    public void SetTime_KeepsToTheYearsTheEphemeridesCover()
    {
        SkyEndpoints.TimeOf(long.MinValue / 2).Year.Should().Be(1900);
        SkyEndpoints.TimeOf(253402300799000).Year.Should().Be(2100);
    }

    [Fact]
    public async Task Selection_ShowsTheCatalogObjectWithItsFeasibility()
    {
        var (shell, rpc) = await CreateAsync();
        shell.Targets.SelectById("M42").Should().BeTrue();

        var card = (await CallAsync(rpc, "selection.get")).GetProperty("target");

        card.GetProperty("id").GetString().Should().Be("M42");
        card.GetProperty("title").GetString().Should().Contain("M42");
        card.GetProperty("constellation").GetString().Should().NotBeNullOrEmpty();
        card.GetProperty("coordinates").GetString().Should().StartWith("RA 05h 3").And.Contain("Dec −05°");
        card.GetProperty("altitudeDegrees").GetDouble().Should().BePositive("M42 is up at 22:00 in January");
        card.GetProperty("transitLocal").GetString().Should().HaveLength(19);
        card.GetProperty("verdict").GetString().Should().BeOneOf("notPossible", "unlikely", "marginal", "good", "excellent");
        card.GetProperty("score").GetInt32().Should().BeInRange(0, 100);
        card.GetProperty("reasons").GetArrayLength().Should().BeInRange(1, SelectionEndpoints.MaximumReasons);
        card.GetProperty("isInPlan").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Selection_AddsToAndRemovesFromThePlan_AndClears()
    {
        var (shell, rpc) = await CreateAsync();
        shell.Targets.SelectById("M31");

        var added = await CallAsync(rpc, "selection.addToPlan");
        added.GetProperty("target").GetProperty("isInPlan").GetBoolean().Should().BeTrue();
        shell.Plan.Items.Should().Contain(item => item.Id == "M31");

        var removed = await CallAsync(rpc, "selection.removeFromPlan");
        removed.GetProperty("target").GetProperty("isInPlan").GetBoolean().Should().BeFalse();

        var cleared = await CallAsync(rpc, "selection.clear");
        cleared.GetProperty("target").ValueKind.Should().Be(JsonValueKind.Null);
        shell.Targets.SelectedObject.Should().BeNull();
    }

    [Fact]
    public async Task Selection_FrameTurnsTheCameraFrameOn()
    {
        var (shell, rpc) = await CreateAsync();
        shell.Targets.SelectById("M42");

        await CallAsync(rpc, "selection.frame");
        await CallAsync(rpc, "selection.center");

        shell.Sky.Camera.IsFovVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SelectionChanged_FollowsTheSelectionAndThePlan()
    {
        var (shell, _) = await CreateAsync();
        _sent.Clear();

        shell.Targets.SelectById("M13");
        _selectionClock.LastTimer!.Fire();
        shell.Detail.AddToPlanCommand.Execute(null);
        _selectionClock.LastTimer!.Fire();

        Events("selection.changed").Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchQuery_PutsTheExactMatchFirst()
    {
        var (_, rpc) = await CreateAsync();

        var results = (await CallAsync(rpc, "search.query", """{"text":"m31","limit":10}""")).GetProperty("results");

        results[0].GetProperty("id").GetString().Should().Be("M31");
        results[0].GetProperty("typeName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchQuery_ByName_FindsTheObject_AndNothingForBlank()
    {
        var (_, rpc) = await CreateAsync();

        var byName = (await CallAsync(rpc, "search.query", """{"text":"andromeda","limit":10}""")).GetProperty("results");
        var blank = (await CallAsync(rpc, "search.query", """{"text":"  ","limit":10}""")).GetProperty("results");

        byName.EnumerateArray().Select(r => r.GetProperty("id").GetString()).Should().Contain("M31");
        blank.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task SearchGo_SelectsTheObject()
    {
        var (shell, rpc) = await CreateAsync();

        var card = await CallAsync(rpc, "search.go", """{"id":"M13"}""");

        shell.Targets.SelectedObject!.Id.Should().Be("M13");
        card.GetProperty("target").GetProperty("id").GetString().Should().Be("M13");
    }

    [Fact]
    public async Task SearchResolve_UsesTheCatalogFirst_ThenSaysWhatItCouldNotFind()
    {
        var (shell, rpc) = await CreateAsync();

        var inCatalog = await CallAsync(rpc, "search.resolve", """{"text":"M42"}""");
        inCatalog.GetProperty("found").GetBoolean().Should().BeTrue();
        shell.Targets.SelectedObject!.Id.Should().Be("M42");

        var missing = await CallAsync(rpc, "search.resolve", """{"text":"Sh2-155"}""");
        missing.GetProperty("found").GetBoolean().Should().BeFalse();
        missing.GetProperty("message").GetString().Should().Contain("Sh2-155");
    }
}
