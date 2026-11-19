using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstroPlanner.UI.Services;

/// <summary>A published version newer than the installed one.</summary>
public sealed record UpdateInfo(AppVersion Version, string Name, string Url);

/// <summary>Checks whether a newer version has been published.</summary>
public interface IUpdateChecker
{
    /// <summary>
    /// The highest published version if it is newer than <paramref name="current"/>; null if
    /// there is none or it could not be checked (never throws for network errors).
    /// </summary>
    Task<UpdateInfo?> CheckAsync(AppVersion current, CancellationToken cancellationToken = default);
}

/// <summary>
/// Queries the GitHub releases (drafts are ignored). It only works while the repository is
/// public: for a private one the API answers 404 and simply nothing is announced.
/// </summary>
public sealed class GitHubUpdateChecker(HttpClient http, IAppLogger logger) : IUpdateChecker
{
    public const string ReleasesUrl = "https://api.github.com/repos/Yamato6/AstroPlanner-Pro/releases?per_page=20";

    public async Task<UpdateInfo?> CheckAsync(AppVersion current, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInfo($"Update check: GitHub answered {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, cancellationToken: cancellationToken);
            return Newest(releases ?? [], current);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogInfo($"Update check unavailable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The highest release above <paramref name="current"/>. Pre-releases (a suffix such as
    /// "-rc.1", or flagged as such on GitHub) are only offered to users of a 0.x or a
    /// pre-release: users of a stable version only see stable versions.
    /// </summary>
    internal static UpdateInfo? Newest(IEnumerable<GitHubRelease> releases, AppVersion current)
    {
        var acceptsPreReleases = current.IsPreRelease || current.Number.Major == 0;
        return releases
            .Where(r => !r.Draft && !string.IsNullOrWhiteSpace(r.HtmlUrl))
            .Select(r => (Release: r, Version: ParseTag(r.TagName)))
            .Where(r => r.Version is not null
                        && r.Version > current
                        && (acceptsPreReleases || !(r.Version.IsPreRelease || r.Release.PreRelease)))
            .OrderByDescending(r => r.Version)
            .Select(r => new UpdateInfo(r.Version!, string.IsNullOrWhiteSpace(r.Release.Name) ? r.Release.TagName! : r.Release.Name!, r.Release.HtmlUrl!))
            .FirstOrDefault();
    }

    /// <summary>"v0.8.0" → 0.8.0, "v1.0.0-rc.1" → 1.0.0-rc.1; null if the tag is not a version (e.g. "PreRelease").</summary>
    internal static AppVersion? ParseTag(string? tag) => AppVersion.TryParse(tag);

    internal sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
    }
}
