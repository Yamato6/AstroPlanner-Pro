using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Online.Weather;

/// <summary>
/// Cloud forecast from Open-Meteo (https://open-meteo.com): free and without a key.
/// Coordinates are sent rounded to 2 decimals (~1 km), which is more than enough for a
/// forecast and avoids sending the exact location.
/// </summary>
public sealed class OpenMeteoForecastService(HttpClient http, TimeProvider? timeProvider = null) : IWeatherForecastService
{
    public const string BaseUrl = "https://api.open-meteo.com/v1/forecast";

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public static string BuildUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"{BaseUrl}?latitude={Math.Round(latitude, 2):0.00}&longitude={Math.Round(longitude, 2):0.00}")
        + "&hourly=cloud_cover,cloud_cover_low,cloud_cover_mid,cloud_cover_high,precipitation_probability,relative_humidity_2m"
        + "&timezone=GMT&forecast_days=7";

    public async Task<WeatherForecast> GetForecastAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        OpenMeteoResponse? response;
        try
        {
            using var message = await http.GetAsync(BuildUrl(latitude, longitude), cancellationToken);
            if (!message.IsSuccessStatusCode)
            {
                throw new WeatherForecastException($"The weather service answered {(int)message.StatusCode}.");
            }

            await using var stream = await message.Content.ReadAsStreamAsync(cancellationToken);
            response = await JsonSerializer.DeserializeAsync<OpenMeteoResponse>(stream, cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new WeatherForecastException("No connection to the weather service.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherForecastException("The weather service did not answer in time.", ex);
        }
        catch (JsonException ex)
        {
            throw new WeatherForecastException("Invalid response from the weather service.", ex);
        }

        return Parse(response, latitude, longitude, _time.GetUtcNow().UtcDateTime);
    }

    private static WeatherForecast Parse(OpenMeteoResponse? response, double latitude, double longitude, DateTime fetchedAtUtc)
    {
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
                ValueAt(hourly.RelativeHumidity, i)));
        }

        return new WeatherForecast(latitude, longitude, hours, fetchedAtUtc);
    }

    private static int? ValueAt(List<int?>? values, int index) =>
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
    }
}
