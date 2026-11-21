using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Weather;

/// <summary>
/// Hourly forecast from Open-Meteo (https://open-meteo.com, free, no key; spec §9.1): cloud cover by
/// layer, precipitation probability, humidity, temperature, dew point and wind. Coordinates are sent
/// rounded to 2 decimals (~1 km), which is more than enough for a forecast and avoids sending the
/// exact location. Responses are cached for <see cref="CacheDuration"/> and reused when the service
/// cannot be reached.
/// </summary>
public sealed class OpenMeteoForecastService(HttpClient http, TimeProvider? timeProvider = null, IOnlineCache? cache = null)
    : IWeatherForecastService
{
    public const string BaseUrl = "https://api.open-meteo.com/v1/forecast";
    public const string CacheName = "open-meteo";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public static string BuildUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"{BaseUrl}?latitude={Math.Round(latitude, 2):0.00}&longitude={Math.Round(longitude, 2):0.00}")
        + "&hourly=cloud_cover,cloud_cover_low,cloud_cover_mid,cloud_cover_high,precipitation_probability,relative_humidity_2m"
        + ",temperature_2m,dew_point_2m,wind_speed_10m,wind_gusts_10m"
        + "&timezone=GMT&forecast_days=7";

    public async Task<WeatherForecast> GetForecastAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, BuildUrl(latitude, longitude), CacheDuration,
            _time.GetUtcNow().UtcDateTime, "The weather service",
            (message, inner) => new WeatherForecastException(message, inner), cancellationToken);
        return Parse(fetched.Content, latitude, longitude, fetched.FetchedAtUtc);
    }

    /// <exception cref="WeatherForecastException">Invalid or incomplete response.</exception>
    public static WeatherForecast Parse(byte[] json, double latitude, double longitude, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(json);
        OpenMeteoResponse? response;
        try
        {
            response = JsonSerializer.Deserialize<OpenMeteoResponse>(json);
        }
        catch (JsonException ex)
        {
            throw new WeatherForecastException("Invalid response from the weather service.", ex);
        }

        var hourly = response?.Hourly;
        if (hourly?.Time is not { Count: > 0 } times || hourly.CloudCover is not { } cloud || cloud.Count != times.Count)
        {
            throw new WeatherForecastException("Incomplete response from the weather service.");
        }

        var hours = new List<HourlyWeather>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            if (cloud[i] is not { } cover
                || !DateTime.TryParseExact(times[i], "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            {
                continue; // hours without data are skipped
            }

            hours.Add(new HourlyWeather(
                DateTime.SpecifyKind(time, DateTimeKind.Utc),
                cover,
                ValueAt(hourly.CloudCoverLow, i),
                ValueAt(hourly.CloudCoverMid, i),
                ValueAt(hourly.CloudCoverHigh, i),
                ValueAt(hourly.PrecipitationProbability, i),
                ValueAt(hourly.RelativeHumidity, i))
            {
                TemperatureC = ValueAt(hourly.Temperature, i),
                DewPointC = ValueAt(hourly.DewPoint, i),
                WindSpeedKmh = ValueAt(hourly.WindSpeed, i),
                WindGustKmh = ValueAt(hourly.WindGusts, i)
            });
        }

        return new WeatherForecast(latitude, longitude, hours, fetchedAtUtc);
    }

    private static T? ValueAt<T>(List<T?>? values, int index) where T : struct =>
        values is not null && index < values.Count ? values[index] : null;

    private sealed class OpenMeteoResponse
    {
        [JsonPropertyName("hourly")] public HourlyData? Hourly { get; set; }
    }

    private sealed class HourlyData
    {
        [JsonPropertyName("time")] public List<string>? Time { get; set; }
        [JsonPropertyName("cloud_cover")] public List<int?>? CloudCover { get; set; }
        [JsonPropertyName("cloud_cover_low")] public List<int?>? CloudCoverLow { get; set; }
        [JsonPropertyName("cloud_cover_mid")] public List<int?>? CloudCoverMid { get; set; }
        [JsonPropertyName("cloud_cover_high")] public List<int?>? CloudCoverHigh { get; set; }
        [JsonPropertyName("precipitation_probability")] public List<int?>? PrecipitationProbability { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public List<int?>? RelativeHumidity { get; set; }
        [JsonPropertyName("temperature_2m")] public List<double?>? Temperature { get; set; }
        [JsonPropertyName("dew_point_2m")] public List<double?>? DewPoint { get; set; }
        [JsonPropertyName("wind_speed_10m")] public List<double?>? WindSpeed { get; set; }
        [JsonPropertyName("wind_gusts_10m")] public List<double?>? WindGusts { get; set; }
    }
}
