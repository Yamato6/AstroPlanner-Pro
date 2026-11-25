using System.Net;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Online.Satellites;

namespace AstroPlanner.Online.Tests;

/// <summary>M5b: CelesTrak GP elements (recorded answers only) and its gentle-client rules.</summary>
public sealed class CelesTrakTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);

    private static HttpResponseMessage Recorded(HttpRequestMessage request) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(OnlineFixtures.Text(request.RequestUri!.Query.Contains("GROUP=visual", StringComparison.Ordinal)
                ? "celestrak-visual.json" : "celestrak-stations.json"))
        };

    [Fact]
    public void Omm_IsRead()
    {
        var stations = CelesTrakService.ParseOmm(OnlineFixtures.Text("celestrak-stations.json"));

        stations.Should().HaveCount(17);
        var iss = stations.Single(s => s.CatalogNumber == 25544);
        iss.Should().BeEquivalentTo(new
        {
            Name = "ISS (ZARYA)",
            InternationalDesignator = "1998-067A",
            MeanMotionRevPerDay = 15.49518511,
            Eccentricity = 0.00073891,
            InclinationDegrees = 51.6319,
            AscendingNodeDegrees = 244.6605,
            ArgumentOfPerigeeDegrees = 48.0812,
            MeanAnomalyDegrees = 312.0806,
            BStar = 0.00018413556
        }, o => o.ExcludingMissingMembers());
        iss.EpochUtc.Should().Be(new DateTime(2026, 11, 20, 21, 9, 47, DateTimeKind.Utc).AddTicks(7515520));
        iss.EpochUtc.Kind.Should().Be(DateTimeKind.Utc);
        stations.Should().Contain(s => s.CatalogNumber > 99_999, "six-digit catalog numbers are why OMM is used");
    }

    [Theory]
    [InlineData("No GP data found")]
    [InlineData("{\"error\":1}")]
    [InlineData("[{\"NORAD_CAT_ID\":1,\"EPOCH\":\"2026-11-20T00:00:00\",\"MEAN_MOTION\":-1}, 3, {\"OBJECT_NAME\":\"x\"}]")]
    public void Omm_SkipsWhatItCannotRead(string body) => CelesTrakService.ParseOmm(body).Should().BeEmpty();

    [Fact]
    public async Task BothGroups_AreMerged_AndEverySatelliteCanBePropagated()
    {
        var handler = new StubHttpHandler(Recorded);
        var service = new CelesTrakService(new HttpClient(handler), new ManualTimeProvider(Now));

        var catalog = await service.GetSatellitesAsync();

        handler.Requests.Select(u => u.ToString()).Should().Equal(CelesTrakService.GroupUrl("visual"), CelesTrakService.GroupUrl("stations"));
        catalog.Satellites.Should().OnlyHaveUniqueItems(s => s.CatalogNumber).And.HaveCountGreaterThan(160);
        catalog.Satellites.Should().OnlyContain(s => s.IsNearEarth);
        var tracked = TrackedSatellite.From(catalog.Satellites);
        tracked.Should().Contain(s => s.CatalogNumber == 25544).And.NotContain(s => s.CatalogNumber == 36086, "POISK is docked to the ISS");
        foreach (var satellite in tracked)
        {
            FluentActions.Invoking(() => satellite.Propagator.Propagate(Now.UtcDateTime)).Should().NotThrow(satellite.Name);
        }
    }

    [Fact]
    public async Task Groups_AreCachedForTwelveHours()
    {
        var handler = new StubHttpHandler(Recorded);
        var cache = new MemoryCache();
        var clock = new ManualTimeProvider(Now);
        var service = new CelesTrakService(new HttpClient(handler), clock, cache);

        await service.GetSatellitesAsync();
        clock.UtcNow += TimeSpan.FromHours(11);
        await service.GetSatellitesAsync();
        handler.Requests.Should().HaveCount(2);

        clock.UtcNow += TimeSpan.FromHours(2);
        var fresh = await service.GetSatellitesAsync();
        handler.Requests.Should().HaveCount(4);
        fresh.FetchedAtUtc.Should().Be(clock.UtcNow.UtcDateTime);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task AfterAnError_ItDoesNotAskAgainForTwoHours(HttpStatusCode status)
    {
        var cache = new MemoryCache();
        var clock = new ManualTimeProvider(Now);
        await new CelesTrakService(new HttpClient(new StubHttpHandler(Recorded)), clock, cache).GetSatellitesAsync();

        clock.UtcNow += TimeSpan.FromHours(13);
        var failing = new StubHttpHandler(_ => new HttpResponseMessage(status));
        var service = new CelesTrakService(new HttpClient(failing), clock, cache);
        var stale = await service.GetSatellitesAsync();
        stale.FetchedAtUtc.Should().Be(Now.UtcDateTime, "the old elements are used meanwhile");
        var requestsAfterFailure = failing.Requests.Count;

        clock.UtcNow += TimeSpan.FromMinutes(90);
        await service.GetSatellitesAsync();
        failing.Requests.Should().HaveCount(requestsAfterFailure, "paused");

        clock.UtcNow += TimeSpan.FromMinutes(31);
        await service.GetSatellitesAsync();
        failing.Requests.Should().HaveCountGreaterThan(requestsAfterFailure);
    }

    [Fact]
    public async Task NothingCached_AndOffline_IsAnError()
    {
        var failing = new StubHttpHandler(_ => throw new HttpRequestException("offline"));
        var service = new CelesTrakService(new HttpClient(failing), new ManualTimeProvider(Now));

        await FluentActions.Awaiting(() => service.GetSatellitesAsync()).Should().ThrowAsync<OnlineServiceException>();
        await FluentActions.Awaiting(() => service.GetSatellitesAsync()).Should().ThrowAsync<OnlineServiceException>()
            .WithMessage("*paused*");
        failing.Requests.Should().ContainSingle();
    }
}
