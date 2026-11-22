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

    internal static IReadOnlyDictionary<string, string> OutgoingSamples() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.info.response"] = AppMessageCodec.Response(1, AppMethods.AppInfo.Name, SampleInfo, AppMethods.AppInfo.Response),
        ["app.info.error"] = AppMessageCodec.Error(2, AppMethods.AppInfo.Name, RpcErrorCodes.Timeout, "The host did not answer in time."),
        ["theme.changed.event"] = AppMessageCodec.Event(AppEvents.ThemeChanged, new ThemeChangedEvent(AppThemes.Night)),
    };

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
    public void Event_LargerThanTheLimit_IsAProgrammingError()
    {
        var huge = new AppEvent<AppInfoResponse>("test.huge", AppJsonContext.Default.AppInfoResponse);
        var act = () => AppMessageCodec.Event(huge, SampleInfo with { Version = new string('9', AppMessageCodec.MaximumEventLength) });
        act.Should().Throw<InvalidOperationException>();
    }

    private static string Pretty(string json) => BridgeContractTests.Pretty(json);
}
