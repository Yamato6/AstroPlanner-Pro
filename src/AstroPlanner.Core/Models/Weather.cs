namespace AstroPlanner.Core.Models;

/// <summary>Forecast for one hour. Percentages go from 0 to 100.</summary>
/// <param name="TimeUtc">Start of the hour, in UTC.</param>
public sealed record HourlyWeather(
    DateTime TimeUtc,
    int CloudCoverPercent,
    int? LowCloudPercent = null,
    int? MidCloudPercent = null,
    int? HighCloudPercent = null,
    int? PrecipitationProbabilityPercent = null,
    int? RelativeHumidityPercent = null);

/// <summary>Cloud cover summary of an interval (usually the dark night).</summary>
/// <param name="AverageCloudCoverPercent">Average cloud cover of the interval.</param>
/// <param name="ClearestHourUtc">Hour with the fewest clouds (the first one on a tie).</param>
public sealed record CloudSummary(
    double AverageCloudCoverPercent,
    int MinimumCloudCoverPercent,
    int MaximumCloudCoverPercent,
    DateTime ClearestHourUtc,
    int ClearestHourCloudCoverPercent);

/// <summary>Hourly forecast for a location.</summary>
public sealed class WeatherForecast
{
    public WeatherForecast(double latitude, double longitude, IReadOnlyList<HourlyWeather> hours, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(hours);
        Latitude = latitude;
        Longitude = longitude;
        Hours = hours.OrderBy(h => h.TimeUtc).ToList();
        FetchedAtUtc = fetchedAtUtc;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public IReadOnlyList<HourlyWeather> Hours { get; }
    public DateTime FetchedAtUtc { get; }

    /// <summary>The forecast hour containing <paramref name="utc"/>, or null if it is outside the forecast.</summary>
    public HourlyWeather? At(DateTime utc)
    {
        foreach (var hour in Hours)
        {
            if (utc >= hour.TimeUtc && utc < hour.TimeUtc.AddHours(1))
            {
                return hour;
            }
        }

        return null;
    }

    /// <summary>Hours overlapping [<paramref name="startUtc"/>, <paramref name="endUtc"/>).</summary>
    public IReadOnlyList<HourlyWeather> Between(DateTime startUtc, DateTime endUtc) =>
        Hours.Where(h => h.TimeUtc.AddHours(1) > startUtc && h.TimeUtc < endUtc).ToList();

    /// <summary>
    /// Cloud cover summary of the interval, or null if the forecast does not cover it (past
    /// dates or beyond the forecast horizon).
    /// </summary>
    public CloudSummary? Summarize(DateTime startUtc, DateTime endUtc)
    {
        var hours = Between(startUtc, endUtc);
        if (hours.Count == 0)
        {
            return null;
        }

        var clearest = hours.MinBy(h => h.CloudCoverPercent)!;
        return new CloudSummary(
            hours.Average(h => h.CloudCoverPercent),
            hours.Min(h => h.CloudCoverPercent),
            hours.Max(h => h.CloudCoverPercent),
            clearest.TimeUtc,
            clearest.CloudCoverPercent);
    }
}

/// <summary>The forecast could not be obtained (offline, service error, invalid response).</summary>
public sealed class WeatherForecastException(string message, Exception? innerException = null)
    : Exception(message, innerException);
