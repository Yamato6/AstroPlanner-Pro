using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>M4b: framing preview, Wikipedia description and the Sesame search fallback (fake services only).</summary>
public sealed class OnlineInfoUiTests
{
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);
    private static readonly HipsSurvey Dss = new("DSSColor", "DSS2 colour", 9);

    private static readonly ObserverSettings Aguascalientes = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = 21.88,
        LongitudeDegrees = -102.29,
        ElevationMeters = 1888,
        TimeZoneId = "Central Standard Time (Mexico)",
        MinimumAltitude = 20,
        MaximumMagnitude = 20,
        WeatherForecastEnabled = false,
        AstroForecastEnabled = false
    };

    private sealed class FakeTiles(Func<HipsTileKey, HipsTile>? serve = null) : ISurveyTileSource
    {
        public List<HipsTileKey> Requests { get; } = [];

        public IReadOnlyList<HipsSurvey> Surveys { get; } = [Dss, new HipsSurvey("PanSTARRS-DR1-color", "Pan-STARRS DR1 colour", 11)];

        public Task<HipsTile> GetTileAsync(HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken = default)
        {
            lock (Requests)
            {
                Requests.Add(key);
            }

            return Task.FromResult(serve is null ? new HipsTile(key.IsAllsky ? [0, 0] : [1], "ESA ESASky") : serve(key));
        }

        public string CreditFor(HipsSurvey survey) => "DSS: STScI/NASA · colour and HiPS by CDS";
    }

    /// <summary>"Decodes" a tile into a solid grey image of the right size: the Allsky mosaic (2-byte fake content) or a 512 px tile.</summary>
    private sealed class FakeDecoder : IImageDecoder
    {
        public RgbImage Decode(byte[] content)
        {
            var image = content.Length == 2 ? new RgbImage(27 * 64, 29 * 64) : new RgbImage(512, 512);
            Array.Fill(image.Pixels, (byte)120);
            return image;
        }
    }

    private sealed class FakeInfo(Func<CelestialObject, ObjectSummary?> summary) : IObjectInfoService
    {
        public int SummaryCalls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task<ObjectSummary?> GetSummaryAsync(CelestialObject target, CancellationToken cancellationToken = default)
        {
            SummaryCalls++;
            return FailWith is not null ? Task.FromException<ObjectSummary?>(FailWith) : Task.FromResult(summary(target));
        }

        public Task<byte[]> GetImageAsync(ObjectImage image, CancellationToken cancellationToken = default) => Task.FromResult(new byte[] { 1, 2, 3 });
    }

    private sealed class FakeResolver(ResolvedName? answer) : INameResolver
    {
        public List<string> Queries { get; } = [];
        public Exception? FailWith { get; set; }

        public Task<ResolvedName?> ResolveAsync(string name, CancellationToken cancellationToken = default)
        {
            Queries.Add(name);
            return FailWith is not null ? Task.FromException<ResolvedName?>(FailWith) : Task.FromResult(answer);
        }
    }

    private static readonly ObjectSummary OrionNebula = new("Orion Nebula", "The Orion Nebula is a diffuse nebula…",
        "Diffuse nebula in the constellation Orion", new Uri("https://en.wikipedia.org/wiki/Orion_Nebula"))
    {
        Image = new ObjectImage(new Uri("https://upload.wikimedia.org/wikipedia/commons/thumb/f/f3/M42.jpg/330px-M42.jpg"), "M42.jpg",
            new Uri("https://commons.wikimedia.org/wiki/File:M42.jpg"), "Public domain", null, "NASA, ESA")
    };

    private static async Task<ShellViewModel> CreateAsync(
        ISurveyTileSource? tiles = null, IObjectInfoService? info = null, INameResolver? resolver = null, ObserverSettings? settings = null,
        EquipmentInventory? equipment = null)
    {
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13],
            settings: new FakeSettingsService(settings ?? Aguascalientes),
            time: new ManualTimeProvider(WinterNightUtc),
            equipment: new FakeEquipmentRepository(equipment ?? EquipmentInventory.Demo),
            tiles: tiles,
            info: info,
            resolver: resolver,
            decoder: new FakeDecoder());
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        return shell;
    }

    private static EquipmentInventory WithActiveRig(string rigId) => EquipmentInventory.Demo with { ActiveRigId = rigId };

    // --- Framing preview -------------------------------------------------------------------------

    [Fact]
    public async Task Framing_LoadsOnlyWhenTheDetailIsShown()
    {
        var tiles = new FakeTiles();
        var shell = await CreateAsync(tiles);

        shell.Targets.SelectById("M42").Should().BeTrue();
        await shell.Detail.Framing.LoadTask;
        tiles.Requests.Should().BeEmpty("the detail is not on screen");

        shell.Detail.IsShown = true;
        await shell.Detail.Framing.LoadTask;

        tiles.Requests.Should().Equal([HipsTileKey.Allsky], "the 16 mm field (~80°) uses the Allsky image");
        shell.Detail.Framing.Image.Should().NotBeNull();
        shell.Detail.Framing.StatusText.Should().BeEmpty();
        shell.Detail.Framing.FieldText.Should().StartWith("T7 + Rokinon 16 mm (tripod) · ");
        shell.Detail.Framing.AttributionText.Should().Be("DSS: STScI/NASA · colour and HiPS by CDS · via ESA ESASky");
        shell.OnlineStatusTooltip.Should().Contain("Survey images (CDS, ESA ESASky): updated");
    }

    [Fact]
    public async Task Framing_WithTheTelescope_ShowsTheOutline_AndRotatesWithoutRefetching()
    {
        var tiles = new FakeTiles();
        var shell = await CreateAsync(tiles, equipment: WithActiveRig("t7-starsense-130"));
        shell.Detail.IsShown = true;
        shell.Targets.SelectById("M42");
        await shell.Detail.Framing.LoadTask;

        var framing = shell.Detail.Framing;
        tiles.Requests.Should().NotBeEmpty().And.NotContain(HipsTileKey.Allsky);
        tiles.Requests.Should().OnlyContain(k => k.Order >= 3 && k.Order <= 9);
        framing.Image!.Width.Should().Be(FramingViewModel.OutputWidth);
        framing.HasOutline.Should().BeTrue("M42 is 90′ × 60′ in a ~2° field");
        framing.OutlineWidth.Should().BeApproximately(FramingViewModel.OutputWidth * 1.5 / 1.965, 3, "90′ in the T7 + 650 mm field of 1.965°");
        framing.OutlineAngle.Should().Be(-90, "no position angle → north-south major axis, drawn vertically");

        var requests = tiles.Requests.Count;
        framing.RotationDegrees = 30;
        await framing.LoadTask;

        framing.OutlineAngle.Should().Be(-60);
        tiles.Requests.Should().HaveCountLessThanOrEqualTo(requests + 4, "decoded tiles are reused when turning the camera");
    }

    [Fact]
    public async Task Framing_ExplainsWhenThereIsNoPreview()
    {
        var tiles = new FakeTiles();
        var shell = await CreateAsync(tiles, equipment: EquipmentInventory.Demo with { ActiveRigId = null });
        shell.Detail.IsShown = true;
        shell.Targets.SelectById("M42");

        shell.Detail.Framing.StatusText.Should().Be(Strings.FramingNoRig);

        var disabled = await CreateAsync(tiles, settings: new ObserverSettings
        {
            IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, TimeZoneId = "Central Standard Time (Mexico)",
            CityId = null, SurveyImagesEnabled = false
        });
        disabled.Detail.IsShown = true;
        disabled.Targets.SelectById("M42");
        disabled.Detail.Framing.StatusText.Should().Be(Strings.FramingDisabled);
        tiles.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Framing_Offline_SaysImagesAreUnavailable_AndTheStatusShowsIt()
    {
        var tiles = new FakeTiles(_ => throw new OnlineServiceException("Survey images are unavailable (no connection)."));
        var shell = await CreateAsync(tiles);
        shell.Detail.IsShown = true;
        shell.Targets.SelectById("M42");
        await shell.Detail.Framing.LoadTask;

        shell.Detail.Framing.Image.Should().BeNull();
        shell.Detail.Framing.StatusText.Should().Be("Survey images unavailable: Survey images are unavailable (no connection).");
        shell.OnlineStatusText.Should().Be(Strings.OnlineStatusOffline);
        shell.OnlineStatusTooltip.Should().Contain("Survey images (CDS, ESA ESASky): unavailable");
    }

    [Fact]
    public void RealDssTile_PutsThePleiadesWhereTheSkyHasThem()
    {
        // The recorded order-5 tile with the Pleiades, decoded by WPF and sampled by the preview:
        // the brightest stars must land on bright pixels (checks the tile layout end to end).
        var jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "online", "hips-dsscolor-norder5-npix115.jpg"));
        var tile = new WpfImageDecoder().Decode(jpeg);
        tile.Width.Should().Be(512);

        var plan = FramingPreview.Plan(new FramingRequest(56.40, 24.22, 1.0, 0.8, OutputWidth: 400), Dss with { MaximumOrder = 5 });
        plan.Order.Should().Be(5);
        var image = plan.Compose(new Dictionary<HipsTileKey, RgbImage> { [new HipsTileKey(5, 115)] = tile }, out _);

        var stars = new[] { (56.2189, 24.1133), (56.4566, 24.3678), (56.5816, 23.9484), (56.3021, 24.4672) };   // Electra, Maia, Merope, Taygeta
        foreach (var (ra, dec) in stars)
        {
            var (x, y) = plan.ToPixel(ra, dec)!.Value;
            var (r, g, b) = image[(int)Math.Round(x), (int)Math.Round(y)];
            ((r + g + b) / 3).Should().BeGreaterThan(150, $"star at RA {ra}, Dec {dec}");
        }

        var sky = image[20, 20];
        ((sky.R + sky.G + sky.B) / 3).Should().BeLessThan(90, "the sky away from the stars is dark");
        FluentActions.Invoking(() => new WpfImageDecoder().Decode([1, 2, 3])).Should().Throw<NotSupportedException>();
    }

    // --- Wikipedia -------------------------------------------------------------------------------

    [Fact]
    public async Task About_ShowsTheArticleWithItsImageCredit()
    {
        var info = new FakeInfo(_ => OrionNebula);
        var shell = await CreateAsync(info: info);
        shell.Targets.SelectById("M42");
        info.SummaryCalls.Should().Be(0);

        shell.Detail.IsShown = true;
        await shell.Detail.About.LoadTask;

        var about = shell.Detail.About;
        about.HasSummary.Should().BeTrue();
        about.Summary!.Title.Should().Be("Orion Nebula");
        about.ImageContent.Should().Equal(1, 2, 3);
        about.ImageCreditText.Should().Be("Image: NASA, ESA · Public domain (Wikimedia Commons)");
        about.StatusText.Should().BeEmpty();
        about.OpenArticleCommand.CanExecute(null).Should().BeTrue();
        about.OpenLicenseCommand.CanExecute(null).Should().BeFalse("public domain has no license page");

        // The same object is not asked again.
        shell.Detail.IsShown = false;
        shell.Detail.IsShown = true;
        info.SummaryCalls.Should().Be(1);
    }

    [Fact]
    public async Task About_NoArticle_Disabled_AndOffline()
    {
        var none = await CreateAsync(info: new FakeInfo(_ => null));
        none.Detail.IsShown = true;
        none.Targets.SelectById("M13");
        await none.Detail.About.LoadTask;
        none.Detail.About.StatusText.Should().Be(Strings.InfoNoArticle);
        none.Detail.About.HasSummary.Should().BeFalse();

        var offlineInfo = new FakeInfo(_ => OrionNebula) { FailWith = new OnlineServiceException("No connection to Wikipedia.") };
        var offline = await CreateAsync(info: offlineInfo);
        offline.Detail.IsShown = true;
        offline.Targets.SelectById("M42");
        await offline.Detail.About.LoadTask;
        offline.Detail.About.StatusText.Should().Be("Description unavailable: No connection to Wikipedia.");
        offline.OnlineStatusTooltip.Should().Contain("Descriptions (Wikipedia): unavailable (No connection to Wikipedia.)");

        var info = new FakeInfo(_ => OrionNebula);
        var disabled = await CreateAsync(info: info, settings: new ObserverSettings
        {
            IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, TimeZoneId = "Central Standard Time (Mexico)",
            CityId = null, WikipediaEnabled = false
        });
        disabled.Detail.IsShown = true;
        disabled.Targets.SelectById("M42");
        disabled.Detail.About.StatusText.Should().Be(Strings.InfoDisabled);
        info.SummaryCalls.Should().Be(0);
    }

    // --- Sesame ----------------------------------------------------------------------------------

    [Fact]
    public async Task SkySearch_FallsBackToSesame_ForNamesOutsideTheCatalog()
    {
        var horsehead = new ResolvedName("Barnard 33", "NAME Horsehead Nebula", "DNe", new EquatorialCoordinates(5.683, -2.458), "Sesame (CDS) · Simbad");
        var resolver = new FakeResolver(horsehead);
        var shell = await CreateAsync(resolver: resolver);

        shell.Sky.Search.SearchText = "M31";
        await shell.Sky.Search.SearchCommand.ExecuteAsync(null);
        resolver.Queries.Should().BeEmpty("catalog objects never go online");

        shell.Sky.Search.SearchText = " Barnard 33 ";
        await shell.Sky.Search.SearchCommand.ExecuteAsync(null);

        resolver.Queries.Should().Equal("Barnard 33");
        shell.Sky.Search.ResolvedTarget.Should().Be(horsehead);
        shell.Sky.Search.SearchMessage.Should().StartWith("NAME Horsehead Nebula (DNe), not in the catalog: resolved by Sesame (CDS) · Simbad at RA 5.6830 h");
        shell.OnlineStatusTooltip.Should().Contain("Name search (Sesame, CDS): updated");
    }

    [Fact]
    public async Task SkySearch_UnknownEverywhere_Disabled_AndOffline()
    {
        var unknown = await CreateAsync(resolver: new FakeResolver(null));
        unknown.Sky.Search.SearchText = "Xyzzy 1";
        await unknown.Sky.Search.SearchCommand.ExecuteAsync(null);
        unknown.Sky.Search.SearchMessage.Should().Be("Nothing found for \"Xyzzy 1\", in the catalog or online (Sesame).");

        var offlineResolver = new FakeResolver(null) { FailWith = new OnlineServiceException("No connection to Sesame (CDS).") };
        var offline = await CreateAsync(resolver: offlineResolver);
        offline.Sky.Search.SearchText = "Xyzzy 1";
        await offline.Sky.Search.SearchCommand.ExecuteAsync(null);
        offline.Sky.Search.SearchMessage.Should().EndWith("the online search is unavailable: No connection to Sesame (CDS).");

        var resolver = new FakeResolver(null);
        var disabled = await CreateAsync(resolver: resolver, settings: new ObserverSettings
        {
            IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, TimeZoneId = "Central Standard Time (Mexico)",
            CityId = null, NameResolverEnabled = false
        });
        disabled.Sky.Search.SearchText = "Xyzzy 1";
        await disabled.Sky.Search.SearchCommand.ExecuteAsync(null);
        disabled.Sky.Search.SearchMessage.Should().Be("Nothing found for \"Xyzzy 1\".");
        resolver.Queries.Should().BeEmpty();
    }

    // --- Settings and links ----------------------------------------------------------------------

    [Fact]
    public void Settings_KeepTheNewToggles()
    {
        var store = new FakeSettingsService(new ObserverSettings());
        var settings = new SettingsViewModel(store, new FakeCityRepository(), new NullLogger())
        {
            SurveyImagesEnabled = false,
            WikipediaEnabled = false,
            NameResolverEnabled = false
        };

        settings.SaveCommand.Execute(null);

        store.Load().Should().BeEquivalentTo(new { SurveyImagesEnabled = false, WikipediaEnabled = false, NameResolverEnabled = false },
            o => o.ExcludingMissingMembers());
    }

    [Theory]
    [InlineData("https://en.wikipedia.org/wiki/Orion_Nebula", true)]
    [InlineData("https://commons.wikimedia.org/wiki/File:M42.jpg", true)]
    [InlineData("https://creativecommons.org/licenses/by-sa/4.0", true)]
    [InlineData("https://skies.esac.esa.int/DSSColor/", true)]
    [InlineData("http://en.wikipedia.org/wiki/Orion_Nebula", false)]
    [InlineData("https://wikipedia.org.evil.example/", false)]
    public void SafeLinks_AllowTheNewSites(string url, bool allowed) => SafeLinks.IsAllowed(new Uri(url)).Should().Be(allowed);
}
