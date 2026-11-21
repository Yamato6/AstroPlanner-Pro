using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Sky;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>M5a: comets and asteroids in Targets, Tonight and the sky; the Milky Way core.</summary>
public sealed class SmallBodiesUiTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Aguascalientes.
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

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

    private static double Jd(int year, int month, double day) =>
        AstronomyMath.ToJulianDate(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)) + day - 1;

    /// <summary>A bright made-up comet at perihelion near opposition in January (up all night).</summary>
    private static readonly SmallBodyOrbit BrightComet = new(SmallBodyKind.Comet,
        new OrbitalElements(1.3, 0.99, Jd(2026, 1, 16), 115, 0, 5, Jd(2026, 1, 16)), 5.0, 4.0, "C/2026 Z9", "Testcomet");

    /// <summary>2P/Encke from the MPC file of 2026-11-20: far from perihelion in January 2026, fainter than mag 15.</summary>
    private static readonly SmallBodyOrbit Encke = new(SmallBodyKind.Comet,
        new OrbitalElements(0.338591, 0.847328, Jd(2027, 2, 10.2276), 187.2881, 334.0197, 11.3479, Jd(2026, 11, 20)), 14.6, 4.0, "2P", "Encke");

    private static readonly SmallBodyOrbit Vesta = new(SmallBodyKind.Asteroid,
        OrbitalElements.FromMeanAnomaly(2.3615413, 0.0901676, 7.14406, 103.70232, 151.53712, 26.80968, Jd(2025, 11, 21)), 3.25, 0.15, "(4)", "Vesta");

    private sealed class FakeMpc : ISmallBodyService
    {
        public int Calls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task<SmallBodyCatalog> GetCometsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return FailWith is not null ? Task.FromException<SmallBodyCatalog>(FailWith)
                : Task.FromResult(new SmallBodyCatalog([BrightComet, Encke], WinterNightUtc.UtcDateTime));
        }

        public Task<SmallBodyCatalog> GetBrightAsteroidsAsync(CancellationToken cancellationToken = default) =>
            FailWith is not null ? Task.FromException<SmallBodyCatalog>(FailWith)
                : Task.FromResult(new SmallBodyCatalog([Vesta], WinterNightUtc.UtcDateTime));
    }

    private static async Task<ShellViewModel> CreateAsync(ISmallBodyService? mpc, ObserverSettings? settings = null)
    {
        var shell = TestShell.Create(
            catalog: [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13],
            settings: new FakeSettingsService(settings ?? Aguascalientes),
            time: new ManualTimeProvider(WinterNightUtc),
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo),
            smallBodies: mpc);
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.SmallBodies!.RefreshAsync();
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return shell;
    }

    [Fact]
    public async Task Comets_BrighterThan15_AndTheBrightAsteroids_JoinTheCatalog()
    {
        var shell = await CreateAsync(new FakeMpc());

        var rows = shell.Targets.AllObjects.Where(r => r.Target.IsSmallBody).ToList();
        rows.Select(r => r.Id).Should().BeEquivalentTo(["C/2026 Z9", "(4)"], "2P is fainter than mag 15 in January 2026");
        var comet = rows.Single(r => r.Id == "C/2026 Z9");
        comet.Target.Type.Should().Be(CelestialObjectType.Comet);
        comet.ApparentMagnitude.Should().BeInRange(2, 6);
        comet.Constellation.Should().NotBe(CelestialObject.SolarSystemConstellation);
        comet.Night!.IsObservableInDarkness.Should().BeTrue();
        comet.Feasibility.Should().NotBeNull("feasibility runs for comets too");

        shell.Targets.CatalogFilter = TargetsViewModel.CatalogSolarSystem;
        shell.Targets.Objects.Select(r => r.Id).Should().BeEquivalentTo(["C/2026 Z9", "(4)"], "the test catalog has no planets");
    }

    [Fact]
    public async Task Tonight_AnnouncesTheCometOfTheNight_AndTheNextMilkyWayNight()
    {
        var shell = await CreateAsync(new FakeMpc());
        await shell.Tonight.MilkyWayTask;

        shell.Tonight.Alerts.Should().Contain(a => a.StartsWith("☄ Comet of the night: C/2026 Z9 (Testcomet), mag", StringComparison.Ordinal));
        shell.Tonight.Alerts.Should().Contain(a => a.StartsWith("🌌 No Milky Way core", StringComparison.Ordinal),
            "in January the galactic centre is next to the Sun");
    }

    [Fact]
    public async Task Detail_ShowsDistancesAndTheElementsSource()
    {
        var shell = await CreateAsync(new FakeMpc());

        shell.Targets.SelectById("(4)").Should().BeTrue();

        shell.Detail.Observation!.SmallBodyText.Should().StartWith("Sun 2.").And.Contain("elements MPC, epoch 2025-11-21");
        DisplayNames.SmallBodyName(shell.Detail.SelectedObject!.Target).Should().Be("(4) Vesta");
    }

    [Fact]
    public async Task Disabled_NothingIsFetched_AndOffline_TheStatusSaysSo()
    {
        var mpc = new FakeMpc();
        var disabled = await CreateAsync(mpc, new ObserverSettings
        {
            IsManualLocation = true, LatitudeDegrees = 21.88, LongitudeDegrees = -102.29, TimeZoneId = "Central Standard Time (Mexico)",
            CityId = null, SmallBodiesEnabled = false
        });
        mpc.Calls.Should().Be(0);
        disabled.Targets.AllObjects.Should().NotContain(r => r.Target.IsSmallBody);

        var offline = await CreateAsync(new FakeMpc { FailWith = new OnlineServiceException("No connection to the Minor Planet Center.") });
        offline.Targets.AllObjects.Should().NotContain(r => r.Target.IsSmallBody);
        offline.OnlineStatusTooltip.Should().Contain("Comets and asteroids (MPC): unavailable (No connection to the Minor Planet Center.)");
    }

    [Fact]
    public async Task Sky_GetsTheBrightSmallBodies_AsCometsAndAsteroids()
    {
        var shell = await CreateAsync(new FakeMpc());
        var bodies = shell.Targets.AllObjects.Where(r => r.Target.IsSmallBody).Select(r => r.Target).ToList();

        var payload = SkyBridgeMapper.MovingBodies(new ObserverLocation(21.88, -102.29, 1888), WinterNightUtc.UtcDateTime,
            TimeSpan.FromHours(1), TimeSpan.FromMinutes(30), smallBodies: bodies);

        payload.Bodies.Select(b => (b.Id, b.Kind)).Should().Contain([("C/2026 Z9", "comet"), ("(4)", "asteroid")]);
        payload.Bodies.Should().OnlyContain(b => double.IsFinite(b.Magnitude));
        payload.Bodies.Should().HaveCount(9 + 2);
    }

    [Fact]
    public async Task MilkyWayPlanner_ListsTheBestNightsOfTheYear()
    {
        var shell = await CreateAsync(null);
        var planner = shell.Tonight.CreateMilkyWayPlanner()!;

        await planner.LoadAsync();

        planner.Nights.Should().HaveCount(365);
        planner.BestNights.Should().HaveCount(MilkyWayPlannerViewModel.BestNightCount);
        planner.BestNights.Should().BeInAscendingOrder(r => r.Night.Date);
        planner.BestNights.Should().OnlyContain(r => r.Night.IsGood && r.Night.MoonIlluminatedFraction < 0.6);
        planner.StatusText.Should().BeEmpty();
        planner.SubtitleText.Should().Contain("above 10°");
    }
}
