using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>
/// <c>ui.*</c> (night vision, reduce transparency, full screen), <c>layout.*</c> (the window
/// layout) and <c>app.about</c> / <c>app.openLink</c> / <c>app.openDataFolder</c>.
/// </summary>
public sealed class InterfaceEndpointsTests
{
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _eventClock = new(WinterNightUtc);
    private readonly FakeInterfaceStore _store = new();
    private readonly FakeAppHost _host = new();
    private readonly List<string> _sent = [];
    private readonly List<string> _log = [];

    private (ShellViewModel Shell, RpcDispatcher Rpc) Create()
    {
        var shell = TestShell.Create(time: new ManualTimeProvider(WinterNightUtc));
        var rpc = new RpcDispatcher(_sent.Add, (message, _) => _log.Add(message));
        var ui = new SynchronizationContextUiDispatcher(null);
        new InterfaceEndpoints(shell.Settings, _store, _host, new NullLogger(), ui, _eventClock).Register(rpc);
        new LayoutEndpoints(_store, new NullLogger()).Register(rpc);
        new AppInfoEndpoints(shell.Settings.Theme, new AppVersion(new Version(3, 0, 0), "dev"), _host).Register(rpc);
        return (shell, rpc);
    }

    private async Task<JsonElement> CallAsync(RpcDispatcher rpc, string method, string payload = "{}")
    {
        _sent.Clear();
        await rpc.HandleAsync($$$"""{"v":2,"kind":"req","id":1,"type":"{{{method}}}","payload":{{{payload}}}}""");
        var response = JsonElement.Parse(_sent.Single(m => m.Contains("\"res\"", StringComparison.Ordinal)));
        response.TryGetProperty("error", out var error).Should().BeFalse($"{method} must answer within its contract ({error})");
        return response.GetProperty("payload");
    }

    private List<JsonElement> Events(string name) =>
        [.. _sent.Select(m => JsonElement.Parse(m)).Where(m => m.GetProperty("type").GetString() == name)];

