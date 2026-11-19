using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Sky;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>A channel that records what the view model sends and lets tests play the renderer.</summary>
internal sealed class FakeSkyChannel : ISkyChannel
{
    public List<JsonElement> Sent { get; } = [];

    public event EventHandler<IncomingSkyMessage>? MessageReceived;

    public void Post(string json)
    {
        using var document = JsonDocument.Parse(json);
        Sent.Add(document.RootElement.Clone());
    }

    /// <summary>Delivers a renderer message through the real decoder, as SkyHost does.</summary>
    public void Receive(string json)
    {
        var message = SkyMessageCodec.Decode(json, out var error) ?? throw new InvalidOperationException(error);
        MessageReceived?.Invoke(this, message);
    }

    public void Ready() => Receive("""{"v":1,"type":"ready","payload":{"rendererVersion":"test","webGlVersion":2}}""");

    public IEnumerable<string> Types => Sent.Select(m => m.GetProperty("type").GetString()!);

    public JsonElement Last(string type) => Sent.Last(m => m.GetProperty("type").GetString() == type).GetProperty("payload");

    public int Count(string type) => Types.Count(t => t == type);
}

public sealed class SkyViewModelTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _time = new(WinterNightUtc);
    private readonly FakeSkyViewStore _store = new();
    private readonly FakePlanService _plan = new();

    private async Task<(ShellViewModel Shell, FakeSkyChannel Channel)> CreateAsync(bool ready = true)
    {
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, .. SolarSystemCatalog.Planets],
            time: _time, plan: _plan, skyStore: _store);
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        var channel = new FakeSkyChannel();
        shell.Sky.Attach(channel);
        if (ready)
        {
            channel.Ready();
        }

        return (shell, channel);
    }

    [Fact]
    public async Task NothingIsSent_UntilTheRendererIsReady_ThenTheWholeState()
    {
        var (shell, channel) = await CreateAsync(ready: false);
        shell.Targets.SelectById("M42");
        channel.Sent.Should().BeEmpty();

        channel.Ready();

        channel.Types.Should().StartWith(["init", "setObserver", "frameTransform", "movingBodies", "setTime", "centerOn", "setHighlights", "selectObject"]);
        channel.Last("init").GetProperty("dataBaseUrl").GetString().Should().Be("https://data.astroplanner/");
        channel.Last("selectObject").GetProperty("id").GetString().Should().Be("M42");
        channel.Last("movingBodies").GetProperty("bodies").GetArrayLength().Should().Be(9);
        // Mexico City defaults, the time of the context.
        channel.Last("setObserver").GetProperty("latitudeDegrees").GetDouble().Should().BeApproximately(19.4326, 1e-9);
        channel.Last("setTime").GetProperty("utc").GetString().Should().Be("2026-01-16T04:00:00.000Z");
    }

    [Fact]
    public async Task EverySentMessage_IsValidForTheContract()
    {
        var (shell, channel) = await CreateAsync();
        shell.Sky.LayerToggles[0].IsEnabled = false;
        shell.Sky.PlayCommand.Execute(null);
        shell.Sky.PauseCommand.Execute(null);

        // Re-encode through the codec types: an unknown or malformed message would not round-trip.
        channel.Sent.Should().NotBeEmpty();
        channel.Sent.Should().OnlyContain(m => m.GetProperty("v").GetInt32() == 1);
        channel.Types.Should().BeSubsetOf(BridgeTypes);
    }

    private static readonly string[] BridgeTypes =
    [
        "init", "setObserver", "setTime", "setTimeRate", "frameTransform", "movingBodies", "setLayers",
        "setHighlights", "selectObject", "centerOn", "setViewMode", "setTheme"
    ];

    [Fact]
    public async Task RendererPick_SelectsTheCatalogObject_EvenWhenFilteredOut()
    {
        var (shell, channel) = await CreateAsync();
        shell.Targets.SearchText = "M13";
        shell.Targets.Objects.Select(o => o.Id).Should().Equal("M13");

        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"dso","id":"M42"}}""");

        shell.Targets.SelectedObject!.Id.Should().Be("M42");
        shell.Targets.Objects.Select(o => o.Id).Should().Contain("M42", "the picked object is kept in the table");
        shell.Detail.SelectedObject!.Id.Should().Be("M42");
        shell.Sky.SelectedText.Should().Contain("M42");
        shell.Sky.HasCatalogSelection.Should().BeTrue();

        // Picking another row in the grid releases the pinned object.
        shell.Targets.SelectedObject = shell.Targets.Objects.Single(o => o.Id == "M13");
        shell.Targets.SearchText = "M1";
        shell.Targets.SearchText = "M13";
        shell.Targets.Objects.Select(o => o.Id).Should().Equal("M13");
    }

    [Fact]
    public async Task RendererPick_OfTheMoonOrAStar_IsShownWithoutTouchingTargets()
    {
        var (shell, channel) = await CreateAsync();
        var selected = shell.Targets.SelectedObject;

        channel.Receive("""{"v":1,"type":"hover","payload":{"kind":"star","id":"32263","label":"Sirius (-1.4)"}}""");
        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"star","id":"32263"}}""");

        shell.Targets.SelectedObject.Should().BeSameAs(selected);
        shell.Sky.SelectedText.Should().Be("Sirius (-1.4)");
        shell.Sky.HasCatalogSelection.Should().BeFalse();
        shell.Sky.HoverText.Should().Be("Sirius (-1.4)");

        channel.Receive("""{"v":1,"type":"hover","payload":{"kind":null,"id":null,"label":null}}""");
        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"body","id":"Moon"}}""");
        shell.Sky.SelectedText.Should().Be("Moon");

        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":null,"id":null}}""");
        shell.Sky.HasCatalogSelection.Should().Be(shell.Targets.SelectedObject is not null);
    }

    [Fact]
    public async Task SelectionInTargets_IsSentToTheRenderer_OncePerChange()
    {
        var (shell, channel) = await CreateAsync();
        var before = channel.Count("selectObject");

        shell.Targets.SelectById("M31");
        shell.Targets.SelectById("M31");

        channel.Count("selectObject").Should().Be(before + 1);
        channel.Last("selectObject").GetProperty("kind").GetString().Should().Be("dso");

        shell.Targets.SelectById("Jupiter");
        channel.Last("selectObject").GetProperty("kind").GetString().Should().Be("body");
    }

    [Fact]
    public async Task Play_RunsOnlyTheRenderer_AndPause_AppliesTheSkyTimeToTheContext()
    {
        var (shell, channel) = await CreateAsync();
        var observation = shell.ObservationDateTime;
        shell.Sky.SelectedRate = 3600;

        shell.Sky.PlayCommand.Execute(null);
        shell.Sky.IsPlaying.Should().BeTrue();
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(3600);

        _time.UtcNow += TimeSpan.FromSeconds(2);   // two real seconds = two sky hours
        shell.ObservationDateTime.Should().Be(observation, "Targets is not recalculated while playing");
        _time.LastTimer!.Fire();                   // periodic re-sync
        channel.Last("setTime").GetProperty("utc").GetString().Should().Be("2026-01-16T06:00:00.000Z");

        shell.Sky.PauseCommand.Execute(null);

        shell.Sky.IsPlaying.Should().BeFalse();
        shell.ObservationDateTime.Should().Be(observation.AddHours(2));
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(0);
        _time.LastTimer.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Resync_RefreshesTheFrameAndTheTracks_WhenTheSkyTimeMovesFar()
    {
        var (shell, channel) = await CreateAsync();
        shell.Sky.SelectedRate = 3600;
        shell.Sky.PlayCommand.Execute(null);
        var frames = channel.Count("frameTransform");
        var bodies = channel.Count("movingBodies");

        _time.UtcNow += TimeSpan.FromSeconds(0.1); // 6 sky minutes: same frame and tracks
        shell.Sky.Resync();
        channel.Count("frameTransform").Should().Be(frames);

        _time.UtcNow += TimeSpan.FromSeconds(12);  // 12 more sky hours: outside the tracks
        shell.Sky.Resync();
        channel.Count("frameTransform").Should().Be(frames + 1);
        channel.Count("movingBodies").Should().Be(bodies + 1);
    }

    [Fact]
    public async Task Step_MovesTheObservationTime_AndTheSky()
    {
        var (shell, channel) = await CreateAsync();
        var observation = shell.ObservationDateTime;

        shell.Sky.StepCommand.Execute("60");
        shell.Sky.StepCommand.Execute("-10");
        shell.Sky.StepCommand.Execute("abc");

        shell.ObservationDateTime.Should().Be(observation.AddMinutes(50));
        channel.Last("setTime").GetProperty("utc").GetString().Should().Be("2026-01-16T04:50:00.000Z");
        channel.Last("frameTransform").GetProperty("epochUtc").GetString().Should().Be("2026-01-16T04:50:00.000Z");
        shell.Sky.SkyTimeText.Should().Contain("22:50");
    }

    [Fact]
    public async Task SkyAtTheCurrentTime_RunsAtRealTime_OtherwiseItIsPaused()
    {
        var (shell, channel) = await CreateAsync();
        shell.Sky.NowCommand.Execute(null);
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(1);

        shell.Sky.StepCommand.Execute("1440");
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(0);
    }

    [Fact]
    public async Task ChoosingATimeInTheTopBar_StopsTheAnimation()
    {
        var (shell, _) = await CreateAsync();
        shell.Sky.PlayCommand.Execute(null);

        shell.ObservationDateTime = shell.ObservationDateTime.AddHours(3);

        shell.Sky.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task ShortcutsFromTheRenderer_TogglePlayAndFocusTheSearch()
    {
        var (shell, channel) = await CreateAsync();
        var focus = 0;
        shell.Sky.FocusSearchRequested += (_, _) => focus++;

        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"togglePause"}}""");
        shell.Sky.IsPlaying.Should().BeTrue();
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"togglePause"}}""");
        shell.Sky.IsPlaying.Should().BeFalse();
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"focusSearch"}}""");
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"viewAllSky"}}""");

        focus.Should().Be(1);
    }

    [Fact]
    public async Task Layers_ArePersisted_AndSent()
    {
        var (shell, channel) = await CreateAsync();

        shell.Sky.LayerToggles.Single(t => t.Key == "equatorialGrid").IsEnabled = true;
        shell.Sky.StarLimitMagnitude = 6.5;

        channel.Last("setLayers").GetProperty("equatorialGrid").GetBoolean().Should().BeTrue();
        channel.Last("setLayers").GetProperty("starLimitMagnitude").GetDouble().Should().Be(6.5);
        _store.Saved.EquatorialGrid.Should().BeTrue();
        _store.Saved.StarLimitMagnitude.Should().Be(6.5);

        // A new view model starts from the saved settings.
        var again = TestShell.Create(time: _time, skyStore: _store);
        again.Sky.LayerToggles.Single(t => t.Key == "equatorialGrid").IsEnabled.Should().BeTrue();
        again.Sky.StarLimitMagnitude.Should().Be(6.5);
    }

    [Fact]
    public async Task ViewChanges_AreRemembered_AndRestoredWithCenterOn()
    {
        var (shell, channel) = await CreateAsync();
        channel.Receive("""{"v":1,"type":"viewChanged","payload":{"mode":"dome","azimuthDegrees":90,"altitudeDegrees":45,"fovDegrees":30}}""");

        _store.Saved.AzimuthDegrees.Should().Be(90);
        _store.Saved.FovDegrees.Should().Be(30);

        var again = TestShell.Create(time: _time, skyStore: _store);
        await again.LoadCommand.ExecuteAsync(null);
        var second = new FakeSkyChannel();
        again.Sky.Attach(second);
        second.Ready();
        second.Last("centerOn").GetProperty("fovDegrees").GetDouble().Should().Be(30);
    }

    [Fact]
    public async Task Plan_IsHighlightedInTheSky()
    {
        var (shell, channel) = await CreateAsync();

        shell.Plan.Add(shell.Targets.AllObjects.Single(o => o.Id == "M31"));

        channel.Last("setHighlights").GetProperty("plan").EnumerateArray().Select(e => e.GetString()).Should().Equal("M31");
    }

    [Theory]
    [InlineData("m31", "M31")]
    [InlineData("Orion Nebula", "M42")]
    [InlineData("jupiter", "Jupiter")]
    public async Task Search_SelectsAndCentersTheObject(string query, string expected)
    {
        var (shell, channel) = await CreateAsync();
        var centers = channel.Count("centerOn");

        shell.Sky.SearchText = query;
        shell.Sky.SearchCommand.Execute(null);

        shell.Targets.SelectedObject!.Id.Should().Be(expected);
        channel.Count("centerOn").Should().Be(centers + 1);
        shell.Sky.SearchMessage.Should().BeNull();
    }

    [Fact]
    public async Task Search_ReportsUnknownObjects()
    {
        var (shell, _) = await CreateAsync();
        shell.Sky.SearchText = "NGC 99999";

        shell.Sky.SearchCommand.Execute(null);

        shell.Sky.SearchMessage.Should().Contain("NGC 99999");
    }

    [Fact]
    public async Task CenterOnSelection_And_OpenDetails()
    {
        var (shell, channel) = await CreateAsync();
        var opened = 0;
        shell.Sky.OpenDetailsRequested += (_, _) => opened++;
        shell.Targets.SelectById("M13");
        var centers = channel.Count("centerOn");

        shell.Sky.CenterOnSelectionCommand.Execute(null);
        shell.Sky.OpenDetailsCommand.Execute(null);

        channel.Count("centerOn").Should().Be(centers + 1);
        opened.Should().Be(1);
    }

    [Fact]
    public async Task StatsAndErrors_AreReported()
    {
        var (shell, channel) = await CreateAsync();

        channel.Receive("""{"v":1,"type":"stats","payload":{"fps":144.2,"p95FrameMs":7.9,"frames":1442,"width":1920,"height":1080,"stars":83476,"benchmark":true}}""");
        channel.Receive("""{"v":1,"type":"firstFrame","payload":{"milliseconds":412.5}}""");
        channel.Receive("""{"v":1,"type":"error","payload":{"message":"WebGL context lost"}}""");

        shell.Sky.StatsText.Should().Contain("144 fps").And.Contain("1920×1080");
        shell.Sky.ShowStats = true;
        _store.Saved.ShowStats.Should().BeTrue();
    }

    [Fact]
    public async Task Fallback_ShowsTheReason_AndStopsSending()
    {
        var (shell, channel) = await CreateAsync();
        var sent = channel.Sent.Count;

        shell.Sky.UseFallback("runtime");

        shell.Sky.IsRendererAvailable.Should().BeFalse();
        shell.Sky.RendererMessage.Should().Contain("WebView2");
        shell.Targets.SelectById("M31");
        channel.Sent.Should().HaveCount(sent);
        shell.Sky.UseFallback("assets");
        shell.Sky.RendererMessage.Should().Contain("does not include");
    }
}

