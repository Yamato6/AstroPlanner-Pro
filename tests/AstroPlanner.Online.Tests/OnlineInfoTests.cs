using System.Net;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Imaging;
using AstroPlanner.Online.Info;

namespace AstroPlanner.Online.Tests;

/// <summary>M4b: HiPS tiles with mirrors, Wikipedia + Commons and Sesame (recorded fixtures only).</summary>
public sealed class OnlineInfoTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 21, 0, 0, TimeSpan.Zero);
    private static readonly HipsSurvey Dss = HipsTileService.Definitions[0].Survey;

    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "online", name));

    private static HttpResponseMessage Bytes(byte[] content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new ByteArrayContent(content) };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new ByteArrayContent([]) };

    private sealed class MemoryCache : IOnlineCache
    {
        public Dictionary<(string, string), CachedResponse> Entries { get; } = [];
        public CachedResponse? Read(string service, string key) => Entries.GetValueOrDefault((service, key));
        public void Write(string service, string key, byte[] content, DateTime storedAtUtc) => Entries[(service, key)] = new(content, storedAtUtc);
        public long SizeBytes => Entries.Values.Sum(e => (long)e.Content.Length);
        public void Clear() => Entries.Clear();
    }

    private static CelestialObject Messier42 => new("M42", "Orion Nebula", "Orion", CelestialObjectType.HiiRegion, 4.0, new EquatorialCoordinates(5.588, -5.39))
    {
        Designations = ["NGC 1976", "C 999"]
    };

    // --- HiPS tiles ------------------------------------------------------------------------------

    [Fact]
    public void TilePaths_FollowHips()
    {
        HipsTileService.TilePath(new HipsTileKey(5, 115)).Should().Be("Norder5/Dir0/Npix115.jpg");
        HipsTileService.TilePath(new HipsTileKey(9, 1234567)).Should().Be("Norder9/Dir1230000/Npix1234567.jpg");
        HipsTileService.TilePath(HipsTileKey.Allsky).Should().Be("Norder3/Allsky.jpg");
    }

    [Fact]
    public async Task Tile_FallsBackToTheEsaMirror_AndIsCachedForGood()
    {
        var jpeg = Fixture("hips-dsscolor-norder5-npix115.jpg");
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "alasky.cds.unistra.fr" ? Status(HttpStatusCode.ServiceUnavailable) : Bytes(jpeg));
        var cache = new MemoryCache();
        var clock = new ManualTimeProvider(Now);
        using var service = new HipsTileService(new HttpClient(handler), cache, clock);

        var tile = await service.GetTileAsync(Dss, new HipsTileKey(5, 115));

        tile.Source.Should().Be("ESA ESASky");
        tile.Content.Should().Equal(jpeg);
        handler.Requests.Select(u => u.ToString()).Should().Equal(
            "https://alasky.cds.unistra.fr/DSS/DSSColor/Norder5/Dir0/Npix115.jpg",
            "https://skies.esac.esa.int/DSSColor/Norder5/Dir0/Npix115.jpg");

        // The failed mirror is skipped for a while; a cached tile needs no request at all, even a year later.
        await service.GetTileAsync(Dss, new HipsTileKey(5, 116));
        handler.Requests.Should().HaveCount(3);
        handler.Requests[^1].Host.Should().Be("skies.esac.esa.int");
        clock.UtcNow += TimeSpan.FromDays(365);
        (await service.GetTileAsync(Dss, new HipsTileKey(5, 115))).Source.Should().Be("cache");
        handler.Requests.Should().HaveCount(3);

        // After the pause the preferred mirror is asked again.
        await service.GetTileAsync(Dss, new HipsTileKey(5, 117));
        handler.Requests[^2].Host.Should().Be("alasky.cds.unistra.fr");
        service.CreditFor(Dss).Should().Contain("STScI/NASA").And.Contain("CDS");
    }

    [Fact]
    public async Task MirrorAnswering404_IsPausedAfterAFewTiles()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "alasky.cds.unistra.fr" ? Status(HttpStatusCode.NotFound) : Bytes([1, 2, 3]));
        using var service = new HipsTileService(new HttpClient(handler), null, new ManualTimeProvider(Now));

        for (var i = 0; i < 5; i++)
        {
            await service.GetTileAsync(Dss, new HipsTileKey(6, i));
        }

        handler.Requests.Count(u => u.Host == "alasky.cds.unistra.fr").Should().Be(HipsTileService.NotFoundLimit);
        handler.Requests.Count(u => u.Host == "skies.esac.esa.int").Should().Be(5);
    }

    [Fact]
    public async Task Offline_WithoutCache_SaysImagesAreUnavailable()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("offline"));
        using var service = new HipsTileService(new HttpClient(handler), new MemoryCache(), new ManualTimeProvider(Now));

        var act = () => service.GetTileAsync(Dss, new HipsTileKey(5, 115));

        (await act.Should().ThrowAsync<OnlineServiceException>()).Which.Message.Should().Contain("unavailable");
        FluentActions.Invoking(() => service.CreditFor(new HipsSurvey("nope", "Nope", 3))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Cancellation_IsNotTakenForAMirrorFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new StubHttpHandler(_ => Bytes([1]));
        using var service = new HipsTileService(new HttpClient(handler));

        var act = () => service.GetTileAsync(Dss, new HipsTileKey(5, 1), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- Wikipedia + Commons ---------------------------------------------------------------------

    [Fact]
    public void Titles_ArePlanetMessierNgcCaldwellAndName()
    {
        WikipediaSummaryService.TitlesFor(Messier42).Should().Equal("Messier 42", "NGC 1976", "Caldwell 999", "Orion Nebula");
        WikipediaSummaryService.TitlesFor(CelestialObject.CreatePlanet("mercury", "Mercury", Planet.Mercury)).Should().Equal("Mercury (planet)");
        WikipediaSummaryService.TitlesFor(new CelestialObject("IC 434", "", "Orion", CelestialObjectType.EmissionNebula, 7, new EquatorialCoordinates(5.68, -2.45)))
            .Should().Equal("IC 434");
        WikipediaSummaryService.TitlesFor(new CelestialObject("HIP 27989", "Betelgeuse", "Orion", CelestialObjectType.Star, 0.5, new EquatorialCoordinates(5.92, 7.4)))
            .Should().Equal("Betelgeuse");
    }

    [Fact]
    public void Titles_OfCatalogStars_TryTheStarArticle_ThenTheSpelledNames()
    {
        static CelestialObject Star(string id, string name, params string[] alternates) =>
            new(id, name, "Gemini", CelestialObjectType.DoubleStar, 1.6, new EquatorialCoordinates(7.58, 31.9))
            {
                Star = new StarInfo(1, 36850, null, "A1V", 0.03, 15.6, null, null, null),
                AlternateNames = alternates
            };

        WikipediaSummaryService.TitlesFor(Star("HIP 36850", "Castor", "α Gem", "Alpha Geminorum", "Alpha Geminorum"))
            .Should().Equal("Castor (star)", "Alpha Geminorum", "Castor");
        WikipediaSummaryService.TitlesFor(Star("HIP 104214", "61 Cyg", "61 Cygni")).Should().Equal("61 Cygni");
        WikipediaSummaryService.TitlesFor(Star("HIP 1", "ε Lyr", "Epsilon1 Lyrae", "Epsilon Lyrae")).Should().Equal("Epsilon1 Lyrae", "Epsilon Lyrae");
    }

    [Fact]
    public async Task Summary_FollowsTheRedirect_WithTheCommonsLicense()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host switch
        {
            "en.wikipedia.org" => Bytes(Fixture("wikipedia-summary-messier42.json")),
            "commons.wikimedia.org" => Bytes(Fixture("commons-extmetadata-m42.json")),
            _ => Status(HttpStatusCode.NotFound)
        });
        var cache = new MemoryCache();
        var service = new WikipediaSummaryService(new HttpClient(handler), cache, new ManualTimeProvider(Now));

        var summary = (await service.GetSummaryAsync(Messier42))!;

        summary.Title.Should().Be("Orion Nebula");
        summary.Description.Should().Be("Diffuse nebula in the constellation Orion");
        summary.Extract.Should().StartWith("The Orion Nebula is a diffuse nebula");
        summary.ArticleUrl.Should().Be(new Uri("https://en.wikipedia.org/wiki/Orion_Nebula"));
        summary.Image!.FileName.Should().Be("Orion Nebula - Hubble 2006 mosaic 18000.jpg");
        summary.Image.License.Should().Be("Public domain");
        summary.Image.Artist.Should().StartWith("NASA, ESA, M. Robberto");
        summary.Image.FilePageUrl.ToString().Should().Be("https://commons.wikimedia.org/wiki/File:Orion_Nebula_-_Hubble_2006_mosaic_18000.jpg");
        handler.Requests[0].ToString().Should().Be("https://en.wikipedia.org/api/rest_v1/page/summary/Messier_42");
        handler.Requests[1].Query.Should().Contain("titles=File:Orion_Nebula_-_Hubble_2006_mosaic_18000.jpg");

        // Cached for 30 days.
        await service.GetSummaryAsync(Messier42);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task MissingAndDisambiguationPages_AreSkipped_AndRemembered()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath.EndsWith("Castor", StringComparison.Ordinal)
            ? Bytes(Fixture("wikipedia-summary-disambiguation.json"))
            : Status(HttpStatusCode.NotFound));
        var cache = new MemoryCache();
        var service = new WikipediaSummaryService(new HttpClient(handler), cache, new ManualTimeProvider(Now));
        var castor = new CelestialObject("NGC 99999", "Castor", "Gemini", CelestialObjectType.Star, 1.6, new EquatorialCoordinates(7.58, 31.9));

        (await service.GetSummaryAsync(castor)).Should().BeNull();
        handler.Requests.Should().HaveCount(2);
        (await service.GetSummaryAsync(castor)).Should().BeNull();
        handler.Requests.Should().HaveCount(2, "a missing article is cached like any answer");
    }

    [Fact]
    public void Summary_OfANonAstronomicalArticle_IsIgnored()
    {
        var json = """{"type":"standard","title":"Castor","description":"Genus of rodents","extract":"Castor is a genus.","content_urls":{"desktop":{"page":"https://en.wikipedia.org/wiki/Castor_(genus)"}}}"""u8.ToArray();
        WikipediaSummaryService.ParseSummary(json).Should().BeNull();
        FluentActions.Invoking(() => WikipediaSummaryService.ParseSummary("not json"u8.ToArray())).Should().Throw<OnlineServiceException>();
    }

    [Fact]
    public void License_WithHtmlArtist_IsPlainText_AndNonCommonsImagesAreDropped()
    {
        // The recorded M42 answer with an HTML author and a CC license, as Commons gives for most images.
        var json = """{"query":{"pages":[{"imageinfo":[{"extmetadata":{"LicenseShortName":{"value":"CC BY-SA 4.0"},"LicenseUrl":{"value":"https://creativecommons.org/licenses/by-sa/4.0"},"Artist":{"value":"<a href=\"//commons.wikimedia.org/wiki/User:Someone\">Some&nbsp;One</a>\n (photo)"}}}]}]}}"""u8.ToArray();

        var license = WikipediaSummaryService.ParseLicense(json)!.Value;

        license.License.Should().Be("CC BY-SA 4.0");
        license.LicenseUrl.Should().Be(new Uri("https://creativecommons.org/licenses/by-sa/4.0"));
        license.Artist.Should().Be("Some One (photo)");
        WikipediaSummaryService.ParseLicense("""{"query":{"pages":[{"missing":true}]}}"""u8.ToArray()).Should().BeNull();
        WikipediaSummaryService.CommonsFileName("https://upload.wikimedia.org/wikipedia/en/a/ab/Poster.jpg").Should().BeNull("non-free images live outside Commons");
        WikipediaSummaryService.CommonsFileName("https://upload.wikimedia.org/wikipedia/commons/f/f3/Some_file%2C_2.jpg").Should().Be("Some file, 2.jpg");
    }

    [Fact]
    public async Task Summary_WithoutALicense_KeepsTheTextButDropsTheImage()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "en.wikipedia.org"
            ? Bytes(Fixture("wikipedia-summary-messier42.json"))
            : Status(HttpStatusCode.InternalServerError));
        var service = new WikipediaSummaryService(new HttpClient(handler), null, new ManualTimeProvider(Now));

        var summary = await service.GetSummaryAsync(Messier42);

        summary!.Image.Should().BeNull();
        summary.Title.Should().Be("Orion Nebula");
    }

    // --- Sesame ----------------------------------------------------------------------------------

    [Fact]
    public async Task Sesame_ResolvesThroughTheNextMirror_WhenCdsIsDown()
    {
        var handler = new StubHttpHandler(r => r.RequestUri!.Host == "cds.unistra.fr"
            ? throw new HttpRequestException("down")
            : Bytes(Fixture("sesame-barnard33.xml")));
        var cache = new MemoryCache();
        var resolver = new SesameResolver(new HttpClient(handler), cache, new ManualTimeProvider(Now));

        var resolved = (await resolver.ResolveAsync("  barnard   33 "))!;

        resolved.Query.Should().Be("barnard 33");
        resolved.Name.Should().Be("NAME Horsehead Nebula");
        resolved.ObjectType.Should().Be("DNe");
        resolved.Coordinates.RightAscensionHours.Should().BeApproximately(85.24583333 / 15, 1e-9);
        resolved.Coordinates.DeclinationDegrees.Should().BeApproximately(-2.45833333, 1e-9);
        resolved.Source.Should().Be("Sesame (CDS) · Simbad");
        handler.Requests.Select(u => u.Host).Should().Equal("cds.unistra.fr", "vizier.cfa.harvard.edu");
        handler.Requests[1].AbsoluteUri.Should().EndWith("/-oxp/SNV?barnard%2033");

        (await resolver.ResolveAsync("BARNARD 33")).Should().NotBeNull();
        handler.Requests.Should().HaveCount(2, "names are cached case-insensitively");
    }

    [Fact]
    public async Task Sesame_NothingFound_IsNull_AndInvalidNamesAreNotSent()
    {
        var handler = new StubHttpHandler(_ => Bytes(Fixture("sesame-nothing.xml")));
        var resolver = new SesameResolver(new HttpClient(handler), null, new ManualTimeProvider(Now));

        (await resolver.ResolveAsync("NoSuchObjectXyz123")).Should().BeNull();
        (await resolver.ResolveAsync("   ")).Should().BeNull();
        (await resolver.ResolveAsync(new string('x', 101))).Should().BeNull();
        handler.Requests.Should().HaveCount(1);
        var cds = """<Sesame><Target><Resolver name="Sim=Simbad, all IDs (via url)"><jradeg>83.82</jradeg><jdedeg>-5.39</jdedeg></Resolver></Target></Sesame>"""u8.ToArray();
        SesameResolver.Parse(cds, "m42")!.Should().BeEquivalentTo(new { Name = "m42", ObjectType = (string?)null, Source = "Sesame (CDS) · Simbad" });
        FluentActions.Invoking(() => SesameResolver.Parse("<html/>"u8.ToArray(), "x")).Should().Throw<OnlineServiceException>();
        FluentActions.Invoking(() => SesameResolver.Parse("not xml"u8.ToArray(), "x")).Should().Throw<OnlineServiceException>();
    }

    [Fact]
    public async Task Sesame_AllMirrorsDown_UsesTheStaleAnswer_ElseFails()
    {
        var cache = new MemoryCache();
        var clock = new ManualTimeProvider(Now);
        var online = new SesameResolver(new HttpClient(new StubHttpHandler(_ => Bytes(Fixture("sesame-barnard33.xml")))), cache, clock);
        await online.ResolveAsync("Barnard 33");

        clock.UtcNow += TimeSpan.FromDays(60);
        var offline = new SesameResolver(new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("offline"))), cache, clock);

        (await offline.ResolveAsync("Barnard 33"))!.Name.Should().Be("NAME Horsehead Nebula");
        var act = () => offline.ResolveAsync("M 999");
        (await act.Should().ThrowAsync<OnlineServiceException>()).Which.Message.Should().Be("No connection to Sesame (CDS).");
    }
}
