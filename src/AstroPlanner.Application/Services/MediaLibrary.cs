using System.Text.Json;
using AstroPlanner.Application.Sky;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Application.Services;

/// <summary>Who made a sky image, its license, its page and what was changed (CC BY asks for all four).</summary>
public sealed record MediaCredit(string Author, string License, string Url, string Changes);

/// <summary>A landscape of the media folder: its name, credit and the horizon its ground draws.</summary>
/// <param name="Horizon">The ground's altitude for each degree of azimuth (index = azimuth).</param>
public sealed record LandscapeInfo(string Id, string Name, MediaCredit Credit, IReadOnlyList<double> Horizon)
{
    /// <summary>Degrees of azimuth per point of <see cref="ToHorizonMask"/>.</summary>
    public const int MaskStepDegrees = 2;

    /// <summary>
    /// The landscape's horizon as a site horizon mask (Locations, feasibility): one point every
    /// <see cref="MaskStepDegrees"/>°, the highest ground of those degrees (never below 0°).
    /// </summary>
    public HorizonMask ToHorizonMask() => new(
        Enumerable.Range(0, 360 / MaskStepDegrees).Select(i =>
        {
            var highest = Enumerable.Range(i * MaskStepDegrees, MaskStepDegrees).Max(d => Horizon[d]);
            return new HorizonPoint(i * MaskStepDegrees + MaskStepDegrees / 2.0, Math.Round(Math.Clamp(highest, 0, 90), 1));
        }));
}

/// <summary>
/// The sky's images next to the exe (<c>data/</c>, built by the DataBuilder's build-media, R2c):
/// the Milky Way panorama and the landscapes, described by <c>manifest.json</c>. WebView2 serves
/// the folder as https://media.astroplanner/; without it the app works as before (outline Milky
/// Way, horizon profile).
/// </summary>
public sealed class MediaLibrary
{
    public const string ManifestFileName = "manifest.json";
    public const int ManifestVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private MediaLibrary(MediaCredit? panorama, IReadOnlyList<LandscapeInfo> landscapes)
    {
        PanoramaCredit = panorama;
        Landscapes = landscapes;
    }

    /// <summary>No media folder.</summary>
    public static MediaLibrary None { get; } = new(null, []);

    public bool IsAvailable => PanoramaCredit is not null;

    /// <summary>Where the renderer finds the images (<c>init.mediaBaseUrl</c>), or null.</summary>
    public string? BaseUrl => IsAvailable ? $"https://{SkyHosts.MediaHost}/" : null;

    public MediaCredit? PanoramaCredit { get; }

    public IReadOnlyList<LandscapeInfo> Landscapes { get; }

    public LandscapeInfo? Find(string? id) => id is null ? null : Landscapes.FirstOrDefault(l => l.Id == id);

    /// <summary>
    /// What the sky shows for a site's <see cref="Location.LandscapeId"/>: the id when this folder
    /// has it, <see cref="Core.Models.Landscapes.Flat"/>, or null (the horizon profile) otherwise.
    /// </summary>
    public string? Resolve(string? landscapeId) =>
        landscapeId == Core.Models.Landscapes.Flat ? landscapeId : Find(landscapeId)?.Id;

    /// <summary>Reads <paramref name="directory"/>/manifest.json; <see cref="None"/> when it is missing or unreadable.</summary>
    public static MediaLibrary Load(string directory, IAppLogger? logger = null)
    {
        var path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path))
        {
            return None;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            logger?.LogError("The sky's media folder could not be read; the sky shows no panorama or landscapes.", ex);
            return None;
        }
    }

    /// <summary>Parses a manifest (the DataBuilder's MediaManifest), keeping only well-formed landscapes.</summary>
    public static MediaLibrary Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(json, Json);
        if (manifest?.Version != ManifestVersion || manifest.Panorama?.Credit is not { } panorama)
        {
            throw new InvalidDataException($"The media manifest is not version {ManifestVersion}.");
        }

        var landscapes = (manifest.Landscapes ?? [])
            .Where(l => l.Id is not null && Core.Models.Landscapes.IsValidId(l.Id) && l.Id != Core.Models.Landscapes.Flat
                        && !string.IsNullOrWhiteSpace(l.Name) && l.Credit is not null
                        && l.Horizon is { Count: 360 } horizon && horizon.All(h => double.IsFinite(h) && h is >= -10 and <= 90))
            .Select(l => new LandscapeInfo(l.Id!, l.Name!, l.Credit!, l.Horizon!))
            .ToList();
        return new MediaLibrary(panorama, landscapes);
    }

    private sealed record Manifest(int Version, ManifestPanorama? Panorama, IReadOnlyList<ManifestLandscape>? Landscapes);

    private sealed record ManifestPanorama(MediaCredit? Credit);

    private sealed record ManifestLandscape(string? Id, string? Name, IReadOnlyList<double>? Horizon, MediaCredit? Credit);
}
