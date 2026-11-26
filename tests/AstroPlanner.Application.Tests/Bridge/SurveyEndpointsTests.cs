using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.Services;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>
/// The survey layer's host side (spec §5.4, §8.3): the tile proxy behind https://tiles.astroplanner/
/// (only well-formed requests for a known survey reach the network) and <c>survey.*</c>
/// (Settings › Survey: survey, mode, tile cache).
/// </summary>
public sealed class SurveyEndpointsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 4, 0, 0, TimeSpan.Zero);
    private static readonly HipsSurvey Dss = new("DSSColor", "DSS2 colour", 9);
    private static readonly HipsSurvey PanStarrs = new("PanSTARRS-DR1-color", "Pan-STARRS DR1 colour", 11);

    /// <summary>Two surveys; records every tile asked of the network and answers with its key.</summary>
    private sealed class FakeTiles : ISurveyTileSource
    {
        public List<HipsTileKey> Downloads { get; } = [];

        public HashSet<HipsTileKey> Cached { get; } = [];

        public bool Offline { get; set; }

        public IReadOnlyList<HipsSurvey> Surveys { get; } = [Dss, PanStarrs];

        public Task<HipsTile> GetTileAsync(HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken = default)
        {
            Downloads.Add(key);
            return Offline
                ? throw new OnlineServiceException("No connection.")
                : Task.FromResult(new HipsTile([(byte)key.Order, 0xFF, 0xD8], "CDS"));
        }

        public HipsTile? GetCachedTile(HipsSurvey survey, HipsTileKey key) =>
            Cached.Contains(key) ? new HipsTile([1], "cache") : null;

        public string CreditFor(HipsSurvey survey) => survey.Id + " credit";
    }

    private sealed class FakeCache : ISizedCache
    {
        public long LimitBytes { get; set; }

        public long SizeBytes { get; set; } = 300L * 1024 * 1024;

        public int Clears { get; private set; }

        public CachedResponse? Read(string service, string key) => null;

        public void Write(string service, string key, byte[] content, DateTime storedAtUtc)
        {
        }

        public void Clear()
        {
            Clears++;
            SizeBytes = 0;
        }
    }

    private readonly FakeTiles _tiles = new();
    private readonly FakeCache _cache = new();
    private readonly FakeInterfaceStore _store = new();
    private readonly OnlineInfoState _online = new();
    private readonly List<string> _sent = [];
    private readonly ManualTimeProvider _eventClock = new(Now);

    private RpcDispatcher Create()
    {
        var rpc = new RpcDispatcher(_sent.Add, (_, _) => { });
        new SurveyEndpoints(_tiles, _store, _cache, _online, new NullLogger(), new SynchronizationContextUiDispatcher(null), _eventClock)
            .Register(rpc);
        return rpc;
    }

    private async Task<JsonElement> CallAsync(RpcDispatcher rpc, string method, string payload = "{}")
    {
        _sent.Clear();
        await rpc.HandleAsync($$$"""{"v":2,"kind":"req","id":1,"type":"{{{method}}}","payload":{{{payload}}}}""");
        var response = JsonElement.Parse(_sent.Single(m => m.Contains("\"res\"", StringComparison.Ordinal)));
        response.TryGetProperty("error", out var error).Should().BeFalse($"{method} must answer within its contract ({error})");
        return response.GetProperty("payload");
    }

    [Theory]
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix0.jpg", 3, 0L)]
    [InlineData("https://tiles.astroplanner/DSSColor/Norder9/Dir3140000/Npix3145727.jpg", 9, 3145727L)]
    [InlineData("https://tiles.astroplanner/PanSTARRS-DR1-color/Norder11/Dir50330000/Npix50331647.jpg", 11, 50331647L)]
    [InlineData("https://TILES.astroplanner/DSSColor/Norder5/Dir10000/Npix12287.jpg", 5, 12287L)]
    public void Proxy_ReadsWellFormedTileRequests(string uri, int order, long index)
    {
        SurveyTileProxy.TryParse(uri, _tiles.Surveys, out var survey, out var key).Should().BeTrue();
        survey.Should().NotBeNull();
        key.Should().Be(new HipsTileKey(order, index));
    }

    [Fact]
    public void Proxy_ReadsTheAllskyImage()
    {
        SurveyTileProxy.TryParse("https://tiles.astroplanner/DSSColor/Norder3/Allsky.jpg", _tiles.Surveys, out var survey, out var key)
            .Should().BeTrue();
        survey!.Id.Should().Be("DSSColor");
        key.IsAllsky.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix0.jpg")]               // not https
    [InlineData("https://tiles.astroplanner:8443/DSSColor/Norder3/Dir0/Npix0.jpg")]         // another port
    [InlineData("https://evil.example/DSSColor/Norder3/Dir0/Npix0.jpg")]                    // another host
    [InlineData("https://tiles.astroplanner/Mellinger/Norder3/Dir0/Npix0.jpg")]             // not on the allow-list
    [InlineData("https://tiles.astroplanner/dsscolor/Norder3/Dir0/Npix0.jpg")]              // ids are case-sensitive
    [InlineData("https://tiles.astroplanner/DSSColor/Norder10/Dir0/Npix0.jpg")]             // past the survey's max order
    [InlineData("https://tiles.astroplanner/DSSColor/Norder2/Dir0/Npix0.jpg")]              // below order 3
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix768.jpg")]            // no such pixel at order 3
    [InlineData("https://tiles.astroplanner/DSSColor/Norder5/Dir0/Npix12287.jpg")]          // wrong directory
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix-1.jpg")]             // a sign
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix01.jpg")]             // leading zero
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix0.png")]              // not a JPEG
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix0.jpg?x=1")]          // a query
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/../../Npix0.jpg")]             // a climb out
    [InlineData("https://tiles.astroplanner/DSSColor/Norder4/Allsky.jpg")]                  // Allsky is order 3
    [InlineData("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix0.jpg/more")]         // extra segment
    [InlineData("https://tiles.astroplanner/")]
    [InlineData("not a uri")]
    [InlineData(null)]
    public void Proxy_RejectsAnythingElse(string? uri)
    {
        SurveyTileProxy.TryParse(uri, _tiles.Surveys, out _, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Proxy_AsksTheNetworkOnlyForValidTiles_AndAnswersNotFoundWhenItFails()
    {
        var proxy = new SurveyTileProxy(_tiles, _online);

        (await proxy.GetAsync("https://tiles.astroplanner/Mellinger/Norder3/Dir0/Npix0.jpg")).Should().BeNull();
        (await proxy.GetAsync("https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix768.jpg")).Should().BeNull();
        _tiles.Downloads.Should().BeEmpty();

        (await proxy.GetAsync("https://tiles.astroplanner/DSSColor/Norder4/Dir0/Npix7.jpg")).Should().Equal(4, 0xFF, 0xD8);
        _tiles.Downloads.Should().Equal(new HipsTileKey(4, 7));

        _tiles.Offline = true;
        (await proxy.GetAsync("https://tiles.astroplanner/DSSColor/Norder4/Dir0/Npix8.jpg")).Should().BeNull();
    }

    [Fact]
    public async Task Proxy_WithOnlineImagesOff_ServesOnlyCachedTiles()
    {
        _online.Apply(images: false, wikipedia: true, nameResolver: true);
        _tiles.Cached.Add(new HipsTileKey(5, 1));
        var proxy = new SurveyTileProxy(_tiles, _online);

        (await proxy.GetAsync("https://tiles.astroplanner/DSSColor/Norder5/Dir0/Npix1.jpg")).Should().Equal(1);
        (await proxy.GetAsync("https://tiles.astroplanner/DSSColor/Norder5/Dir0/Npix2.jpg")).Should().BeNull();
        _tiles.Downloads.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_ListsTheSurveys_WithDssByDefault_AndAppliesTheSavedCacheLimit()
    {
        _store.Survey = new SurveyPreferences { CacheLimitBytes = 2048L * 1024 * 1024 };
        var rpc = Create();

        var snapshot = await CallAsync(rpc, "survey.get");

        snapshot.GetProperty("tilesBaseUrl").GetString().Should().Be("https://tiles.astroplanner/");
        snapshot.GetProperty("surveys").EnumerateArray().Select(s => s.GetProperty("id").GetString())
            .Should().Equal("DSSColor", "PanSTARRS-DR1-color");
        snapshot.GetProperty("surveys")[1].GetProperty("maxOrder").GetInt32().Should().Be(11);
        snapshot.GetProperty("surveys")[0].GetProperty("credit").GetString().Should().Be("DSSColor credit");
        snapshot.GetProperty("surveyId").GetString().Should().Be("DSSColor");
        snapshot.GetProperty("mode").GetString().Should().Be("realistic");
        snapshot.GetProperty("downloads").GetBoolean().Should().BeTrue();
        snapshot.GetProperty("cacheMegabytes").GetInt32().Should().Be(300);
        snapshot.GetProperty("cacheLimitMegabytes").GetInt32().Should().Be(2048);
        _cache.LimitBytes.Should().Be(2048L * 1024 * 1024);
    }

    [Fact]
    public async Task Set_ChangesAndSaves_KnownSurveysOrNone_AndIgnoresOthers()
    {
        var rpc = Create();

        (await CallAsync(rpc, "survey.set", """{"surveyId":"PanSTARRS-DR1-color"}""")).GetProperty("surveyId").GetString()
            .Should().Be("PanSTARRS-DR1-color");
        (await CallAsync(rpc, "survey.set", """{"surveyId":"Mellinger"}""")).GetProperty("surveyId").GetString()
            .Should().Be("PanSTARRS-DR1-color");
        (await CallAsync(rpc, "survey.set", """{"surveyId":null}""")).GetProperty("surveyId").ValueKind.Should().Be(JsonValueKind.Null);
        _store.Survey.SurveyId.Should().BeNull();

        (await CallAsync(rpc, "survey.setMode", """{"mode":"survey"}""")).GetProperty("mode").GetString().Should().Be("survey");
        _store.Survey.Mode.Should().Be("survey");
    }

    [Fact]
    public async Task CacheLimit_AndClear_ReachTheTileCache()
    {
        var rpc = Create();

        (await CallAsync(rpc, "survey.setCacheLimit", """{"cacheLimitMegabytes":512}""")).GetProperty("cacheLimitMegabytes").GetInt32()
            .Should().Be(512);
        _cache.LimitBytes.Should().Be(512L * 1024 * 1024);
        _store.Survey.CacheLimitBytes.Should().Be(512L * 1024 * 1024);

        (await CallAsync(rpc, "survey.clearCache")).GetProperty("cacheMegabytes").GetInt32().Should().Be(0);
        _cache.Clears.Should().Be(1);
    }

    [Fact]
    public async Task OutOfRangeLimits_AreRefusedByTheContract()
    {
        var rpc = Create();
        _sent.Clear();
        await rpc.HandleAsync("""{"v":2,"kind":"req","id":1,"type":"survey.setCacheLimit","payload":{"cacheLimitMegabytes":64}}""");

        _sent.Single().Should().Contain("\"error\"");
        _cache.LimitBytes.Should().Be(SurveyPreferences.DefaultCacheLimitBytes);
    }

    [Fact]
    public async Task DamagedSavedSettings_FallBackToTheDefaults()
    {
        _store.Survey = new SurveyPreferences { SurveyId = "Gone", Mode = "psychedelic", CacheLimitBytes = 5 };
        var rpc = Create();

        var snapshot = await CallAsync(rpc, "survey.get");

        snapshot.GetProperty("surveyId").GetString().Should().Be("DSSColor");
        snapshot.GetProperty("mode").GetString().Should().Be("realistic");
        snapshot.GetProperty("cacheLimitMegabytes").GetInt32().Should().Be(256);
    }

    [Fact]
    public async Task TurningOnlineImagesOff_IsAnnounced_AndShown()
    {
        var rpc = Create();
        await CallAsync(rpc, "survey.get");
        _sent.Clear();

        _online.Apply(images: false, wikipedia: true, nameResolver: true);
        _eventClock.LastTimer!.Fire();

        _sent.Should().Contain(m => m.Contains("\"survey.changed\"", StringComparison.Ordinal));
        (await CallAsync(rpc, "survey.get")).GetProperty("downloads").GetBoolean().Should().BeFalse();
    }
}
