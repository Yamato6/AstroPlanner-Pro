using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>Hourly weather forecast.</summary>
public interface IWeatherForecastService
{
    /// <summary>
    /// Forecast for the next days at the location.
    /// </summary>
    /// <exception cref="WeatherForecastException">If it could not be obtained.</exception>
    Task<WeatherForecast> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
