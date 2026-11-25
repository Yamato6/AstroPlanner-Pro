using System.ComponentModel.DataAnnotations;
using System.Text;
using AstroPlanner.BridgeGen;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.SkyBridge.Tests;

/// <summary>
/// The app channel (bridge v2, docs/bridge.md). Golden files in tests/fixtures/bridge/app are
/// shared with the Vitest suite of src/web/packages/bridge-client:
/// <list type="bullet">
/// <item><c>outgoing/*.json</c>: host → web samples (generated here, validated by zod);</item>
/// <item><c>incoming/*.json</c>: web → host samples both sides accept;</item>
/// <item><c>invalid/*.json</c>: web → host messages both sides reject.</item>
/// </list>
/// The generated TypeScript contracts must match the C# records: run the BridgeGen tool (or this
/// suite with <c>UPDATE_BRIDGE_FIXTURES=1</c>) after changing them.
/// </summary>
public sealed class AppChannelTests
{
    private static string AppFixtures => Path.Combine(BridgeContractTests.Fixtures, "app");

    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(BridgeContractTests.Fixtures, "..", "..", ".."));

    private static bool Update => Environment.GetEnvironmentVariable("UPDATE_BRIDGE_FIXTURES") == "1";

    private static readonly AppInfoResponse SampleInfo = new("2.0.0", AppThemes.Dark, SkyMessageCodec.ProtocolVersion, AppMessageCodec.ProtocolVersion);

    // Aguascalientes, 2026-01-15 at 22:00 local: the night, a waning Moon below the horizon.
    private static readonly ObserverSnapshot SampleObserver = new(
        3, "Aguascalientes", 21.88, -102.29, 1888, "America/Mexico_City", "2026-01-15T22:00:00", 20, 20.4, SkyQualitySources.Atlas,
        new ObserverNight("2026-01-15T18:21:00", "2026-01-16T07:16:00", "2026-01-15T18:21:00", "2026-01-16T07:16:00",
            "2026-01-15T19:39:00", "2026-01-16T05:58:00"),
        new ObserverMoon(12.5, -31.2, false),
        null);

    // 2026-01-16 04:00 UTC, paused, M42 selected.
    private static readonly SkySnapshot SampleSky = new(
        7, 1768536000000, 0, false, [1, 60, 600, 3600], SkyViewModes.Dome,
        [new SkyLayerState(SkyLayerKeys.Stars, "Stars", true), new SkyLayerState(SkyLayerKeys.ConstellationLines, "Constellation lines", false)],
        6.5, new SkySelection("M42", "M42 — Orion Nebula", "Nebula"));

    internal static IReadOnlyDictionary<string, string> OutgoingSamples() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.info.response"] = AppMessageCodec.Response(1, AppMethods.AppInfo.Name, SampleInfo, AppMethods.AppInfo.Response),
        ["app.info.error"] = AppMessageCodec.Error(2, AppMethods.AppInfo.Name, RpcErrorCodes.Timeout, "The host did not answer in time."),
        ["theme.changed.event"] = AppMessageCodec.Event(AppEvents.ThemeChanged, new ThemeChangedEvent(AppThemes.Night)),
        ["observer.get.response"] = AppMessageCodec.Response(4, AppMethods.ObserverGet.Name, SampleObserver, AppMethods.ObserverGet.Response),
        ["observer.get.response-no-night"] = AppMessageCodec.Response(5, AppMethods.ObserverGet.Name,
            SampleObserver with { Night = null, Moon = null, InputError = "Latitude must be between -90 and 90." }, AppMethods.ObserverGet.Response),
        ["observer.changed.event"] = AppMessageCodec.Event(AppEvents.ObserverChanged, new ObserverChangedEvent(4)),
        ["sky.get.response"] = AppMessageCodec.Response(6, AppMethods.SkyGet.Name, SampleSky, AppMethods.SkyGet.Response),
        ["sky.play.response-sky-only-selection"] = AppMessageCodec.Response(7, AppMethods.SkyPlay.Name,
            SampleSky with { IsPlaying = true, Rate = 600, Selection = new SkySelection(null, "Moon", null) }, AppMethods.SkyPlay.Response),
        ["sky.changed.event"] = AppMessageCodec.Event(AppEvents.SkyChanged, new SkyChangedEvent(8)),
        ["ui.get.response"] = AppMessageCodec.Response(9, AppMethods.UiGet.Name,
            new UiSnapshot(0, AppThemes.Dark, false, false), AppMethods.UiGet.Response),
        ["ui.changed.event"] = AppMessageCodec.Event(AppEvents.UiChanged, new UiChangedEvent(1)),
        ["layout.load.response"] = AppMessageCodec.Response(10, AppMethods.LayoutLoad.Name, SampleLayout, AppMethods.LayoutLoad.Response),
        ["layout.save.response"] = AppMessageCodec.Response(11, AppMethods.LayoutSave.Name, new NoParameters(), AppMethods.LayoutSave.Response),
        ["app.about.response"] = AppMessageCodec.Response(12, AppMethods.AppAbout.Name, SampleAbout, AppMethods.AppAbout.Response),
        ["app.openLink.response"] = AppMessageCodec.Response(13, AppMethods.AppOpenLink.Name, new OpenLinkResponse(true), AppMethods.AppOpenLink.Response),
    };

    // Help open and docked right, Settings minimised to the rail.
    private static readonly LayoutSnapshot SampleLayout = new(
    [
        new WindowLayoutState(WindowIds.Help, true, false, WindowArrangements.DockRight, 160, 90, 520, 620, 400, 2),
        new WindowLayoutState(WindowIds.Settings, true, true, WindowArrangements.Floating, 220, 120, 440, 520, 380, 1),
    ]);

    private static readonly AboutResponse SampleAbout = new(
        "2.0.0",
        "Astrophotography and observation planner.",
        [
            new AboutCredit("Cloud forecast", "Open-Meteo.com (data under the CC BY 4.0 license)", "https://open-meteo.com/"),
            new AboutCredit("Catalogs", "OpenNGC by Mattia Verga and the HYG database, both under CC BY-SA 4.0.", null),
        ],
        "Settings, plan, logbook and logs are stored only on this computer.",
        @"C:\Users\someone\AppData\Local\AstroPlannerPro",
        "© 2026 Yamato6. Free software under the MIT license.",
        "https://github.com/Yamato6/AstroPlanner-Pro");

    [Fact]
    public void GeneratedTypeScriptContracts_AreUpToDate()
    {
        var path = Path.Combine(RepositoryRoot, TypeScriptContracts.RelativePath);
        var generated = TypeScriptContracts.Generate();
        if (Update)
        {
            File.WriteAllText(path, generated, new UTF8Encoding(false));
        }

        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(generated,
            "the C# contracts changed: run `dotnet run --project tools/AstroPlanner.BridgeGen` and commit the result");
    }

    [Fact]
    public void OutgoingSamples_MatchTheGoldenFiles()
    {
        var directory = Path.Combine(AppFixtures, "outgoing");
        Directory.CreateDirectory(directory);
        foreach (var (name, json) in OutgoingSamples())
        {
            var path = Path.Combine(directory, name + ".json");
            var pretty = Pretty(json);
            if (Update)
            {
                File.WriteAllText(path, pretty, new UTF8Encoding(false));
            }

            File.Exists(path).Should().BeTrue($"{name}.json must exist (run with UPDATE_BRIDGE_FIXTURES=1)");
            File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(pretty, $"{name} changed: update the golden file");
        }
    }

    [Fact]
    public void IncomingSamples_AreAccepted()
    {
        var files = Directory.GetFiles(Path.Combine(AppFixtures, "incoming"), "*.json");
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            var json = File.ReadAllText(file);
            AppMessageCodec.IsAppMessage(json).Should().BeTrue(file);
            AppMessageCodec.Decode(json, out var error).Should().NotBeNull($"{Path.GetFileName(file)}: {error}");
        }
    }

    [Fact]
    public void InvalidSamples_AreRejected()
    {
        var files = Directory.GetFiles(Path.Combine(AppFixtures, "invalid"), "*.json");
        files.Should().NotBeEmpty();
        foreach (var file in files)
        {
            AppMessageCodec.Decode(File.ReadAllText(file), out _).Should().BeNull(Path.GetFileName(file));
        }
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"req","id":1,"type":"app.info","payload":{}}""", true)]
    [InlineData("""{"kind":"cancel","id":3,"v":2}""", true)]
    [InlineData("""{"v":1,"type":"ready","payload":{"rendererVersion":"0.1.0","webGlVersion":2}}""", false)]
    [InlineData("""{"payload":{"v":2},"v":1,"type":"x"}""", false)]
    [InlineData("""[2]""", false)]
    [InlineData("""not json""", false)]
    [InlineData("", false)]
    public void IsAppMessage_LooksAtTheTopLevelVersionOnly(string json, bool expected) =>
        AppMessageCodec.IsAppMessage(json).Should().Be(expected);

    [Fact]
    public void Decode_RejectsOversizedRequests()
    {
        var padding = new string('x', AppMessageCodec.MaximumRequestLength);
        AppMessageCodec.Decode($$$"""{"v":2,"kind":"req","id":1,"type":"app.info","payload":{"p":"{{{padding}}}"}}""", out var error)
            .Should().BeNull();
        error.Should().Be("empty or oversized message");
    }

    [Theory]
    [InlineData("app.info", true)]
    [InlineData("targets.queryPage", true)]
    [InlineData("App.info", false)]
    [InlineData("app", false)]
    [InlineData("app.", false)]
    [InlineData("app.info.", false)]
    [InlineData("app.1info", false)]
    public void MethodNames_AreAreaDotVerb(string name, bool valid) => AppMessageCodec.IsMethodName(name).Should().Be(valid);

    [Fact]
    public void IsAppMessage_DoesNotScanMessagesOverTheLimit()
    {
        var padding = new string(' ', AppMessageCodec.MaximumRequestLength);
        AppMessageCodec.IsAppMessage("""{"v":2,"kind":"cancel","id":1}""" + padding).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{"v":2,"kind":"req","id":1,"type":"app.info","payload":{},"v":1}""")]
    [InlineData("""{"v":2,"kind":"req","id":1,"id":2,"type":"app.info","payload":{}}""")]
    [InlineData("""{"v":2,"kind":"cancel","id":1,"kind":"cancel"}""")]
    public void Decode_RejectsDuplicateMembers(string json) =>
        AppMessageCodec.Decode(json, out _).Should().BeNull();

    [Fact]
    public void Decode_RejectsALoneSurrogate_WithoutThrowing()
    {
        var act = () => AppMessageCodec.Decode("""{"v":2,"kind":"req","id":1,"type":"app.\ud800","payload":{}}""", out _);
        act.Should().NotThrow().Which.Should().BeNull();
    }

    private sealed record Inner([property: Range(0, 10)] int Value);

    private sealed record Outer(Inner Single, IReadOnlyList<Inner> Many);

    [Fact]
    public void Validation_RecursesIntoNestedRecordsAndLists()
    {
        ContractValidator.IsValid(new Outer(new Inner(5), [new Inner(1), new Inner(10)])).Should().BeTrue();
        ContractValidator.IsValid(new Outer(new Inner(11), [])).Should().BeFalse();
        ContractValidator.IsValid(new Outer(new Inner(5), [new Inner(1), new Inner(-1)])).Should().BeFalse();
    }

    [Fact]
    public void Event_LargerThanTheLimit_IsAProgrammingError()
    {
        var huge = new AppEvent<AppInfoResponse>("test.huge", AppJsonContext.Default.AppInfoResponse);
        var act = () => AppMessageCodec.Event(huge, SampleInfo with { Version = new string('9', AppMessageCodec.MaximumEventLength) });
        act.Should().Throw<InvalidOperationException>();
    }

    private static string Pretty(string json) => BridgeContractTests.Pretty(json);
}
