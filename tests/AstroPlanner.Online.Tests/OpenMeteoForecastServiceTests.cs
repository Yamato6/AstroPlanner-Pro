using System.Globalization;
using System.Net;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Weather;

namespace AstroPlanner.Online.Tests;

/// <summary>Open-Meteo cloud forecast without network: HTTP responses are stubbed.</summary>
public sealed class OpenMeteoForecastServiceTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    /// <summary>Open-Meteo response with 48 hours from 2026-01-15 00:00 UTC.</summary>
    private static string OpenMeteoJson(Func<int, int> cloudAtHour)
    {
        var start = new DateTime(2026, 1, 15, 0, 0, 0);
        var times = Enumerable.Range(0, 48).Select(i => $"\"{start.AddHours(i):yyyy-MM-dd'T'HH:mm}\"");
        var clouds = Enumerable.Range(0, 48).Select(i => cloudAtHour(i).ToString(CultureInfo.InvariantCulture));
        return "{\"latitude\":19.43,\"longitude\":-99.13,\"timezone\":\"GMT\",\"hourly\":{"
            + $"\"time\":[{string.Join(",", times)}],"
            + $"\"cloud_cover\":[{string.Join(",", clouds)}],"
            + $"\"cloud_cover_low\":[{string.Join(",", Enumerable.Repeat("null", 48))}],"
            + $"\"precipitation_probability\":[{string.Join(",", Enumerable.Repeat("10", 48))}]"
            + "}}";
    }

    [Fact]
    public void OpenMeteoUrl_RoundsCoordinatesAndUsesInvariantCulture()
    {
        var url = OpenMeteoForecastService.BuildUrl(21.88534, -102.29164);

        url.Should().StartWith("https://api.open-meteo.com/v1/forecast?latitude=21.89&longitude=-102.29&")
            .And.Contain("hourly=cloud_cover").And.Contain("timezone=GMT").And.Contain("forecast_days=7");
    }

    [Fact]
    public async Task OpenMeteo_ParsesHourlyCloudCoverInUtc()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(OpenMeteoJson(i => i)));
        var service = new OpenMeteoForecastService(new HttpClient(handler), new ManualTimeProvider(WinterNightUtc));

        var forecast = await service.GetForecastAsync(19.4326, -99.1332);

        forecast.Hours.Should().HaveCount(48);
        forecast.Hours[0].TimeUtc.Should().Be(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        forecast.Hours[0].TimeUtc.Kind.Should().Be(DateTimeKind.Utc);
        forecast.At(new DateTime(2026, 1, 16, 4, 30, 0, DateTimeKind.Utc))!.CloudCoverPercent.Should().Be(28);
        forecast.Hours[0].PrecipitationProbabilityPercent.Should().Be(10);
        forecast.Hours[0].LowCloudPercent.Should().BeNull();
        forecast.FetchedAtUtc.Should().Be(WinterNightUtc.UtcDateTime);
        handler.Requests.Single().Query.Should().Contain("latitude=19.43");
    }

    [Theory]
    [InlineData("server-error")]
    [InlineData("bad-json")]
    [InlineData("incomplete")]
    [InlineData("no-network")]
    public async Task OpenMeteo_FailuresBecomeWeatherForecastException(string failure)
    {
        var handler = new StubHttpHandler(_ => failure switch
        {
            "server-error" => StubHttpHandler.Json("{}", HttpStatusCode.InternalServerError),
            "bad-json" => StubHttpHandler.Json("{ not json"),
            "incomplete" => StubHttpHandler.Json("""{"hourly":{"time":["2026-01-15T00:00"]}}"""),
            _ => throw new HttpRequestException("no network")
        });
        var service = new OpenMeteoForecastService(new HttpClient(handler));

        var act = () => service.GetForecastAsync(19.43, -99.13);

        await act.Should().ThrowAsync<WeatherForecastException>();
    }
}
