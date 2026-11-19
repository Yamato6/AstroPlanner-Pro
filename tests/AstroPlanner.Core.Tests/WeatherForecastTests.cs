using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

/// <summary>Resumen y descripción del pronóstico por horas.</summary>
public sealed class WeatherForecastTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 en CDMX (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    /// <summary>Pronóstico para CDMX: despejado de 06 a 10 UTC (00-04 local), nublado el resto.</summary>
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

        // 16/1 02:00 → 10:00 UTC: 4 horas al 80 % y 4 al 5 % (06-10 UTC).
        var summary = forecast.Summarize(
            new DateTime(2026, 1, 16, 2, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 16, 10, 0, 0, DateTimeKind.Utc))!;

        summary.AverageCloudCoverPercent.Should().BeApproximately(42.5, 0.01);
        summary.MinimumCloudCoverPercent.Should().Be(5);
        summary.ClearestHourUtc.Should().Be(new DateTime(2026, 1, 16, 6, 0, 0, DateTimeKind.Utc));
        forecast.Summarize(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc))
            .Should().BeNull("fuera del horizonte de pronóstico");
        WeatherForecast.Describe(5).Should().Be("Despejado");
        WeatherForecast.Describe(95).Should().Be("Cubierto");
    }
}
