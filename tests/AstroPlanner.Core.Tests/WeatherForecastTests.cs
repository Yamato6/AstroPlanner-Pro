using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

/// <summary>Summary of the hourly forecast.</summary>
public sealed class WeatherForecastTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Mexico City (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    /// <summary>Forecast for Mexico City: clear from 06 to 10 UTC (00-04 local), cloudy the rest.</summary>
    private static WeatherForecast CdmxForecast(DateTime fetchedAtUtc, double latitude = 19.4326, double longitude = -99.1332)
    {
        var start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var hours = Enumerable.Range(0, 48)
            .Select(i => new HourlyWeather(start.AddHours(i), i is >= 30 and < 34 ? 5 : 80))
            .ToList();
        return new WeatherForecast(latitude, longitude, hours, fetchedAtUtc);
    }

    [Fact]
    public void Summarize_ReportsAverageAndClearestHour()
    {
        var forecast = CdmxForecast(WinterNightUtc.UtcDateTime);

        // 2026-01-16 02:00 → 10:00 UTC: 4 hours at 80% and 4 at 5% (06-10 UTC).
        var summary = forecast.Summarize(
            new DateTime(2026, 1, 16, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc))!;

        summary.AverageCloudCoverPercent.Should().BeApproximately(42.5, 0.01);
        summary.MinimumCloudCoverPercent.Should().Be(5);
        summary.ClearestHourUtc.Should().Be(new DateTime(2026, 1, 16, 6, 0, 0, DateTimeKind.Utc));
        forecast.Summarize(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc))
            .Should().BeNull("it is beyond the forecast horizon");
    }
}