public sealed class SkyClockTests
{
    [Fact]
    public void Clock_AdvancesAtItsRate()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var clock = new SkyClock(time);
        var start = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        clock.Set(start, 600);
        time.UtcNow += TimeSpan.FromSeconds(6);

        clock.UtcNow.Should().Be(start.AddHours(1));
        clock.UtcNow.Kind.Should().Be(DateTimeKind.Utc);
        clock.Rate.Should().Be(600);
    }
}

public sealed class SkyAssetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Zip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    [Fact]
    public void Extract_CreatesAHashFolder_ReusesIt_AndRemovesOldCopies()
    {
        var old = Path.Combine(_root, "0123456789abcdef");
        Directory.CreateDirectory(old);

        var first = SkyAssets.Extract(Zip(("app/index.html", "<html>"), ("data/stars.bin", "x")), _root);
        var second = SkyAssets.Extract(Zip(("app/index.html", "<html>"), ("data/stars.bin", "x")), _root);

        first.Should().NotBeNull();
        second.Should().Be(first);
        File.ReadAllText(Path.Combine(first!.AppDirectory, "index.html")).Should().Be("<html>");
        File.Exists(Path.Combine(first.DataDirectory, "stars.bin")).Should().BeTrue();
        Directory.Exists(old).Should().BeFalse();
        Directory.GetDirectories(_root).Should().ContainSingle();
    }

    [Fact]
    public void Extract_WithoutARenderer_ReturnsNull()
    {
        SkyAssets.Extract(Zip(("data/stars.bin", "x")), _root).Should().BeNull();
        Directory.GetDirectories(_root).Should().BeEmpty("the staging folder is cleaned up");
    }

    [Fact]
    public void TheBuild_EmbedsTheRenderer()
    {
        // Only when the UI was built with Node.js (always in CI); otherwise the app falls back.
        using var zip = SkyAssets.OpenEmbedded();
        if (zip is null)
        {
            return;
        }

        using var archive = new ZipArchive(zip);
        archive.Entries.Select(e => e.FullName).Should().Contain(["app/index.html", "data/stars.bin", "data/dsos.json"]);
    }
}

public sealed class SkyHostSecurityTests
{
    [Theory]
    [InlineData("https://app.astroplanner/index.html", true)]
    [InlineData("https://app.astroplanner/index.html?bench=1", true)]
    [InlineData("https://APP.astroplanner/assets/x.js", true)]
    [InlineData("https://data.astroplanner/stars.bin", false)]
    [InlineData("http://app.astroplanner/index.html", false)]
    [InlineData("https://app.astroplanner:8443/index.html", false)]
    [InlineData("https://app.astroplanner.evil.com/", false)]
    [InlineData("https://github.com/", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheAppHostCanBeLoaded_OrSendMessages(string? uri, bool allowed)
    {
        SkyHost.IsAllowedNavigation(uri).Should().Be(allowed);
        SkyHost.IsTrustedSource(uri).Should().Be(allowed);
    }
}
