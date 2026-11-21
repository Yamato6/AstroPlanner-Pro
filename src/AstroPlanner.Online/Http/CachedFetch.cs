using System.Net;
using System.Net.Http;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Online.Http;

/// <summary>A response body and when it was fetched from the service.</summary>
/// <param name="FromCache">True when it came from the cache (fresh, or stale because the service failed).</param>
/// <param name="Content">The body; empty for a cached "not found" (see <c>cacheNotFound</c>).</param>
public sealed record FetchedContent(byte[] Content, DateTime FetchedAtUtc, bool FromCache)
{
    public bool IsNotFound => Content.Length == 0;
}

/// <summary>
/// GET with the disk cache in front (spec §9): a cached response younger than
/// <c>maxAge</c> is used without any request; otherwise each mirror is asked in turn, and if all
/// fail the last cached response is returned whatever its age, so that the app keeps working
/// offline. Only when there is nothing cached does the failure reach the caller, as the exception
/// built by <c>fail</c> (an <see cref="OnlineServiceException"/>) with a short reason.
/// </summary>
internal static class CachedFetch
{
    public static Task<FetchedContent> GetAsync(
        HttpClient http,
        IOnlineCache? cache,
        string service,
        string url,
        TimeSpan maxAge,
        DateTime nowUtc,
        string serviceName,
        Func<string, Exception?, OnlineServiceException> fail,
        CancellationToken cancellationToken,
        bool cacheNotFound = false) =>
        GetAsync(http, cache, service, url, [url], maxAge, nowUtc, serviceName, fail, cancellationToken, cacheNotFound);

    /// <param name="cacheKey">Key of the answer in the cache, the same whichever mirror serves it.</param>
    /// <param name="urls">The same request on each mirror, in order of preference.</param>
    /// <param name="cacheNotFound">A 404 is an answer ("no such page"), cached as empty content.</param>
    public static async Task<FetchedContent> GetAsync(
        HttpClient http,
        IOnlineCache? cache,
        string service,
        string cacheKey,
        IReadOnlyList<string> urls,
        TimeSpan maxAge,
        DateTime nowUtc,
        string serviceName,
        Func<string, Exception?, OnlineServiceException> fail,
        CancellationToken cancellationToken,
        bool cacheNotFound = false)
    {
        var cached = cache?.Read(service, cacheKey);
        if (cached is not null && nowUtc - cached.StoredAtUtc < maxAge && nowUtc >= cached.StoredAtUtc)
        {
            return new FetchedContent(cached.Content, cached.StoredAtUtc, FromCache: true);
        }

        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                var content = await FetchAsync(http, url, serviceName, fail, cacheNotFound, cancellationToken);
                cache?.Write(service, cacheKey, content, nowUtc);
                return new FetchedContent(content, nowUtc, FromCache: false);
            }
            catch (Exception ex) when (IsServiceFailure(ex, cancellationToken))
            {
                last = ex;
            }
        }

        if (cached is not null)
        {
            return new FetchedContent(cached.Content, cached.StoredAtUtc, FromCache: true);
        }

        throw last switch
        {
            OnlineServiceException online => online,
            HttpRequestException => fail($"No connection to {serviceName}.", last),
            _ => fail($"{serviceName} did not answer in time.", last)
        };
    }

    /// <summary>One request: the body, empty for a 404 when <paramref name="notFoundIsEmpty"/>.</summary>
    /// <exception cref="OnlineServiceException">Any other unsuccessful status.</exception>
    public static async Task<byte[]> FetchAsync(
        HttpClient http,
        string url,
        string serviceName,
        Func<string, Exception?, OnlineServiceException> fail,
        bool notFoundIsEmpty,
        CancellationToken cancellationToken)
    {
        using var message = await http.GetAsync(url, cancellationToken);
        if (notFoundIsEmpty && message.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!message.IsSuccessStatusCode)
        {
            throw fail($"{serviceName} answered {(int)message.StatusCode}.", null);
        }

        return await message.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>The service could not answer (as opposed to the caller cancelling).</summary>
    public static bool IsServiceFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or OnlineServiceException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);
}
