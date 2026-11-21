using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Info;

/// <summary>
/// Object descriptions from the English Wikipedia REST API (page summary) and the license of their
/// image from Wikimedia Commons (spec §4.4, §9). Only the article title (an object name) is sent.
/// Answers are cached for <see cref="CacheDuration"/>, "no article" included. An image is offered
/// only when it is on Commons with a license name; Wikipedia-only (non-free) images are not shown.
/// The text is CC BY-SA 4.0: the UI credits Wikipedia and links to the article.
/// </summary>
public sealed partial class WikipediaSummaryService(HttpClient http, IOnlineCache? cache = null, TimeProvider? timeProvider = null)
    : IObjectInfoService
{
    public const string SummaryUrl = "https://en.wikipedia.org/api/rest_v1/page/summary/";
    public const string CommonsApiUrl =
        "https://commons.wikimedia.org/w/api.php?action=query&format=json&formatversion=2&prop=imageinfo&iiprop=extmetadata"
        + "&iiextmetadatafilter=LicenseShortName|LicenseUrl|Artist&titles=File:";
    public const string CacheName = "wikipedia";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    /// <summary>Words of Wikidata's short description that mark an astronomical article.</summary>
    private static readonly string[] AstronomicalWords =
    [
        "galaxy", "galaxies", "nebula", "cluster", "star", "planet", "constellation", "supernova", "remnant",
        "asterism", "association", "nova", "quasar", "solar system", "h ii", "interstellar", "molecular cloud",
        "comet", "asteroid", "minor planet", "galactic"
    ];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Article titles to try, best first: the planet's article, "Messier N", the NGC/IC designation,
    /// "Caldwell N", then the common name. The REST API follows redirects (e.g. "Messier 42" → "Orion Nebula").
    /// </summary>
    public static IReadOnlyList<string> TitlesFor(CelestialObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var titles = new List<string>();
        if (target.Planet is { } planet)
        {
            titles.Add(planet == Planet.Mercury ? "Mercury (planet)" : planet.ToString());
            return titles;
        }

        if (target.Type == CelestialObjectType.MilkyWayCore)
        {
            titles.Add("Galactic Center");
            return titles;
        }

        if (target.Orbit is { } orbit)
        {
            // "4 Vesta"; "2P/Encke"; "C/2023 A3 (Tsuchinshan-ATLAS)"; then "Comet Encke".
            var name = orbit.Name;
            if (orbit.Kind == SmallBodyKind.Asteroid)
            {
                titles.Add(name.Length > 0 ? $"{orbit.Designation.Trim('(', ')')} {name}" : orbit.Designation.Trim('(', ')'));
            }
            else
            {
                titles.Add(name.Length == 0 ? orbit.Designation
                    : orbit.Designation.Contains('/', StringComparison.Ordinal) ? $"{orbit.Designation} ({name})"
                    : $"{orbit.Designation}/{name}");
                if (name.Length > 0)
                {
                    titles.Add($"Comet {name}");
                }
            }

            return [.. titles.Distinct(StringComparer.OrdinalIgnoreCase)];
        }

        if (target.Star is not null)
        {
            // "Castor (star)" before "Castor" (a disambiguation page); then "Alpha Geminorum" or "61 Cygni",
            // the spelled-out names (ASCII letters only), which redirect to the star's article.
            var proper = target.CommonName.Length > 0 && target.CommonName.All(c => c < 0x370 && !char.IsDigit(c))
                ? target.CommonName
                : null;
            if (proper is not null)
            {
                titles.Add($"{proper} (star)");
            }

            titles.AddRange(target.AlternateNames.Where(name => name.All(c => c < 0x80)));
            if (proper is not null)
            {
                titles.Add(proper);
            }

            return [.. titles.Distinct(StringComparer.OrdinalIgnoreCase).Take(4)];
        }

        if (target.MessierNumber is { } messier)
        {
            titles.Add(string.Create(CultureInfo.InvariantCulture, $"Messier {messier}"));
        }

        foreach (var id in target.AllIdentifiers)
        {
            if (CatalogDesignation().Match(id) is { Success: true } match)
            {
                titles.Add($"{match.Groups[1].Value.ToUpperInvariant()} {match.Groups[2].Value}");
            }
        }

        if (target.CaldwellNumber is { } caldwell)
        {
            titles.Add(string.Create(CultureInfo.InvariantCulture, $"Caldwell {caldwell}"));
        }

        if (target.CommonName.Length > 0)
        {
            titles.Add(target.CommonName);
        }

        return [.. titles.Distinct(StringComparer.OrdinalIgnoreCase).Take(4)];
    }

    public async Task<ObjectSummary?> GetSummaryAsync(CelestialObject target, CancellationToken cancellationToken = default)
    {
        foreach (var title in TitlesFor(target))
        {
            var url = SummaryUrl + Uri.EscapeDataString(title.Replace(' ', '_'));
            var fetched = await CachedFetch.GetAsync(http, cache, CacheName, url, CacheDuration, Now, "Wikipedia", Fail, cancellationToken,
                cacheNotFound: true);
            if (fetched.IsNotFound || ParseSummary(fetched.Content) is not { } summary)
            {
                continue;
            }

            return summary.Image is { } image ? summary with { Image = await LicensedAsync(image, cancellationToken) } : summary;
        }

        return null;
    }

    public async Task<byte[]> GetImageAsync(ObjectImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        var url = image.ThumbnailUrl.GetLeftPart(UriPartial.Path);
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, url, CacheDuration, Now, "Wikimedia Commons", Fail, cancellationToken);
        return fetched.Content;
    }

    /// <summary>
    /// The summary when the page is a standard article about an astronomical object; its image
    /// (Commons only) without license yet. Null for disambiguation pages and other topics.
    /// </summary>
    public static ObjectSummary? ParseSummary(byte[] json)
    {
        Summary? page;
        try
        {
            page = JsonSerializer.Deserialize<Summary>(json);
        }
        catch (JsonException ex)
        {
            throw new OnlineServiceException("Invalid response from Wikipedia.", ex);
        }

        if (page is not { Type: "standard", Title: { Length: > 0 } title, Extract: { Length: > 0 } extract }
            || page.ContentUrls?.Desktop?.Page is not { } pageUrl
            || !Uri.TryCreate(pageUrl, UriKind.Absolute, out var articleUrl)
            || !IsAstronomical(page.Description))
        {
            return null;
        }

        return new ObjectSummary(title, extract, page.Description, articleUrl)
        {
            Image = page.Thumbnail?.Source is { } source && CommonsFileName(source) is { } file
                ? new ObjectImage(new Uri(source), file, FilePage(file), License: "", LicenseUrl: null, Artist: null)
                : null
        };
    }

    /// <summary>The file name of a Commons upload or thumbnail URL ("…/commons/thumb/f/f3/Name.jpg/330px-Name.jpg" → "Name.jpg"), or null.</summary>
    public static string? CommonsFileName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var commons = Array.IndexOf(segments, "commons");
        if (commons < 0)
        {
            return null;
        }

        var index = commons + 1 < segments.Length && segments[commons + 1] == "thumb" ? commons + 4 : commons + 3;
        return index < segments.Length ? Uri.UnescapeDataString(segments[index]).Replace('_', ' ') : null;
    }

    /// <summary>License, license URL and author from Commons' extmetadata; null without a license name.</summary>
    public static (string License, Uri? LicenseUrl, string? Artist)? ParseLicense(byte[] json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("query", out var query)
                || !query.TryGetProperty("pages", out var pages) || pages.GetArrayLength() == 0
                || !pages[0].TryGetProperty("imageinfo", out var info) || info.GetArrayLength() == 0
                || !info[0].TryGetProperty("extmetadata", out var meta))
            {
                return null;
            }

            var license = Value(meta, "LicenseShortName");
            if (string.IsNullOrWhiteSpace(license))
            {
                return null;
            }

            var licenseUrl = Value(meta, "LicenseUrl") is { } u && Uri.TryCreate(u, UriKind.Absolute, out var parsed)
                && parsed.Scheme == Uri.UriSchemeHttps ? parsed : null;
            var artist = Value(meta, "Artist") is { } html ? PlainText(html) : null;
            return (PlainText(license), licenseUrl, string.IsNullOrWhiteSpace(artist) ? null : artist);
        }
        catch (JsonException ex)
        {
            throw new OnlineServiceException("Invalid response from Wikimedia Commons.", ex);
        }

        static string? Value(JsonElement meta, string name) =>
            meta.TryGetProperty(name, out var field) && field.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>HTML (Commons' Artist field) as plain text: tags removed, entities decoded, spaces collapsed.</summary>
    public static string PlainText(string html) =>
        Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();

    private static bool IsAstronomical(string? description) =>
        description is { Length: > 0 } text && AstronomicalWords.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static Uri FilePage(string file) =>
        new("https://commons.wikimedia.org/wiki/File:" + Uri.EscapeDataString(file.Replace(' ', '_')));

    private async Task<ObjectImage?> LicensedAsync(ObjectImage image, CancellationToken cancellationToken)
    {
        try
        {
            var url = CommonsApiUrl + Uri.EscapeDataString(image.FileName.Replace(' ', '_'));
            var fetched = await CachedFetch.GetAsync(http, cache, CacheName, url, CacheDuration, Now, "Wikimedia Commons", Fail, cancellationToken);
            return ParseLicense(fetched.Content) is { } license
                ? image with { License = license.License, LicenseUrl = license.LicenseUrl, Artist = license.Artist }
                : null;
        }
        catch (OnlineServiceException)
        {
            return null;   // without its license the image is not shown; the text still is
        }
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static OnlineServiceException Fail(string message, Exception? inner) => new(message, inner);

    [GeneratedRegex(@"^(NGC|IC)\s*(\d+[A-Za-z]?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogDesignation();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private sealed class Summary
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("extract")] public string? Extract { get; set; }
        [JsonPropertyName("thumbnail")] public Image? Thumbnail { get; set; }
        [JsonPropertyName("content_urls")] public ContentUrls? ContentUrls { get; set; }
    }

    private sealed class Image
    {
        [JsonPropertyName("source")] public string? Source { get; set; }
    }

    private sealed class ContentUrls
    {
        [JsonPropertyName("desktop")] public Urls? Desktop { get; set; }
    }

    private sealed class Urls
    {
        [JsonPropertyName("page")] public string? Page { get; set; }
    }
}
