using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Weather;

/// <summary>
/// Seeing and transparency from 7Timer! ASTRO (https://www.7timer.info, free, no key; spec §9.2):
/// 3-hour steps for about 3 days. Coordinates are sent rounded to 2 decimals (~1 km). The responses
/// are cached for <see cref="CacheDuration"/> and reused when the service cannot be reached.
/// </summary>
public sealed class SevenTimerAstroService(HttpClient http, TimeProvider? timeProvider = null, IOnlineCache? cache = null)
    : IAstroForecastService
{
    public const string BaseUrl = "https://www.7timer.info/bin/astro.php";
    public const string CacheName = "7timer";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(3);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public static string BuildUrl(double latitude, double longitude) =>
        FormattableString.Invariant($"{BaseUrl}?lon={Math.Round(longitude, 2):0.00}&lat={Math.Round(latitude, 2):0.00}")
        + "&ac=0&unit=metric&output=json&tzshift=0";

    public async Task<AstroForecast> GetForecastAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, BuildUrl(latitude, longitude), CacheDuration,
            _time.GetUtcNow().UtcDateTime, "7Timer!",
            (message, inner) => new WeatherForecastException(message, inner), cancellationToken);
        return Parse(fetched.Content, latitude, longitude, fetched.FetchedAtUtc);
    }

    /// <exception cref="WeatherForecastException">Invalid or incomplete response.</exception>
    public static AstroForecast Parse(byte[] json, double latitude, double longitude, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(json);
        Response? response;
        try
        {
            response = JsonSerializer.Deserialize<Response>(json);
        }
        catch (JsonException ex)
        {
            throw new WeatherForecastException("Invalid response from 7Timer!.", ex);
        }

        if (response?.Init is not { } init
            || !DateTime.TryParseExact(init, "yyyyMMddHH", CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var initUtc)
            || response.Series is not { Count: > 0 } series)
        {
            throw new WeatherForecastException("Incomplete response from 7Timer!.");
        }

        var points = new List<AstroForecastPoint>(series.Count);
        foreach (var item in series)
        {
            // 7Timer! marks missing values with -9999; such points are skipped.
            if (item.TimePoint is not { } hours || !InRange(item.CloudCover, 9) || !InRange(item.Seeing, 8) || !InRange(item.Transparency, 8))
            {
                continue;
            }

            points.Add(new AstroForecastPoint(
                DateTime.SpecifyKind(initUtc.AddHours(hours), DateTimeKind.Utc),
                item.CloudCover!.Value,
                item.Seeing!.Value,
                item.Transparency!.Value,
                item.Wind?.Speed is >= 1 and <= 8 ? item.Wind.Speed.Value : 0));
        }

        if (points.Count == 0)
        {
            throw new WeatherForecastException("Incomplete response from 7Timer!.");
        }

        return new AstroForecast(latitude, longitude, points, fetchedAtUtc);
    }

    private static bool InRange(int? value, int maximum) => value is { } v && v >= 1 && v <= maximum;

    private sealed class Response
    {
        [JsonPropertyName("init")] public string? Init { get; set; }
        [JsonPropertyName("dataseries")] public List<Point>? Series { get; set; }
    }

    private sealed class Point
    {
        [JsonPropertyName("timepoint")] public int? TimePoint { get; set; }
        [JsonPropertyName("cloudcover")] public int? CloudCover { get; set; }
        [JsonPropertyName("seeing")] public int? Seeing { get; set; }
        [JsonPropertyName("transparency")] public int? Transparency { get; set; }
        [JsonPropertyName("wind10m")] public Wind? Wind { get; set; }
    }

    private sealed class Wind
    {
        [JsonPropertyName("speed")] public int? Speed { get; set; }
    }
}
