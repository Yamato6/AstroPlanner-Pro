using System.Net;
using System.Text;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Weather;

namespace AstroPlanner.Online.Tests;

/// <summary>M4: 7Timer! ASTRO, the new Open-Meteo fields and the cache in front of both (recorded fixtures only).</summary>
public sealed class SevenTimerAndCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SevenTimerUrl_RoundsCoordinates()
    {
        SevenTimerAstroService.BuildUrl(21.88534, -102.29164).Should().Be(
            "https://www.7timer.info/bin/astro.php?lon=-102.29&lat=21.89&ac=0&unit=metric&output=json&tzshift=0");
    }

    [Fact]
    public async Task SevenTimer_ParsesTheRecordedForecast()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(OnlineFixtures.Text("7timer-astro.json")));
        var service = new SevenTimerAstroService(new HttpClient(handler), new ManualTimeProvider(Now));

        var forecast = await service.GetForecastAsync(21.88, -102.29);

        forecast.Points.Should().HaveCount(24);
        var first = forecast.Points[0];
        first.Should().BeEquivalentTo(new { CloudCoverClass = 6, SeeingClass = 1, TransparencyClass = 2, WindSpeedClass = 2 });
        first.TimeUtc.Should().Be(new DateTime(2026, 11, 20, 15, 0, 0, DateTimeKind.Utc), "init 2026112012 + 3 h");
        first.TimeUtc.Kind.Should().Be(DateTimeKind.Utc);
        forecast.Points[^1].TimeUtc.Should().Be(new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc));
        forecast.Points.Should().OnlyContain(p => p.SeeingClass >= 1 && p.SeeingClass <= 8 && p.TransparencyClass >= 1 && p.TransparencyClass <= 8);
        forecast.FetchedAtUtc.Should().Be(Now.UtcDateTime);
        handler.Requests.Single().Query.Should().Contain("lat=21.88").And.Contain("lon=-102.29");
    }

    [Fact]
    public void SevenTimer_SkipsMissingValues_AndRejectsEmptyAnswers()
    {
        var json = """{"init":"2026112012","dataseries":[{"timepoint":3,"cloudcover":2,"seeing":-9999,"transparency":2},{"timepoint":6,"cloudcover":2,"seeing":3,"transparency":4,"wind10m":{"speed":3}}]}""";

        var forecast = SevenTimerAstroService.Parse(Encoding.UTF8.GetBytes(json), 0, 0, Now.UtcDateTime);

        forecast.Points.Should().ContainSingle().Which.SeeingClass.Should().Be(3);
        var empty = () => SevenTimerAstroService.Parse("""{"init":"2026112012","dataseries":[]}"""u8.ToArray(), 0, 0, Now.UtcDateTime);
        var noInit = () => SevenTimerAstroService.Parse("""{"dataseries":[{"timepoint":3}]}"""u8.ToArray(), 0, 0, Now.UtcDateTime);
        var broken = () => SevenTimerAstroService.Parse("{ nope"u8.ToArray(), 0, 0, Now.UtcDateTime);
        empty.Should().Throw<WeatherForecastException>();
        noInit.Should().Throw<WeatherForecastException>();
        broken.Should().Throw<WeatherForecastException>();
    }

    [Fact]
    public async Task OpenMeteo_ReadsTemperatureDewPointAndWind()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(OnlineFixtures.Text("open-meteo-forecast.json")));
        var service = new OpenMeteoForecastService(new HttpClient(handler), new ManualTimeProvider(Now));

        var forecast = await service.GetForecastAsync(21.88, -102.29);

        forecast.Hours.Should().HaveCount(168);
        var first = forecast.Hours[0];
        first.TimeUtc.Should().Be(new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
        first.TemperatureC.Should().Be(21.3);
        first.DewPointC.Should().Be(3.7);
        first.WindGustKmh.Should().Be(12.6);
        first.WindSpeedKmh.Should().NotBeNull();
        handler.Requests.Single().Query.Should().Contain("dew_point_2m").And.Contain("wind_gusts_10m");
    }

    [Fact]
    public async Task FreshCache_AvoidsTheRequest()
    {
        var cache = new MemoryCache();
        var url = SevenTimerAstroService.BuildUrl(21.88, -102.29);
        cache.Write(SevenTimerAstroService.CacheName, url, Encoding.UTF8.GetBytes(OnlineFixtures.Text("7timer-astro.json")), Now.UtcDateTime.AddHours(-1));
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("no request expected"));
        var service = new SevenTimerAstroService(new HttpClient(handler), new ManualTimeProvider(Now), cache);

        var forecast = await service.GetForecastAsync(21.88, -102.29);

        handler.Requests.Should().BeEmpty();
        forecast.FetchedAtUtc.Should().Be(Now.UtcDateTime.AddHours(-1));
    }

    [Fact]
    public async Task OldCache_IsRefreshed_AndStored()
    {
        var cache = new MemoryCache();
        var url = OpenMeteoForecastService.BuildUrl(21.88, -102.29);
        cache.Write(OpenMeteoForecastService.CacheName, url, "{}"u8.ToArray(), Now.UtcDateTime.AddHours(-2));
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(OnlineFixtures.Text("open-meteo-forecast.json")));
        var service = new OpenMeteoForecastService(new HttpClient(handler), new ManualTimeProvider(Now), cache);

        var forecast = await service.GetForecastAsync(21.88, -102.29);

        handler.Requests.Should().ContainSingle();
        forecast.FetchedAtUtc.Should().Be(Now.UtcDateTime);
        cache.Read(OpenMeteoForecastService.CacheName, url)!.StoredAtUtc.Should().Be(Now.UtcDateTime);
    }

    [Theory]
    [InlineData("no-network")]
    [InlineData("server-error")]
    [InlineData("timeout")]
    public async Task Offline_TheLastCachedForecastIsUsed_WhateverItsAge(string failure)
    {
        var cache = new MemoryCache();
        var url = SevenTimerAstroService.BuildUrl(21.88, -102.29);
        var stored = Now.UtcDateTime.AddDays(-2);
        cache.Write(SevenTimerAstroService.CacheName, url, Encoding.UTF8.GetBytes(OnlineFixtures.Text("7timer-astro.json")), stored);
        var handler = new StubHttpHandler(_ => failure switch
        {
            "server-error" => StubHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable),
            "timeout" => throw new TaskCanceledException("timeout"),
            _ => throw new HttpRequestException("offline")
        });
        var service = new SevenTimerAstroService(new HttpClient(handler), new ManualTimeProvider(Now), cache);

        var forecast = await service.GetForecastAsync(21.88, -102.29);

        forecast.FetchedAtUtc.Should().Be(stored, "the age tells the UI to say 'as of'");
        forecast.Points.Should().HaveCount(24);
    }

    [Theory]
    [InlineData("no-network")]
    [InlineData("server-error")]
    [InlineData("timeout")]
    public async Task Offline_WithoutCache_FailsWithAShortReason(string failure)
    {
        var handler = new StubHttpHandler(_ => failure switch
        {
            "server-error" => StubHttpHandler.Json("{}", HttpStatusCode.ServiceUnavailable),
            "timeout" => throw new TaskCanceledException("timeout"),
            _ => throw new HttpRequestException("offline")
        });
        var service = new SevenTimerAstroService(new HttpClient(handler), new ManualTimeProvider(Now), new MemoryCache());

        var act = () => service.GetForecastAsync(21.88, -102.29);

        (await act.Should().ThrowAsync<WeatherForecastException>()).Which.Message.Should().Contain("7Timer!");
    }
}
