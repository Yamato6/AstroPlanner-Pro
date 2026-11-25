using System.Text.Json;
using AstroPlanner.Application.Bridge;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Tests.Bridge;

/// <summary>
/// <c>landscape.*</c> (spec §8.5): the active saved site's landscape, chosen in Settings › Sky and
/// saved with the site, and its horizon copied into the site's mask on request; and the media
/// folder's manifest (<see cref="MediaLibrary"/>).
/// </summary>
public sealed class LandscapeEndpointsTests
{
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private static readonly Location Home = new(
        "home", "Home", 21.88, -102.29, 1888, "America/Mexico_City", "Central Standard Time (Mexico)",
        new HorizonMask([new HorizonPoint(90, 15)]));

    /// <summary>A manifest like the DataBuilder's: two landscapes, one with a 20° hill at azimuth 100°–109°.</summary>
    internal static string Manifest()
    {
        var flat = string.Join(',', Enumerable.Repeat("1.5", 360));
        var hill = string.Join(',', Enumerable.Range(0, 360).Select(d => d is >= 100 and < 110 ? "20" : "-0.5"));
        const string Credit = """{"author":"Greg Zaal, Poly Haven","license":"CC0 1.0","url":"https://polyhaven.com/a/x","changes":"sky"}""";
        return $$$"""
            {"version":1,
             "panorama":{"file":"panorama/milky-way.webp","width":4096,"height":2048,"sha256":"00",
                         "credit":{"author":"ESO/S. Brunier","license":"CC BY 4.0","url":"https://www.eso.org/","changes":"stars removed"}},
             "landscapes":[
               {"id":"open-field","name":"Open field","file":"landscapes/open-field.webp","width":4096,"height":1400,
                "topAltitudeDegrees":3,"horizon":[{{{flat}}}],"sha256":"00","credit":{{{Credit}}}},
               {"id":"mountains","name":"Mountains","file":"landscapes/mountains.webp","width":4096,"height":1200,
                "topAltitudeDegrees":21,"horizon":[{{{hill}}}],"sha256":"00","credit":{{{Credit}}}},
               {"id":"Bad Id","name":"Broken","file":"landscapes/x.webp","width":1,"height":1,
                "topAltitudeDegrees":0,"horizon":[1],"sha256":"00","credit":{{{Credit}}}}
             ]}
            """;
    }

    private readonly FakeLocationRepository _repository = new();
    private readonly List<string> _sent = [];
    private readonly ManualTimeProvider _eventClock = new(WinterNightUtc);

