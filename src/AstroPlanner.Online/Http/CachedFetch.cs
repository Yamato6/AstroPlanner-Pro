using System.Net.Http;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Online.Http;

/// <summary>A response body and when it was fetched from the service.</summary>
/// <param name="FromCache">True when it came from the cache (fresh, or stale because the service failed).</param>
public sealed record FetchedContent(byte[] Content, DateTime FetchedAtUtc, bool FromCache);

/// <summary>
/// GET with the disk cache in front (spec §9): a cached response younger than
/// <c>maxAge</c> is used without any request; otherwise the service is asked, and if it fails the
/// last cached response is returned whatever its age, so that the app keeps working offline.
/// Only when there is nothing cached does the failure reach the caller, as
/// <see cref="WeatherForecastException"/> with a short reason.
/// </summary>
internal static class CachedFetch
{
    public static async Task<FetchedContent> GetAsync(
        HttpClient http,
        IOnlineCache? cache,
        string service,
        string url,
        TimeSpan maxAge,
        DateTime nowUtc,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var cached = cache?.Read(service, url);
        if (cached is not null && nowUtc - cached.StoredAtUtc < maxAge && nowUtc >= cached.StoredAtUtc)
        {
            return new FetchedContent(cached.Content, cached.StoredAtUtc, FromCache: true);
        }

        try
        {
            using var message = await http.GetAsync(url, cancellationToken);
            if (!message.IsSuccessStatusCode)
            {
                throw new WeatherForecastException($"{serviceName} answered {(int)message.StatusCode}.");
            }

            var content = await message.Content.ReadAsByteArrayAsync(cancellationToken);
            cache?.Write(service, url, content, nowUtc);
            return new FetchedContent(content, nowUtc, FromCache: false);
        }
        catch (Exception ex) when (cached is not null && IsServiceFailure(ex, cancellationToken))
        {
            return new FetchedContent(cached.Content, cached.StoredAtUtc, FromCache: true);
        }
        catch (HttpRequestException ex)
        {
            throw new WeatherForecastException($"No connection to {serviceName}.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WeatherForecastException($"{serviceName} did not answer in time.", ex);
        }
    }

    private static bool IsServiceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or WeatherForecastException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
