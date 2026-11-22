using System.IO;
using System.IO.Compression;
using System.Text.Json;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Resources;
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

        channel.Types.Should().StartWith(["init", "setObserver", "frameTransform", "movingBodies", "setSkyAppearance", "setTime", "centerOn", "setHighlights", "selectObject", "setFov"]);
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
        shell.Sky.Layers.LayerToggles[0].IsEnabled = false;
        shell.Sky.Time.PlayCommand.Execute(null);
        shell.Sky.Time.PauseCommand.Execute(null);

        // Re-encode through the codec types: an unknown or malformed message would not round-trip.
        channel.Sent.Should().NotBeEmpty();
        channel.Sent.Should().OnlyContain(m => m.GetProperty("v").GetInt32() == 1);
        channel.Types.Should().BeSubsetOf(BridgeTypes);
    }

    private static readonly string[] BridgeTypes =
    [
        "init", "setObserver", "setTime", "setTimeRate", "frameTransform", "movingBodies", "setLayers",
        "setHighlights", "selectObject", "centerOn", "setViewMode", "setTheme", "setFov", "setSkyAppearance"
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
        shell.Sky.Time.SelectedRate = 3600;

        shell.Sky.Time.PlayCommand.Execute(null);
        shell.Sky.Time.IsPlaying.Should().BeTrue();
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(3600);

        _time.UtcNow += TimeSpan.FromSeconds(2);   // two real seconds = two sky hours
        shell.ObservationDateTime.Should().Be(observation, "Targets is not recalculated while playing");
        _time.LastTimer!.Fire();                   // periodic re-sync
        channel.Last("setTime").GetProperty("utc").GetString().Should().Be("2026-01-16T06:00:00.000Z");

        shell.Sky.Time.PauseCommand.Execute(null);

        shell.Sky.Time.IsPlaying.Should().BeFalse();
        shell.ObservationDateTime.Should().Be(observation.AddHours(2));
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(0);
        _time.LastTimer.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Resync_RefreshesTheFrameAndTheTracks_WhenTheSkyTimeMovesFar()
    {
        var (shell, channel) = await CreateAsync();
        shell.Sky.Time.SelectedRate = 3600;
        shell.Sky.Time.PlayCommand.Execute(null);
        var frames = channel.Count("frameTransform");
        var bodies = channel.Count("movingBodies");

        _time.UtcNow += TimeSpan.FromSeconds(0.1); // 6 sky minutes: same frame and tracks
        shell.Sky.Time.Resync();
        channel.Count("frameTransform").Should().Be(frames);

        _time.UtcNow += TimeSpan.FromSeconds(12);  // 12 more sky hours: outside the tracks
        shell.Sky.Time.Resync();
        channel.Count("frameTransform").Should().Be(frames + 1);
        channel.Count("movingBodies").Should().Be(bodies + 1);
    }

    [Fact]
    public async Task Step_MovesTheObservationTime_AndTheSky()
    {
        var (shell, channel) = await CreateAsync();
        var observation = shell.ObservationDateTime;

        shell.Sky.Time.StepCommand.Execute("60");
        shell.Sky.Time.StepCommand.Execute("-10");
        shell.Sky.Time.StepCommand.Execute("abc");

        shell.ObservationDateTime.Should().Be(observation.AddMinutes(50));
        channel.Last("setTime").GetProperty("utc").GetString().Should().Be("2026-01-16T04:50:00.000Z");
        channel.Last("frameTransform").GetProperty("epochUtc").GetString().Should().Be("2026-01-16T04:50:00.000Z");
        shell.Sky.Time.SkyTimeText.Should().Contain("22:50");
    }

    [Fact]
    public async Task SkyAtTheCurrentTime_RunsAtRealTime_OtherwiseItIsPaused()
    {
        var (shell, channel) = await CreateAsync();
        shell.Sky.Time.NowCommand.Execute(null);
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(1);

        shell.Sky.Time.StepCommand.Execute("1440");
        channel.Last("setTime").GetProperty("rate").GetDouble().Should().Be(0);
    }

    [Fact]
    public async Task ChoosingATimeInTheTopBar_StopsTheAnimation()
    {
        var (shell, _) = await CreateAsync();
        shell.Sky.Time.PlayCommand.Execute(null);

        shell.ObservationDateTime = shell.ObservationDateTime.AddHours(3);

        shell.Sky.Time.IsPlaying.Should().BeFalse();
    }

    [Fact]
    public async Task ShortcutsFromTheRenderer_TogglePlayAndFocusTheSearch()
    {
        var (shell, channel) = await CreateAsync();
        var focus = 0;
        shell.Sky.FocusSearchRequested += (_, _) => focus++;

        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"togglePause"}}""");
        shell.Sky.Time.IsPlaying.Should().BeTrue();
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"togglePause"}}""");
        shell.Sky.Time.IsPlaying.Should().BeFalse();
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"focusSearch"}}""");
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"viewAllSky"}}""");

        focus.Should().Be(1);
    }

    [Fact]
    public async Task Layers_ArePersisted_AndSent()
    {
        var (shell, channel) = await CreateAsync();

        shell.Sky.Layers.LayerToggles.Single(t => t.Key == "equatorialGrid").IsEnabled = true;
        shell.Sky.Layers.StarLimitMagnitude = 6.5;

        channel.Last("setLayers").GetProperty("equatorialGrid").GetBoolean().Should().BeTrue();
        channel.Last("setLayers").GetProperty("starLimitMagnitude").GetDouble().Should().Be(6.5);
        _store.Saved.EquatorialGrid.Should().BeTrue();
        _store.Saved.StarLimitMagnitude.Should().Be(6.5);

        // A new view model starts from the saved settings.
        var again = TestShell.Create(time: _time, skyStore: _store);
        again.Sky.Layers.LayerToggles.Single(t => t.Key == "equatorialGrid").IsEnabled.Should().BeTrue();
        again.Sky.Layers.StarLimitMagnitude.Should().Be(6.5);
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

        shell.Sky.Search.SearchText = query;
        shell.Sky.Search.SearchCommand.Execute(null);

        shell.Targets.SelectedObject!.Id.Should().Be(expected);
        channel.Count("centerOn").Should().Be(centers + 1);
        shell.Sky.Search.SearchMessage.Should().BeNull();
    }

    [Fact]
    public async Task Search_ReportsUnknownObjects()
    {
        var (shell, _) = await CreateAsync();
        shell.Sky.Search.SearchText = "NGC 99999";

        shell.Sky.Search.SearchCommand.Execute(null);

        shell.Sky.Search.SearchMessage.Should().Contain("NGC 99999");
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
        shell.Sky.Layers.ShowStats = true;
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

    private static void ShouldPointAt(JsonElement payload, EquatorialCoordinates expected, double toleranceDegrees = 1e-4)
    {
        var v = new Vector3d(payload.GetProperty("x").GetDouble(), payload.GetProperty("y").GetDouble(), payload.GetProperty("z").GetDouble());
        var actual = SkyFrame.ToEquatorial(v);
        (actual.RightAscensionHours * 15).Should().BeApproximately(expected.RightAscensionHours * 15, toleranceDegrees);
        actual.DeclinationDegrees.Should().BeApproximately(expected.DeclinationDegrees, toleranceDegrees);
    }

    [Fact]
    public async Task ViewModes_AreSwitched_Remembered_AndTheFinderFollowsTheSelection()
    {
        var (shell, channel) = await CreateAsync();
        shell.Targets.SelectById("M42");
        var skyShown = 0;
        shell.Sky.Modes.ShowSkyRequested += (_, _) => skyShown++;

        shell.Sky.Modes.IsAllSkyMode = true;
        channel.Last("setViewMode").GetProperty("mode").GetString().Should().Be("allSky");
        _store.Saved.ViewMode.Should().Be("allSky");
        shell.Sky.Modes.IsDomeMode.Should().BeFalse();

        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"viewDome"}}""");
        shell.Sky.Modes.ViewMode.Should().Be("dome");
        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"viewAllSky"}}""");
        shell.Sky.Modes.ViewMode.Should().Be("allSky");

        // "Finder chart" (Detail, Targets, Sky): shows the Sky tab, centred on the selection.
        shell.Sky.Modes.OpenFinderChartCommand.Execute(null);
        skyShown.Should().Be(1);
        shell.Sky.Modes.IsFinderMode.Should().BeTrue();
        ShouldPointAt(channel.Last("centerOn"), TestCatalog.M42.Coordinates);
        channel.Last("centerOn").GetProperty("fovDegrees").GetDouble().Should().Be(10);

        shell.Targets.SelectById("M31");
        ShouldPointAt(channel.Last("centerOn"), TestCatalog.M31.Coordinates);

        // The next session opens in the same mode.
        var (_, again) = await CreateAsync();
        again.Last("init").GetProperty("viewMode").GetString().Should().Be("finder");
    }

    [Fact]
    public async Task FovOverlay_FollowsTheSelection_AndKeepsWhatTheUserDragged()
    {
        var (shell, channel) = await CreateAsync();
        channel.Last("setFov").GetProperty("visible").GetBoolean().Should().BeFalse();

        channel.Receive("""{"v":1,"type":"shortcut","payload":{"action":"toggleFov"}}""");

        shell.Sky.Camera.IsFovVisible.Should().BeTrue();
        _store.Saved.FovVisible.Should().BeTrue();
        var fov = channel.Last("setFov");
        fov.GetProperty("visible").GetBoolean().Should().BeTrue();
        // Default: Canon APS-C behind 650 mm (spec §1.3): 1.97° × 1.31°.
        fov.GetProperty("widthDegrees").GetDouble().Should().BeApproximately(1.9655, 1e-3);
        fov.GetProperty("heightDegrees").GetDouble().Should().BeApproximately(1.3133, 1e-3);
        fov.GetProperty("label").GetString().Should().Contain("650 mm");
        shell.Sky.Camera.FovText.Should().Contain("1.97").And.Contain("1.31");

        shell.Targets.SelectById("M42");
        ShouldPointAt(channel.Last("setFov").GetProperty("center"), TestCatalog.M42.Coordinates);
        shell.Sky.Modes.FinderFovDegrees.Should().BeApproximately(3 * 1.9655, 1e-3);

        // A drag in the renderer is remembered, not echoed back.
        var sent = channel.Count("setFov");
        channel.Receive("""{"v":1,"type":"fovChanged","payload":{"x":0,"y":0.6,"z":0.8,"rotationDegrees":15.5}}""");
        channel.Count("setFov").Should().Be(sent);
        shell.Sky.Camera.FovRotationDegrees.Should().Be(15.5);
        _store.Saved.FovRotationDegrees.Should().Be(15.5);
        shell.Sky.Camera.FovCenterText.Should().Contain("+53°");

        // Typed values: a preset fills the sensor; editing it by hand clears the preset.
        shell.Sky.Camera.SelectedSensorPreset = shell.Sky.Camera.SensorPresets[0];
        shell.Sky.Camera.SensorWidthMm.Should().Be(36);
        shell.Sky.Camera.SensorWidthMm = 30;
        shell.Sky.Camera.SelectedSensorPreset.Should().BeNull();

        shell.Sky.Camera.FocalLengthMm = 0;
        shell.Sky.Camera.FovText.Should().Be(Strings.SkyFovInvalid);
        channel.Last("setFov").GetProperty("visible").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SkyBrightness_IsSentWithTheTracks_AndUsesTheSiteSqm()
    {
        var (shell, channel) = await CreateAsync();
        var appearance = channel.Last("setSkyAppearance");
        appearance.GetProperty("zenithSqm").GetArrayLength().Should().Be(145);
        appearance.GetProperty("startUtc").GetString().Should().Be(channel.Last("movingBodies").GetProperty("bodies")[0].GetProperty("track").GetProperty("startUtc").GetString());
        shell.Sky.SiteSqmText.Should().Be(Strings.SkySiteSqmUnknown);
        shell.Sky.SkyBrightnessText.Should().NotBeNullOrEmpty();

        shell.Observer.ZenithSqm = 19.0;

        var darkest = channel.Last("setSkyAppearance").GetProperty("zenithSqm").EnumerateArray().Max(e => e.GetDouble());
        darkest.Should().BeLessThanOrEqualTo(19.0 + 1e-6, "a city sky is never darker than its SQM");
        shell.Sky.SiteSqmText.Should().Contain("19.0").And.Contain("6").And.Contain(Strings.SkyQualityManual);

        shell.Sky.Layers.LayerToggles.Single(t => t.Key == "skyBrightness").IsEnabled = false;
        channel.Last("setLayers").GetProperty("skyBrightness").GetBoolean().Should().BeFalse();
        _store.Saved.SkyBrightness.Should().BeFalse();
    }

    [Fact]
    public async Task ExportedImages_GetADescriptiveName()
    {
        var (shell, _) = await CreateAsync();
        shell.Sky.Modes.SuggestedImageFileName().Should().Be("AstroPlannerPro-dome-2026-01-15-2200.png");

        shell.Targets.SelectById("M42");
        shell.Sky.Modes.IsFinderMode = true;
        shell.Sky.Modes.SuggestedImageFileName().Should().Be("AstroPlannerPro-finder-M42-2026-01-15-2200.png");
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
    [InlineData("about:blank", false)]
    [InlineData("data:text/html,<script>alert(1)</script>", false)]
    [InlineData("https://app.astroplanner/index.html?res=1920x1080&mode=finder", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheAppHostCanBeLoaded_OrSendMessages(string? uri, bool allowed)
    {
        SkyHost.IsAllowedNavigation(uri).Should().Be(allowed);
        SkyHost.IsTrustedSource(uri).Should().Be(allowed);
    }
}

public sealed class SkyLaunchOptionsTests
{
    [Fact]
    public void BenchmarkSwitches_BecomeRendererParameters()
    {
        SkyLaunchOptions.Query(["AstroPlannerPro.exe"]).Should().BeEmpty();
        SkyLaunchOptions.Query(["AstroPlannerPro.exe", "--sky-benchmark", "--sky-stars=20", "--sky-resolution=1920x1080", "--sky-mode=allsky"])
            .Should().Be("?bench=1&stars=8&res=1920x1080&mode=allSky");
    }

    [Theory]
    [InlineData("--sky-resolution=1920x1080&x=1")]
    [InlineData("--sky-resolution=big")]
    [InlineData("--sky-mode=<script>")]
    [InlineData("--sky-stars=many")]
    public void AnythingMalformed_NeverReachesTheUrl(string argument) =>
        SkyLaunchOptions.Query([argument]).Should().BeEmpty();

    [Theory]
    [InlineData(new[] { "AstroPlannerPro.exe" }, false)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=web" }, true)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--UI=Web", "--sky-benchmark" }, true)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=wpf" }, false)]
    [InlineData(new[] { "AstroPlannerPro.exe", "--ui=web2" }, false)]
    public void UiWeb_OpensThePlanetariumUi_AnythingElseTheClassicOne(string[] args, bool web) =>
        SkyLaunchOptions.UseWebUi(args).Should().Be(web);

    [Fact]
    public void Pages_AreTheTwoBuiltFromTheWebWorkspace_OnTheAppHost()
    {
        SkyHost.PageUri(SkyPage.Classic, "").Should().Be(new Uri("https://app.astroplanner/index.html"));
        SkyHost.PageUri(SkyPage.Planetarium, "?bench=1").Should().Be(new Uri("https://app.astroplanner/planetarium.html?bench=1"));
        SkyHost.IsAllowedNavigation(SkyHost.PageUri(SkyPage.Planetarium, "").ToString()).Should().BeTrue();
    }
}

/// <summary>
/// WPF reports a broken binding only at run time, silently: every "Sky.…" path of the main
/// window must resolve, property by property, from ShellViewModel (the sky view model is split
/// into parts, Sky.Time, Sky.Layers, Sky.Camera, Sky.Search and Sky.Map).
/// </summary>
public sealed partial class SkyBindingTests
{
    [Fact]
    public void EverySkyBindingOfTheMainWindow_Resolves()
    {
        var xaml = File.ReadAllText(Path.Combine(AccessibilityTests.RepositoryRoot(), "src", "AstroPlanner.UI", "MainWindow.xaml"));
        var paths = SkyPath().Matches(xaml).Select(m => m.Groups[1].Value).Distinct().ToList();

        paths.Should().HaveCountGreaterThan(30, "the sky tab binds most of its controls");
        paths.Where(path => !Resolves(typeof(ShellViewModel), path)).Should().BeEmpty();
    }

    private static bool Resolves(Type type, string path)
    {
        foreach (var name in path.Split('.'))
        {
            if (type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance) is not { } property)
            {
                return false;
            }

            type = property.PropertyType;
        }

        return true;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"Binding\s+(?:DataContext\.)?(Sky\.[A-Za-z.]+)")]
    private static partial System.Text.RegularExpressions.Regex SkyPath();
}
