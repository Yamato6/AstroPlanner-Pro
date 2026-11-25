using System.Net;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.SmallBodies;

namespace AstroPlanner.Online.Tests;

/// <summary>M5a: MPC comet and bright-asteroid elements (recorded excerpts only).</summary>
public sealed class MpcSmallBodyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

    private static double Jd(int year, int month, double day) =>
        AstronomyMath.ToJulianDate(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc)) + day - 1;

    [Fact]
    public void Comets_AreReadColumnByColumn()
    {
        var comets = MpcSmallBodyService.ParseComets(OnlineFixtures.Text("mpc-cometels-excerpt.txt"));

        comets.Should().HaveCount(8);
        var atlas = comets.Single(c => c.Designation == "C/2023 A3");
        atlas.Name.Should().Be("Tsuchinshan-ATLAS");
        atlas.Kind.Should().Be(SmallBodyKind.Comet);
        atlas.Elements.Should().BeEquivalentTo(new
        {
            PerihelionDistanceAu = 0.391277,
            Eccentricity = 1.000184,
            ArgumentOfPerihelionDegrees = 308.5914,
            AscendingNodeDegrees = 21.6863,
            InclinationDegrees = 139.0974
        }, o => o.ExcludingMissingMembers());
        atlas.Elements.PerihelionJulianDate.Should().BeApproximately(Jd(2024, 9, 27.8471), 1e-9);
        atlas.Elements.EpochJulianDate.Should().BeApproximately(Jd(2026, 11, 20), 1e-9);
        (atlas.AbsoluteMagnitude, atlas.Slope).Should().Be((6.5, 3.2));

        comets.Select(c => (c.Designation, c.Name)).Should().Contain([
            ("2P", "Encke"), ("1P", "Halley"), ("73P-BT", "Schwassmann-Wachmann"), ("C/1995 O1", "Hale-Bopp"), ("P/1996 R2", "Lagerkvist")]);
        comets.Single(c => c.Designation == "C/1995 O1").AbsoluteMagnitude.Should().Be(-2.0);
    }

    [Fact]
    public void Comets_SkipBrokenLines_AndKeepMissingMagnitudes()
    {
        const string hale = "    CJ95O010  1997 03 29.0300  0.927215  0.994892  130.7417  281.8180   89.7294  20261120  -2.0  4.0  C/1995 O1 (Hale-Bopp)                                    MPC194091";
        var noMagnitude = hale[..91] + "          " + hale[101..];

        var comets = MpcSmallBodyService.ParseComets(string.Join("\n", "garbage", hale[..60], noMagnitude, hale.Replace("0.927215", "-0.92721", StringComparison.Ordinal)));

        comets.Should().ContainSingle();
        comets[0].AbsoluteMagnitude.Should().BeNull();
        comets[0].Slope.Should().BeNull();
        OrbitCalculator.GetPosition(comets[0], Now.UtcDateTime).Magnitude.Should().Be(double.NaN);
    }

    [Fact]
    public void BrightAsteroids_AreReadFromMpcorb()
    {
        var asteroids = MpcSmallBodyService.ParseBrightAsteroids(OnlineFixtures.Text("mpc-bright-2026-excerpt.txt") + "\nnot an asteroid\n");

        asteroids.Should().HaveCount(6);
        var vesta = asteroids.Single(a => a.Name == "Vesta");
        vesta.Designation.Should().Be("(4)");
        (vesta.AbsoluteMagnitude, vesta.Slope).Should().Be((3.25, 0.15));
        vesta.Elements.Eccentricity.Should().Be(0.0901676);
        vesta.Elements.PerihelionDistanceAu.Should().BeApproximately(2.3615413 * (1 - 0.0901676), 1e-9);
        vesta.Elements.EpochJulianDate.Should().BeApproximately(Jd(2025, 11, 21), 1e-9);
    }

    [Theory]
    [InlineData("K25BL", 2025, 11, 21)]
    [InlineData("J99A1", 1999, 10, 1)]
    [InlineData("K26CV", 2026, 12, 31)]
    public void PackedEpochs_Unpack(string packed, int year, int month, int day) =>
        MpcSmallBodyService.UnpackEpoch(packed).Should().BeApproximately(Jd(year, month, day), 1e-9);

    [Theory]
    [InlineData("K25BW")]
    [InlineData("Z25BL")]
    [InlineData("K25D1")]
    [InlineData("K2")]
    public void PackedEpochs_RejectNonsense(string packed) => MpcSmallBodyService.UnpackEpoch(packed).Should().BeNull();

    [Fact]
    public async Task Comets_AreCachedForADay_AndUsedOffline()
    {
        var text = OnlineFixtures.Text("mpc-cometels-excerpt.txt");
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        var cache = new MemoryCache();
        var clock = new ManualTimeProvider(Now);
        var service = new MpcSmallBodyService(new HttpClient(handler), clock, cache);

        var first = await service.GetCometsAsync();
        await service.GetCometsAsync();

        first.Orbits.Should().HaveCount(8);
        handler.Requests.Should().ContainSingle().Which.ToString().Should().Be(MpcSmallBodyService.CometsUrl);

        clock.UtcNow += TimeSpan.FromDays(3);
        var offline = new MpcSmallBodyService(new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("offline"))), clock, cache);
        var stale = await offline.GetCometsAsync();
        stale.FetchedAtUtc.Should().Be(Now.UtcDateTime);
        stale.Orbits.Should().HaveCount(8);

        var empty = new MpcSmallBodyService(new HttpClient(new StubHttpHandler(_ => throw new HttpRequestException("offline"))), clock);
        await FluentActions.Awaiting(() => empty.GetCometsAsync()).Should().ThrowAsync<OnlineServiceException>();
    }

    [Fact]
    public async Task BrightAsteroids_FallBackToLastYearsFile()
    {
        var text = OnlineFixtures.Text("mpc-bright-2026-excerpt.txt");
        var handler = new StubHttpHandler(r => r.RequestUri!.AbsolutePath.Contains("/2027/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        var service = new MpcSmallBodyService(new HttpClient(handler), new ManualTimeProvider(new DateTimeOffset(2027, 1, 3, 0, 0, 0, TimeSpan.Zero)));

        var asteroids = await service.GetBrightAsteroidsAsync();

        asteroids.Orbits.Should().HaveCount(6);
        handler.Requests.Select(u => u.ToString()).Should().Equal(MpcSmallBodyService.BrightAsteroidsUrl(2027), MpcSmallBodyService.BrightAsteroidsUrl(2026));
    }
}
