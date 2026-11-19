using System.Diagnostics;
using AstroPlanner.Data.Storage;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.UI.Tests;

/// <summary>M1: catalog filters, cross-id search, night ephemerides, horizon, locations, v1 import.</summary>
public sealed class CatalogFeatureTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private static readonly CelestialObject NorthAmerica = new(
        "NGC 7000", "North America Nebula", "Cygnus", CelestialObjectType.HiiRegion, 4,
        new EquatorialCoordinates(20.98, 44.33))
    {
        Designations = ["C20", "LBN 373"],
        MajorAxisArcmin = 120,
        MinorAxisArcmin = 100
    };

    private static readonly CelestialObject Horsehead = new(
        "B 33", "Horsehead Nebula", "Orion", CelestialObjectType.DarkNebula, double.NaN,
        new EquatorialCoordinates(5.683, -2.458))
    {
        Designations = ["IC 434"],
        MajorAxisArcmin = 6,
        MinorAxisArcmin = 4
    };

    private static readonly CelestialObject TinyGalaxy = new(
        "IC 2000", "", "Lepus", CelestialObjectType.Galaxy, 13.5, new EquatorialCoordinates(5.5, -20));

    private static readonly IReadOnlyList<CelestialObject> Catalog =
        [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, NorthAmerica, Horsehead, TinyGalaxy, .. SolarSystemCatalog.Planets];

    private static async Task<ShellViewModel> CreateLoadedAsync(
        IReadOnlyList<CelestialObject>? catalog = null,
        FakeSettingsService? settings = null,
        FakeLocationRepository? locations = null,
        FakeLogService? log = null)
    {
        var logbook = log is null ? null : new LogbookViewModel(log, new NullLogger());
        var shell = TestShell.Create(
            catalog: catalog ?? Catalog,
            settings: settings ?? new FakeSettingsService(new ObserverSettings { MinimumAltitude = 20, MaximumMagnitude = 20 }),
            time: new ManualTimeProvider(WinterNightUtc),
            locations: locations,
            logbook: logbook);
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        return shell;
    }

    private static List<string> Ids(ShellViewModel shell) => [.. shell.Targets.Objects.Select(o => o.Id).Order()];

    // ─── Filters and search ─────────────────────────────────────────────

    [Theory]
    [InlineData(TargetsViewModel.CatalogMessier, new[] { "M13", "M31", "M42" })]
    [InlineData(TargetsViewModel.CatalogCaldwell, new[] { "NGC 7000" })]
    [InlineData(TargetsViewModel.CatalogNgc, new[] { "M13", "M31", "M42", "NGC 7000" })]
    [InlineData(TargetsViewModel.CatalogIc, new[] { "B 33", "IC 2000" })]
    public async Task CatalogFilter_KeepsTheObjectsOfThatCatalog(string catalog, string[] expected)
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.CatalogFilter = catalog;

        Ids(shell).Should().Equal(expected);
    }

    [Fact]
    public async Task CatalogFilter_SolarSystem_ShowsThePlanets()
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.CatalogFilter = TargetsViewModel.CatalogSolarSystem;

        shell.Targets.Objects.Should().HaveCount(7).And.OnlyContain(o => o.IsPlanet);
    }

    [Fact]
    public async Task MinimumSize_HidesSmallObjects_ButKeepsThoseOfUnknownSize()
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.MinimumSizeArcmin = 30;

        Ids(shell).Should().Contain(["M31", "M42", "NGC 7000", "IC 2000"]).And.NotContain(["M13", "B 33"]);
        shell.Targets.Objects.Should().Contain(o => o.IsPlanet);
    }

    [Fact]
    public async Task MinimumSize_OutOfRange_ShowsAWarning()
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.MinimumSizeArcmin = -1;

        shell.Targets.ValidationMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task MaximumMagnitude_KeepsNebulaeOfUnknownMagnitude_ButNotOtherObjects()
    {
        var star = new CelestialObject("IC 9999", "", "Orion", CelestialObjectType.Star, double.NaN, new EquatorialCoordinates(5.6, -1));
        var shell = await CreateLoadedAsync([.. Catalog, star]);

        shell.Targets.MaximumMagnitude = 5;

        Ids(shell).Should().Contain("B 33").And.NotContain("IC 2000").And.NotContain("IC 9999");
    }

    [Theory]
    [InlineData("NGC 1976", "M42")]
    [InlineData("ic434", "B 33")]
    [InlineData("C20", "NGC 7000")]
    [InlineData("north america", "NGC 7000")]
    [InlineData("horsehead", "B 33")]
    public async Task Search_FindsObjectsByCrossIdAndName(string query, string id)
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.SearchText = query;

        shell.Targets.Objects.Select(o => o.Id).Should().Equal(id);
    }

    [Fact]
    public async Task ResetFilters_ClearsTheNewFilters()
    {
        var shell = await CreateLoadedAsync();
        shell.Targets.CatalogFilter = TargetsViewModel.CatalogMessier;
        shell.Targets.MinimumSizeArcmin = 30;
        shell.Targets.OnlyObservableTonight = true;

        shell.Targets.ResetFiltersCommand.Execute(null);

        shell.Targets.CatalogFilter.Should().Be(TargetsViewModel.CatalogAll);
        shell.Targets.MinimumSizeArcmin.Should().Be(0);
        shell.Targets.OnlyObservableTonight.Should().BeFalse();
        shell.Targets.Objects.Should().HaveCount(Catalog.Count);
    }

    [Fact]
    public async Task Rows_ShowCrossIdsSizeAndConstellationName()
    {
        var shell = await CreateLoadedAsync();
        var m42 = shell.Targets.AllObjects.Single(o => o.Id == "M42");

        m42.CrossIdentifiers.Should().Be("NGC 1976");
        m42.SizeText.Should().Contain("90").And.Contain("60");
        shell.Targets.AllObjects.Single(o => o.Id == "IC 2000").CommonName.Should().Be("IC 2000", "objects without a name show their id");
    }

    [Fact]
    public async Task Planets_GetTheirRealConstellation()
    {
        var shell = await CreateLoadedAsync();

        // Jupiter was in Gemini in January 2026 (opposition on 2026-01-10).
        shell.Targets.AllObjects.Single(o => o.Id == "Jupiter").Constellation.Should().Be("Gemini");
        shell.Targets.ConstellationFilters.Should().HaveCount(89, "all 88 IAU constellations plus 'All'");
    }

    // ─── Night ephemerides ──────────────────────────────────────────────

    [Fact]
    public async Task NightEphemerides_AreCalculatedForEveryObject()
    {
        var shell = await CreateLoadedAsync();
        var m42 = shell.Targets.AllObjects.Single(o => o.Id == "M42");

        shell.Targets.AllObjects.Should().OnlyContain(o => o.Night != null);
        m42.IsObservableTonight.Should().BeTrue();
        m42.MaximumAltitudeTonight.Should().BeApproximately(90 - 19.43 - 5.39, 0.5);
        m42.BestWindowText.Should().MatchRegex(@"^\d\d:\d\d–\d\d:\d\d$");
        shell.Targets.IsCalculatingNight.Should().BeFalse();
    }

    [Fact]
    public async Task OnlyObservableTonight_HidesObjectsThatNeverClimbHighEnoughInDarkness()
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.OnlyObservableTonight = true;

        // NGC 7000 is low in the evening twilight in January; M42 is up all night.
        Ids(shell).Should().Contain("M42").And.NotContain("NGC 7000");
    }

    [Fact]
    public async Task NightEphemerides_AreNotRecalculatedForAnotherTimeOfTheSameNight()
    {
        var shell = await CreateLoadedAsync();
        var task = shell.Targets.NightEphemeridesTask;

        shell.ObservationDateTime = shell.ObservationDateTime.AddHours(2);
        shell.Targets.NightEphemeridesTask.Should().BeSameAs(task);

        shell.ObservationDateTime = shell.ObservationDateTime.AddDays(1);
        shell.Targets.NightEphemeridesTask.Should().NotBeSameAs(task);
        await shell.Targets.NightEphemeridesTask;
    }

    // ─── Horizon and locations ──────────────────────────────────────────

    [Fact]
    public async Task HorizonMask_HidesObjectsBehindObstacles()
    {
        var shell = await CreateLoadedAsync();
        var m42 = shell.Targets.AllObjects.Single(o => o.Id == "M42");
        m42.IsVisible.Should().BeTrue();

        // A wall 80° high all around.
        shell.Observer.HorizonMask = new HorizonMask([new HorizonPoint(0, 80)]);

        m42.IsVisible.Should().BeFalse();
        await shell.Targets.NightEphemeridesTask;
        m42.IsObservableTonight.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCurrentSite_CreatesAnActiveLocation()
    {
        var settings = new FakeSettingsService(new ObserverSettings { MinimumAltitude = 20 });
        var locations = new FakeLocationRepository();
        var shell = await CreateLoadedAsync(settings: settings, locations: locations);

        shell.Locations.NewLocationName = "Rooftop";
        shell.Locations.SaveCurrentCommand.Execute(null);

        var saved = locations.Locations.Should().ContainSingle().Subject;
        saved.Name.Should().Be("Rooftop");
        saved.IanaTimeZoneId.Should().Be("America/Mexico_City");
        settings.Load().ActiveLocationId.Should().Be(saved.Id);
        shell.Locations.Locations.Should().ContainSingle(l => l.IsActive);
        shell.ActiveLocationName.Should().Be("Rooftop");
    }

    [Fact]
    public async Task UseLocation_MovesTheObserverAndAppliesItsHorizon()
    {
        var locations = new FakeLocationRepository();
        var mask = new HorizonMask([new HorizonPoint(90, 30), new HorizonPoint(270, 5)]);
        locations.Save(new Location("dark", "Dark site", 22.1, -102.6, 2100, "America/Mexico_City", "Central Standard Time (Mexico)", mask));
        var shell = await CreateLoadedAsync(locations: locations);

        shell.Locations.UseCommand.Execute(shell.Locations.Locations.Single());

        shell.Observer.Latitude.Should().Be(22.1);
        shell.Observer.Elevation.Should().Be(2100);
        shell.Observer.HorizonMask.Should().Be(mask);
        shell.ActiveLocationName.Should().Be("Dark site");
    }

    [Fact]
    public async Task EditingTheCoordinates_LeavesTheSavedLocation()
    {
        var settings = new FakeSettingsService(new ObserverSettings());
        var locations = new FakeLocationRepository();
        locations.Save(new Location("home", "Home", 21.88, -102.29, 1888, "America/Mexico_City", "Central Standard Time (Mexico)"));
        var shell = await CreateLoadedAsync(settings: settings, locations: locations);
        shell.Locations.UseCommand.Execute(shell.Locations.Locations.Single());

        shell.Settings.Latitude = 22.5;
        shell.Settings.SaveCommand.Execute(null);

        settings.Load().ActiveLocationId.Should().BeNull();
        shell.Locations.ActiveLocation.Should().BeNull();
        shell.Observer.HorizonMask.Should().Be(HorizonMask.Flat);
    }

    [Fact]
    public async Task DeletingTheActiveLocation_KeepsTheSiteAsAnUnsavedOne()
    {
        var settings = new FakeSettingsService(new ObserverSettings());
        var locations = new FakeLocationRepository();
        locations.Save(new Location("home", "Home", 21.88, -102.29, 1888, "America/Mexico_City", "Central Standard Time (Mexico)"));
        var shell = await CreateLoadedAsync(settings: settings, locations: locations);
        shell.Locations.UseCommand.Execute(shell.Locations.Locations.Single());

        shell.Locations.DeleteCommand.Execute(shell.Locations.Locations.Single());

        locations.Locations.Should().BeEmpty();
        settings.Load().ActiveLocationId.Should().BeNull();
        shell.Observer.Latitude.Should().Be(21.88);
    }

    // ─── Logbook ────────────────────────────────────────────────────────

    [Fact]
    public async Task Logbook_CountsCaldwellObjectsByTheirCatalogId()
    {
        var log = new FakeLogService
        {
            Entries = [new ObservationLogEntry(Guid.NewGuid(), "NGC 7000", "North America Nebula", new DateTime(2025, 8, 1, 22, 0, 0), "", "", 3, 3, 3, "")]
        };

        var shell = await CreateLoadedAsync(log: log);

        shell.Logbook.ObservedCaldwellCount.Should().Be(1);
        shell.Logbook.CaldwellProgressText.Should().Be("1 / 109");
    }

    // ─── Detail ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Detail_ShowsCrossIdsSizeSurfaceBrightnessAndTonightsWindow()
    {
        var shell = await CreateLoadedAsync();

        shell.Targets.SelectedObject = shell.Targets.AllObjects.Single(o => o.Id == "M31");
        var observation = shell.Detail.Observation!;

        observation.DesignationsText.Should().Contain("NGC 224");
        observation.SizeText.Should().Contain("177.8");
        observation.SurfaceBrightnessText.Should().Contain("23.6");
        observation.Constellation.Should().Be("Andromeda");
        observation.BestWindowText.Should().NotBeNullOrEmpty();
    }

    // ─── Performance (spec §14, M1 DoD) ─────────────────────────────────

    [Fact]
    [Trait("Category", "Performance")]
    public async Task FifteenThousandObjects_FilterInLessThan200Milliseconds()
    {
        var random = new Random(7);
        var catalog = Enumerable.Range(0, 15_000)
            .Select(i => new CelestialObject($"NGC {i}", i % 3 == 0 ? $"Object {i}" : "", "Orion",
                (CelestialObjectType)(i % 7), random.NextDouble() * 16, new EquatorialCoordinates(random.NextDouble() * 24, random.NextDouble() * 180 - 90))
            {
                Designations = [$"PGC {i + 100_000}"],
                MajorAxisArcmin = random.NextDouble() * 60
            })
            .ToList();
        var shell = await CreateLoadedAsync(catalog);
        var targets = shell.Targets;

        var times = new List<TimeSpan>();
        foreach (var change in new Action[]
        {
            () => targets.SearchText = "object 12",
            () => targets.SearchText = "pgc 101234",
            () => targets.SearchText = "",
            () => targets.MaximumMagnitude = 10,
            () => targets.CatalogFilter = TargetsViewModel.CatalogNgc,
            () => targets.MinimumSizeArcmin = 20,
            () => targets.OnlyVisible = true,
            () => targets.OnlyObservableTonight = true
        })
        {
            var stopwatch = Stopwatch.StartNew();
            change();
            times.Add(stopwatch.Elapsed);
        }

        targets.Objects.Should().NotBeEmpty();
        times.Max().Should().BeLessThan(TimeSpan.FromMilliseconds(200));

        // The 2D sky chart draws only the brightest objects (the full sky arrives with M2).
        targets.ResetFiltersCommand.Execute(null);
        var points = shell.Sky.GetSkyMapPoints();
        points.Should().HaveCount(SkyViewModel.MaximumPoints);
        points.Count(p => p.HasLabel).Should().Be(SkyViewModel.MaximumLabels);
    }
}

