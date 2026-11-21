using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Satellites;

/// <summary>
/// GP element sets from CelesTrak (spec §9.9) in OMM/JSON form (five-digit catalog numbers ran out
/// in 2026, so TLEs are not used): the <c>visual</c> group (the ~150 brightest satellites) and
/// <c>stations</c>. Nothing about the user is sent.
/// CelesTrak asks clients to be gentle: data change at most every 2 hours, a repeated download
/// in that time is answered 403, and too many errors get the address blocked. So each group is
/// cached for <see cref="CacheDuration"/>, its HTTP client has no retries, and after any failure
/// the service does not ask again for <see cref="PauseAfterFailure"/> (the cache is used meanwhile).
/// </summary>
public sealed class CelesTrakService(HttpClient http, TimeProvider? timeProvider = null, IOnlineCache? cache = null) : ISatelliteService
{
    public const string CacheName = "celestrak";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);
    public static readonly TimeSpan PauseAfterFailure = TimeSpan.FromHours(2);
    public static readonly IReadOnlyList<string> Groups = ["visual", "stations"];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private DateTime _pausedUntilUtc = DateTime.MinValue;

    public static string GroupUrl(string group) =>
        string.Create(CultureInfo.InvariantCulture, $"https://celestrak.org/NORAD/elements/gp.php?GROUP={Uri.EscapeDataString(group)}&FORMAT=JSON");

    public async Task<SatelliteCatalog> GetSatellitesAsync(CancellationToken cancellationToken = default)
    {
        var satellites = new Dictionary<int, SatelliteElements>();
        var fetchedAt = DateTime.MaxValue;
        foreach (var group in Groups)
        {
            var fetched = await GetGroupAsync(group, cancellationToken);
            foreach (var elements in ParseOmm(System.Text.Encoding.UTF8.GetString(fetched.Content)))
            {
                satellites.TryAdd(elements.CatalogNumber, elements);
            }

            fetchedAt = fetched.FetchedAtUtc < fetchedAt ? fetched.FetchedAtUtc : fetchedAt;
        }

        return new SatelliteCatalog([.. satellites.Values.OrderBy(s => s.CatalogNumber)], fetchedAt);
    }

    private async Task<FetchedContent> GetGroupAsync(string group, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var cached = cache?.Read(CacheName, GroupUrl(group));   // CachedFetch keys by URL
        bool paused;
        lock (_gate)
        {
            paused = now < _pausedUntilUtc;
        }

        if (paused)
        {
            return cached is not null
                ? new FetchedContent(cached.Content, cached.StoredAtUtc, FromCache: true)
                : throw new OnlineServiceException(string.Create(CultureInfo.InvariantCulture,
                    $"CelesTrak is paused after an error until {_pausedUntilUtc:HH:mm} UTC."));
        }

        try
        {
            var fetched = await CachedFetch.GetAsync(http, cache, CacheName, GroupUrl(group), CacheDuration, now, "CelesTrak",
                (message, inner) => new OnlineServiceException(message, inner), cancellationToken);
            if (fetched.FromCache && (cached is null || now - cached.StoredAtUtc >= CacheDuration))
            {
                // The request failed and the (stale) cache answered: pause anyway.
                Pause(now);
            }

            return fetched;
        }
        catch (OnlineServiceException)
        {
            Pause(now);
            throw;
        }
    }

    private void Pause(DateTime now)
    {
        lock (_gate)
        {
            _pausedUntilUtc = now + PauseAfterFailure;
        }
    }

    /// <summary>
    /// Element sets of a CelesTrak OMM/JSON answer. Entries missing a field or with impossible values
    /// are skipped; a body that is not a JSON array (e.g. CelesTrak's "No GP data found") gives none.
    /// </summary>
    public static IReadOnlyList<SatelliteElements> ParseOmm(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<SatelliteElements>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (Read(entry) is { } elements)
                {
                    result.Add(elements);
                }
            }

            return result;
        }
    }

    private static SatelliteElements? Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !TryNumber(entry, "NORAD_CAT_ID", out var catalog)
            || !entry.TryGetProperty("EPOCH", out var epochText) || epochText.ValueKind != JsonValueKind.String
            || !DateTime.TryParse(epochText.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var epoch)
            || !TryNumber(entry, "MEAN_MOTION", out var meanMotion) || !TryNumber(entry, "ECCENTRICITY", out var eccentricity)
            || !TryNumber(entry, "INCLINATION", out var inclination) || !TryNumber(entry, "RA_OF_ASC_NODE", out var node)
            || !TryNumber(entry, "ARG_OF_PERICENTER", out var perigee) || !TryNumber(entry, "MEAN_ANOMALY", out var anomaly)
            || !TryNumber(entry, "BSTAR", out var bstar)
            || meanMotion <= 0 || eccentricity is < 0 or >= 1 || catalog is < 1 or > int.MaxValue)
        {
            return null;
        }

        return new SatelliteElements((int)catalog, Text(entry, "OBJECT_NAME") is { Length: > 0 } name ? name : ((int)catalog).ToString(CultureInfo.InvariantCulture),
            Text(entry, "OBJECT_ID") ?? "", DateTime.SpecifyKind(epoch, DateTimeKind.Utc),
            meanMotion, eccentricity, inclination, node, perigee, anomaly, bstar);
    }

    private static bool TryNumber(JsonElement entry, string name, out double value)
    {
        value = 0;
        return entry.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()?.Trim() : null;
}
