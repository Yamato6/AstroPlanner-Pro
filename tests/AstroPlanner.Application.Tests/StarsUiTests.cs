using AstroPlanner.SkyBridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.Application.Tests;

/// <summary>M6a: stars and double stars as catalog rows, in Targets, Detail and the sky selection.</summary>
public sealed class StarsUiTests
{
    private static readonly DateTimeOffset SummerNightUtc = new(2026, 7, 15, 4, 0, 0, TimeSpan.Zero);

    private static readonly ObserverSettings Aguascalientes = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = 21.88,
        LongitudeDegrees = -102.29,
        ElevationMeters = 1888,
        TimeZoneId = "Central Standard Time (Mexico)",
        MinimumAltitude = 20,
        MaximumMagnitude = 12,
        WeatherForecastEnabled = false,
        AstroForecastEnabled = false,
        SmallBodiesEnabled = false,
        SatellitesEnabled = false
    };

    private static readonly CelestialObject Albireo =
        new("HIP 95947", "Albireo", "Cygnus", CelestialObjectType.DoubleStar, 3.05, new EquatorialCoordinates(19.5120, 27.9597))
        {
            Star = new StarInfo(95648, 95947, 183912, "K3II+...", 1.074, 133.2, null, null, null)
            {
                Pairs = [new DoubleStarPair("19307+2758", "STFA 43", "AB", 2025, 54, 34.3, 3.19, 4.68)]
            },
            Designations = ["HD 183912", "Bet1 Cyg", "6 Cyg", "WDS 19307+2758", "STFA 43"],
            AlternateNames = ["β¹ Cyg", "Beta1 Cygni", "β Cyg", "Beta Cygni", "6 Cygni"]
        };

    private static readonly CelestialObject Vega =
        new("HIP 91262", "Vega", "Lyra", CelestialObjectType.Star, 0.03, new EquatorialCoordinates(18.6156, 38.7837))
        {
            Star = new StarInfo(90979, 91262, 172167, "A0Vvar", -0.001, 7.6787, "Alp", 0.07, -0.02),
            Designations = ["HD 172167", "Alp Lyr", "3 Lyr"],
            AlternateNames = ["α Lyr", "Alpha Lyrae", "3 Lyrae"]
        };

    private static async Task<ShellViewModel> CreateAsync()
    {
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, Albireo, Vega],
            settings: new FakeSettingsService(Aguascalientes),
            time: new ManualTimeProvider(SummerNightUtc),
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo));
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return shell;
    }

    [Fact]
    public async Task TheStarsFilter_ListsOnlyCatalogStars_AndSearchFindsThemByEveryName()
    {
        var shell = await CreateAsync();
        var targets = shell.Targets;

        targets.CatalogFilter = TargetsViewModel.CatalogStars;
        targets.Objects.Select(r => r.Id).Should().BeEquivalentTo(["HIP 95947", "HIP 91262"]);

        targets.CatalogFilter = TargetsViewModel.CatalogAll;
        foreach (var query in new[] { "Albireo", "beta cygni", "β Cyg", "HIP 95947", "HD 183912", "STFA 43" })
        {
            targets.SearchText = query;
            targets.Objects.Select(r => r.Id).Should().Contain("HIP 95947", query);
        }
    }

    [Fact]
    public async Task Detail_ShowsTheStarAndItsPairs_AndWhetherTheRigSplitsIt()
    {
        var shell = await CreateAsync();

        shell.Targets.SelectById("HIP 95947").Should().BeTrue();
        var observation = shell.Detail.Observation!;

        observation.StarText.Should().Be("Spectral type K3II+... · B−V 1.07 · 434.4 light-years");
        observation.DoubleStarText.Should().Be("STFA 43 AB · 34.3″ at PA 54° · mags 3.2 + 4.7 (2025)");
        var row = shell.Targets.AllObjects.Single(r => r.Id == "HIP 95947");
        row.Feasibility!.Reasons.Should().Contain(r => r.Factor == Core.Imaging.Feasibility.FeasibilityFactor.Resolution,
            "the demo rig (16 mm) cannot split 34″");

        shell.Targets.SelectById("HIP 91262");
        shell.Detail.Observation!.StarText.Should().EndWith("variable (Hipparcos -0.02–0.07)");
        shell.Detail.Observation.DoubleStarText.Should().BeNull();
    }

    [Fact]
    public async Task Sky_SelectingAStar_SelectsItsRow_AndTheRowSelectsTheStar()
    {
        var shell = await CreateAsync();
        var channel = new FakeSkyChannel();
        shell.Sky.Attach(channel);
        channel.Ready();

        var sentBefore = channel.Count("selectObject");
        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"star","id":"95648"}}""");
        shell.Targets.SelectedObject!.Id.Should().Be("HIP 95947");
        shell.Sky.SkyOnlySelectionText.Should().BeNull();
        channel.Count("selectObject").Should().Be(sentBefore, "no echo of the renderer's own selection");

        shell.Targets.SelectById("HIP 91262");
        var sent = channel.Last("selectObject");
        (sent.GetProperty("kind").GetString(), sent.GetProperty("id").GetString()).Should().Be((SkyObjectKinds.Star, "90979"));

        // A star fainter than the catalog rows stays a sky-only selection.
        channel.Receive("""{"v":1,"type":"hover","payload":{"kind":"star","id":"70000","label":"HYG 70000 (8.4)"}}""");
        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"star","id":"70000"}}""");
        shell.Sky.SkyOnlySelectionText.Should().Be("HYG 70000 (8.4)");
        Strings.CatalogStars.Should().Be("Stars");
    }
}
