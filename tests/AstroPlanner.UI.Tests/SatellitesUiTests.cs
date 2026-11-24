using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Application.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

/// <summary>M5b: satellites in Tonight, the sky and the session plan, from recorded CelesTrak elements.</summary>
public sealed class SatellitesUiTests
{
    // 2026-11-21 01:00 UTC = 2026-11-20 22:00 in Buenos Aires: the night with the ISS passes of the Horizons fixture.
    private static readonly DateTimeOffset EveningUtc = new(2026, 11, 21, 1, 0, 0, TimeSpan.Zero);

    private static readonly ObserverSettings BuenosAires = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = -34.60,
        LongitudeDegrees = -58.38,
        ElevationMeters = 25,
        TimeZoneId = "Argentina Standard Time",
        MinimumAltitude = 10,
        MaximumMagnitude = 20,
        WeatherForecastEnabled = false,
        AstroForecastEnabled = false,
        SmallBodiesEnabled = false
    };

    /// <summary>CelesTrak GP elements of the ISS recorded 2026-11-20 (tests/fixtures/online/celestrak-stations.json).</summary>
    private static readonly SatelliteElements Iss = new(25544, "ISS (ZARYA)", "1998-067A",
        new DateTime(2026, 11, 20, 21, 9, 47, DateTimeKind.Utc).AddTicks(7515520),
        15.49518511, 0.00073891, 51.6319, 244.6605, 48.0812, 312.0806, 0.00018413556);

    private sealed class FakeCelesTrak : ISatelliteService
    {
        public int Calls { get; private set; }
        public Exception? FailWith { get; set; }

        public Task<SatelliteCatalog> GetSatellitesAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return FailWith is not null ? Task.FromException<SatelliteCatalog>(FailWith)
                : Task.FromResult(new SatelliteCatalog([Iss, Iss with { CatalogNumber = 36086, Name = "POISK" }], EveningUtc.UtcDateTime));
        }
    }

    private static async Task<ShellViewModel> CreateAsync(ISatelliteService? celestrak, ObserverSettings? settings = null,
        FakePlanService? plan = null, IReadOnlyList<CelestialObject>? catalog = null)
    {
        var shell = TestShell.Create(
            catalog: catalog ?? [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13],
            plan: plan,
            settings: new FakeSettingsService(settings ?? BuenosAires),
            time: new ManualTimeProvider(EveningUtc),
            equipment: new FakeEquipmentRepository(EquipmentInventory.Demo),
            satellites: celestrak);
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Satellites!.RefreshAsync();
        await shell.Satellites.PassesTask;
        await shell.Plan.CrossingsTask;
        return shell;
    }

    [Fact]
    public async Task Tonight_ListsTheVisibleIssPasses_AndAlertsTheBest()
    {
        var shell = await CreateAsync(new FakeCelesTrak());

        shell.Satellites!.Satellites.Should().ContainSingle("POISK is docked to the ISS");
        shell.Satellites.Passes.Should().NotBeEmpty().And.OnlyContain(p => p.IsVisible).And.BeInAscendingOrder(p => p.VisibleStartUtc);
        shell.Tonight.SatellitePasses.Should().HaveCount(shell.Satellites.Passes.Count).And.OnlyContain(line => line.StartsWith("ISS (ZARYA) · "));
        shell.Tonight.SatellitePassesHint.Should().BeNull();
        shell.Tonight.Alerts.Should().ContainSingle(a => a.StartsWith("🛰", StringComparison.Ordinal));
        shell.OnlineStatusTooltip.Should().Contain(Strings.OnlineServiceSatellites);
    }

    [Fact]
    public async Task TurningSatellitesOff_EmptiesEverything_WithoutAsking()
    {
        var celestrak = new FakeCelesTrak();
        var shell = await CreateAsync(celestrak);
        var calls = celestrak.Calls;

        shell.Settings.SatellitesEnabled = false;
        shell.Settings.SaveCommand.Execute(null);
        await shell.Satellites!.RefreshAsync(force: true);

        celestrak.Calls.Should().Be(calls);
        shell.Satellites.Satellites.Should().BeEmpty();
        shell.Tonight.SatellitePasses.Should().BeEmpty();
        shell.Tonight.SatellitePassesHint.Should().Be(Strings.SatellitePassesOff);
        shell.Tonight.Alerts.Should().NotContain(a => a.Contains("ISS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailure_IsReported_AndNotRetriedAtOnce()
    {
        var celestrak = new FakeCelesTrak { FailWith = new OnlineServiceException("CelesTrak answered 403.") };
        var shell = await CreateAsync(celestrak);

        await shell.Satellites!.RefreshAsync();

        celestrak.Calls.Should().Be(1, "the next attempt waits SatelliteState.RetryDelay");
        shell.Tonight.SatellitePassesHint.Should().Be(Strings.SatellitePassesWaiting);
        shell.OnlineStatusTooltip.Should().Contain("403");
    }

    [Fact]
    public async Task Sky_DrawsTheIss_WhileItIsUp_AndNamesItsNextPass()
    {
        var shell = await CreateAsync(new FakeCelesTrak());
        var pass = shell.Satellites!.Passes[0];
        var middle = pass.VisibleStartUtc!.Value + ((pass.VisibleEndUtc!.Value - pass.VisibleStartUtc.Value) / 2);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        shell.Observer.ObservationDateTime = TimeZoneInfo.ConvertTimeFromUtc(middle, zone);
        var channel = new FakeSkyChannel();
        shell.Sky.Attach(channel);
        channel.Ready();

        var bodies = channel.Last("movingBodies").GetProperty("bodies").EnumerateArray().ToList();
        var iss = bodies.Single(b => b.GetProperty("kind").GetString() == "satellite");
        iss.GetProperty("id").GetString().Should().Be("sat-25544");
        iss.GetProperty("name").GetString().Should().Be("ISS (ZARYA)");
        iss.GetProperty("track").GetProperty("stepSeconds").GetDouble().Should().Be(10);
        iss.GetProperty("sunlit").EnumerateArray().Should().Contain(v => v.GetInt32() == 1);
        channel.Last("init").GetProperty("layers").GetProperty("satellites").GetBoolean().Should().BeTrue();

        channel.Receive("""{"v":1,"type":"selectObject","payload":{"kind":"body","id":"sat-25544"}}""");
        shell.Sky.SelectedText.Should().StartWith("ISS (ZARYA) · ");
    }

    [Fact]
    public async Task Plan_MarksTheIssCrossingALockedBlock()
    {
        // A target exactly where the ISS is in the middle of its first visible pass, with a block locked around it.
        var probe = await CreateAsync(new FakeCelesTrak());
        var pass = probe.Satellites!.Passes[0];
        var middle = pass.VisibleStartUtc!.Value + ((pass.VisibleEndUtc!.Value - pass.VisibleStartUtc.Value) / 2);
        var observation = SatelliteCalculator.Observe(probe.Satellites.Satellites[0], new ObserverLocation(-34.60, -58.38, 25), middle);
        var j2000 = Precession.Precess(observation.Equatorial, AstronomyMath.ToJulianDate(middle), 2451545.0);
        var target = new CelestialObject("TEST 1", "Satellite lane", "Unknown", CelestialObjectType.OpenCluster, 6.0, j2000) { MajorAxisArcmin = 30 };
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(middle, zone);
        var plan = new FakePlanService
        {
            Entries = [new("TEST 1") { Locked = true, StartLocal = local.AddMinutes(-10), EndLocal = local.AddMinutes(10) }]
        };

        var shell = await CreateAsync(new FakeCelesTrak(), plan: plan, catalog: [target, TestCatalog.M42]);

        var item = shell.Plan.Items.Single(i => i.Id == "TEST 1");
        item.Crossings.Should().ContainSingle().Which.Name.Should().Be("ISS (ZARYA)");
        item.Crossings[0].EnterLocal.Should().BeBefore(local.AddMinutes(1)).And.BeAfter(local.AddMinutes(-10));
        item.CrossingsSummary.Should().Be("1");
        item.CrossingsText.Should().StartWith("ISS (ZARYA) crosses the field ");
        shell.Plan.Timeline!.Rows.Single(r => r.Label == "TEST 1").CrossingsLocal.Should().ContainSingle();
    }
}