    [Fact]
    public async Task UiGet_ReturnsTheSavedThemeTransparencyAndTheWindowsFullScreen()
    {
        _store.Preferences = new InterfacePreferences { ReduceTransparency = true };
        var (_, rpc) = Create();

        var ui = await CallAsync(rpc, "ui.get");

        ui.GetProperty("theme").GetString().Should().Be("dark");
        ui.GetProperty("reduceTransparency").GetBoolean().Should().BeTrue();
        ui.GetProperty("isFullScreen").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task SetTheme_SwitchesNightVisionLikeTheClassicWindow_AndAnswersWithTheNewRevision()
    {
        var (shell, rpc) = Create();

        var ui = await CallAsync(rpc, "ui.setTheme", """{"theme":"night"}""");

        shell.IsNightTheme.Should().BeTrue();
        shell.Settings.Theme.Current.Should().Be(AppTheme.Night);
        ui.GetProperty("theme").GetString().Should().Be("night");
        ui.GetProperty("revision").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ReduceTransparency_IsSaved_OnlyWhenItChanges()
    {
        var (_, rpc) = Create();

        var on = await CallAsync(rpc, "ui.setReduceTransparency", """{"enabled":true}""");
        await CallAsync(rpc, "ui.setReduceTransparency", """{"enabled":true}""");

        on.GetProperty("reduceTransparency").GetBoolean().Should().BeTrue();
        _store.Preferences.ReduceTransparency.Should().BeTrue();
        _store.PreferenceSaves.Should().Be(1);
    }

    [Fact]
    public async Task AutoHide_IsOnByDefault_AndSavedWhenTurnedOff()
    {
        var (_, rpc) = Create();

        var before = await CallAsync(rpc, "ui.get");
        var off = await CallAsync(rpc, "ui.setAutoHide", """{"enabled":false}""");

        before.GetProperty("autoHideToolbars").GetBoolean().Should().BeTrue();
        off.GetProperty("autoHideToolbars").GetBoolean().Should().BeFalse();
        _store.Preferences.AutoHideToolbars.Should().BeFalse();
        _store.Preferences.ReduceTransparency.Should().BeFalse("the other preferences stay as they were");
    }

    [Fact]
    public async Task ReduceTransparency_ThatCannotBeSaved_StillAppliesForTheSession()
    {
        var (_, rpc) = Create();
        _store.Failure = new IOException("locked");

        var ui = await CallAsync(rpc, "ui.setReduceTransparency", """{"enabled":true}""");

        ui.GetProperty("reduceTransparency").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task FullScreen_GoesToTheHostWindow()
    {
        var (_, rpc) = Create();

        var ui = await CallAsync(rpc, "ui.setFullScreen", """{"enabled":true}""");

        _host.IsFullScreen.Should().BeTrue();
        ui.GetProperty("isFullScreen").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void ChangesMadeElsewhere_AreAnnouncedOnce()
    {
        var (shell, _) = Create();
        _sent.Clear();

        shell.IsNightTheme = true; // the classic window's switch
        _host.SetFullScreen(true); // the window, e.g. F11 handled by the host
        _eventClock.LastTimer!.Fire();

        var changed = Events("ui.changed").Should().ContainSingle().Subject;
        changed.GetProperty("payload").GetProperty("revision").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Layout_RoundTripsThroughTheStore()
    {
        var (_, rpc) = Create();
        const string window = """{"id":"settings","isOpen":true,"isMinimized":false,"arrangement":"dockRight","x":120,"y":80,"width":420,"height":560,"dockWidth":380,"zOrder":3}""";

        await CallAsync(rpc, "layout.save", $$"""{"windows":[{{window}}]}""");
        var loaded = await CallAsync(rpc, "layout.load");

        _store.Layouts.Should().ContainSingle().Which.Should().Be(
            new WindowLayout("settings", true, false, WindowArrangement.DockRight, 120, 80, 420, 560, 380, 3));
        loaded.GetProperty("windows")[0].GetRawText().Should().Be(window);
    }

    [Fact]
    public async Task LayoutLoad_LeavesOutWindowsThisVersionDoesNotHave()
    {
        _store.Layouts =
        [
            new WindowLayout("help", true, false, WindowArrangement.Floating, 10, 10, 400, 300, 360, 1),
            new WindowLayout("telescope", true, false, WindowArrangement.Floating, 10, 10, 400, 300, 360, 2),
        ];
        var (_, rpc) = Create();

        var loaded = await CallAsync(rpc, "layout.load");

        loaded.GetProperty("windows").EnumerateArray().Select(w => w.GetProperty("id").GetString()).Should().Equal("help");
    }

    [Fact]
    public async Task Layout_ThatCannotBeReadOrSaved_IsNotAnError()
    {
        var (_, rpc) = Create();
        _store.Failure = new IOException("locked");

        await CallAsync(rpc, "layout.save", """{"windows":[]}""");
        var loaded = await CallAsync(rpc, "layout.load");

        loaded.GetProperty("windows").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task LayoutSave_RejectsAnUnknownWindow()
    {
        var (_, rpc) = Create();
        _sent.Clear();

        await rpc.HandleAsync("""{"v":2,"kind":"req","id":1,"type":"layout.save","payload":{"windows":[{"id":"telescope","isOpen":true,"isMinimized":false,"arrangement":"floating","x":0,"y":0,"width":400,"height":300,"dockWidth":360,"zOrder":1}]}}""");

        JsonElement.Parse(_sent.Single()).GetProperty("error").GetProperty("code").GetString().Should().Be(RpcErrorCodes.BadRequest);
        _store.Layouts.Should().BeEmpty();
    }

    [Fact]
    public async Task About_HasTheVersionCreditsAndDataFolder()
    {
        var (_, rpc) = Create();

        var about = await CallAsync(rpc, "app.about");

        about.GetProperty("version").GetString().Should().Be("3.0.0-dev");
        about.GetProperty("dataFolder").GetString().Should().Be(_host.DataFolder);
        var credits = about.GetProperty("credits").EnumerateArray().ToList();
        credits.Should().HaveCountGreaterThan(4);
        credits.Should().OnlyContain(c => !c.GetProperty("title").GetString()!.EndsWith(':'));
        credits.Should().Contain(c => c.GetProperty("url").GetString() == "https://open-meteo.com/", "CC BY 4.0 asks for the link");
        about.GetProperty("repositoryUrl").GetString().Should().Be(AppInfoEndpoints.RepositoryUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://open-meteo.com/", true)]
    [InlineData("https://github.com/Yamato6/AstroPlanner-Pro", true)]
    [InlineData("http://open-meteo.com/", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("not a link", false)]
    public async Task OpenLink_OpensOnlyAllowedHttpsLinks(string url, bool opened)
    {
        var (_, rpc) = Create();

        var answer = await CallAsync(rpc, "app.openLink", JsonSerializer.Serialize(new { url }));

        answer.GetProperty("opened").GetBoolean().Should().Be(opened);
        _host.OpenedLinks.Should().HaveCount(opened ? 1 : 0);
    }

    [Fact]
    public async Task OpenDataFolder_AsksTheHost()
    {
        var (_, rpc) = Create();

        await CallAsync(rpc, "app.openDataFolder");

        _host.DataFolderOpened.Should().Be(1);
    }
}