    private (ShellViewModel Shell, RpcDispatcher Rpc) Create(MediaLibrary media, bool savedSite = true)
    {
        _repository.Locations.Add(Home);
        var settings = new FakeSettingsService(new ObserverSettings { IsManualLocation = true, ActiveLocationId = savedSite ? "home" : null });
        var shell = TestShell.Create(settings: settings, locations: _repository, time: new ManualTimeProvider(WinterNightUtc), media: media);
        var rpc = new RpcDispatcher(_sent.Add, (_, _) => { });
        new LandscapeEndpoints(shell.Locations, shell.Observer, shell.Sky.Media, new SynchronizationContextUiDispatcher(null), _eventClock)
            .Register(rpc);
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

    [Fact]
    public void Manifest_KeepsWellFormedLandscapes_AndResolvesIds()
    {
        var media = MediaLibrary.Parse(Manifest());

        media.IsAvailable.Should().BeTrue();
        media.BaseUrl.Should().Be("https://media.astroplanner/");
        media.PanoramaCredit!.Author.Should().Be("ESO/S. Brunier");
        media.Landscapes.Select(l => l.Id).Should().Equal("open-field", "mountains");
        media.Resolve("mountains").Should().Be("mountains");
        media.Resolve(Landscapes.Flat).Should().Be(Landscapes.Flat);
        media.Resolve("removed-one").Should().BeNull("a landscape no longer installed falls back to the profile");
        media.Resolve(null).Should().BeNull();
        MediaLibrary.None.IsAvailable.Should().BeFalse();
        MediaLibrary.None.BaseUrl.Should().BeNull();
    }

    [Fact]
    public void LandscapeHorizon_BecomesAMaskEveryTwoDegrees_TheHighestGround_NeverBelowZero()
    {
        var mountains = MediaLibrary.Parse(Manifest()).Find("mountains")!;

        var mask = mountains.ToHorizonMask();

        mask.Points.Should().HaveCount(180);
        mask.Points[0].Should().Be(new HorizonPoint(1, 0));
        mask.AltitudeAt(105).Should().Be(20);
        mask.AltitudeAt(99).Should().Be(0);
    }

    [Fact]
    public void Load_IsNoneWithoutAReadableManifest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));
        try
        {
            MediaLibrary.Load(directory).Should().BeSameAs(MediaLibrary.None);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, MediaLibrary.ManifestFileName), "{not json");
            MediaLibrary.Load(directory).Should().BeSameAs(MediaLibrary.None);
            File.WriteAllText(Path.Combine(directory, MediaLibrary.ManifestFileName), Manifest());
            MediaLibrary.Load(directory).Landscapes.Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Get_ListsTheLandscapes_ForTheActiveSavedSite()
    {
        var (_, rpc) = Create(MediaLibrary.Parse(Manifest()));

        var snapshot = await CallAsync(rpc, "landscape.get");

        snapshot.GetProperty("mediaAvailable").GetBoolean().Should().BeTrue();
        snapshot.GetProperty("siteName").GetString().Should().Be("Home");
        snapshot.GetProperty("landscapeId").ValueKind.Should().Be(JsonValueKind.Null);
        snapshot.GetProperty("landscapes").EnumerateArray().Select(l => l.GetProperty("name").GetString())
            .Should().Equal("Open field", "Mountains");
        snapshot.GetProperty("horizonFromLandscape").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Set_SavesTheLandscapeWithTheSite_AndTheSkyGetsIt()
    {
        var (shell, rpc) = Create(MediaLibrary.Parse(Manifest()));

        var snapshot = await CallAsync(rpc, "landscape.set", """{"landscapeId":"mountains"}""");

        snapshot.GetProperty("landscapeId").GetString().Should().Be("mountains");
        _repository.Locations.Single().LandscapeId.Should().Be("mountains");
        shell.Observer.LandscapeId.Should().Be("mountains");
        shell.Observer.HorizonMask.Should().Be(Home.HorizonMask, "choosing a landscape does not change the calculations");

        await CallAsync(rpc, "landscape.set", """{"landscapeId":"not-installed"}""");
        _repository.Locations.Single().LandscapeId.Should().Be("mountains", "unknown ids are ignored");

        await CallAsync(rpc, "landscape.set", """{"landscapeId":"flat"}""");
        shell.Observer.LandscapeId.Should().Be(Landscapes.Flat);

        snapshot = await CallAsync(rpc, "landscape.set", """{"landscapeId":null}""");
        snapshot.GetProperty("landscapeId").ValueKind.Should().Be(JsonValueKind.Null);
        _repository.Locations.Single().LandscapeId.Should().BeNull();
    }

    [Fact]
    public async Task UseHorizon_CopiesTheLandscapeHorizonIntoTheSiteMask()
    {
        var (shell, rpc) = Create(MediaLibrary.Parse(Manifest()));
        await CallAsync(rpc, "landscape.set", """{"landscapeId":"mountains"}""");

        var snapshot = await CallAsync(rpc, "landscape.useHorizon");

        snapshot.GetProperty("horizonFromLandscape").GetBoolean().Should().BeTrue();
        _repository.Locations.Single().HorizonMask.AltitudeAt(105).Should().Be(20);
        shell.Observer.HorizonMask.AltitudeAt(105).Should().Be(20);
    }

    [Fact]
    public async Task WithoutASavedSiteOrMedia_NothingIsChosen()
    {
        var (shell, rpc) = Create(MediaLibrary.None, savedSite: false);

        var snapshot = await CallAsync(rpc, "landscape.set", """{"landscapeId":"flat"}""");

        snapshot.GetProperty("mediaAvailable").GetBoolean().Should().BeFalse();
        snapshot.GetProperty("siteName").ValueKind.Should().Be(JsonValueKind.Null);
        snapshot.GetProperty("landscapes").GetArrayLength().Should().Be(0);
        shell.Observer.LandscapeId.Should().BeNull();
        (await CallAsync(rpc, "landscape.useHorizon")).GetProperty("horizonFromLandscape").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ChangesElsewhere_AreAnnounced()
    {
        var (shell, rpc) = Create(MediaLibrary.Parse(Manifest()));
        await CallAsync(rpc, "landscape.get");
        _sent.Clear();

        shell.Observer.LandscapeId = "open-field";
        _eventClock.LastTimer!.Fire();

        _sent.Should().Contain(m => m.Contains("\"landscape.changed\"", StringComparison.Ordinal));
    }
}