public sealed class V1ImportServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string V1Folder => Path.Combine(AppContext.BaseDirectory, "fixtures", "v1", "current");

    private V1ImportService Create(string v1Folder, out AppDatabase database)
    {
        database = new AppDatabase(Path.Combine(_folder, "app.db"));
        return new V1ImportService(
            database,
            new FakeCelestialRepository([TestCatalog.M42, TestCatalog.M31, .. SolarSystemCatalog.Planets]),
            new SqliteSettingsService(database), new SqlitePlanService(database), new SqliteLogbookService(database),
            new SqliteLocationRepository(database), v1Folder);
    }

    [Fact]
    public async Task Import_AppliesTheDataAndIsOfferedOnlyOnce()
    {
        var service = Create(V1Folder, out var database);
        service.ShouldOffer.Should().BeTrue();

        var result = await service.ImportAsync();

        result.PlanItemsAdded.Should().Be(3);
        result.LogEntriesAdded.Should().Be(3);
        new SqliteSettingsService(database).Load().MinimumAltitude.Should().Be(25);
        service.ShouldOffer.Should().BeFalse();
        V1ImportService.Describe(result).Should().Contain("3").And.Contain("X99");
    }

    [Fact]
    public void Declining_IsRemembered()
    {
        var service = Create(V1Folder, out _);

        service.MarkOffered();

        service.ShouldOffer.Should().BeFalse();
    }

    [Fact]
    public void WithoutV1Data_NothingIsOffered()
    {
        Create(Path.Combine(_folder, "no-v1-here"), out _).ShouldOffer.Should().BeFalse();
    }
}
