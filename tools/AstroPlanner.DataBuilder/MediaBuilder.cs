using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// build-media (R2c, spec §8.3/§8.5): the Milky Way panorama and the landscapes, written to
/// data/build/media with a manifest.json the app and the renderer read (files, sizes, SHA-256,
/// horizon profiles, credits). The folder is shipped next to the exe as <c>data/</c>.
/// </summary>
internal static class MediaBuilder
{
    public const string ManifestFileName = "manifest.json";
    public const int ManifestVersion = 1;
    public const int PanoramaWidth = 4096;

    /// <summary>
    /// Width of the landscapes (11 px per degree; the photos are 8192): an 8192 px landscape costs
    /// ~280 MB more in WebView2's GPU process, past the app's 1.2 GB budget (spec §11).
    /// </summary>
    public const int LandscapeWidth = 4096;
    public const int PanoramaQuality = 85;
    public const int LandscapeQuality = 80;
    public const long PanoramaBudgetBytes = 15L * 1024 * 1024;
    public const long LandscapeBudgetBytes = 6L * 1024 * 1024;

    public const string PanoramaFile = "panorama/milky-way.webp";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>One landscape of data/curated/landscapes.json.</summary>
    internal sealed record CuratedLandscape(
        string Id, string Name, string Source, string Author, string Url, double AzimuthOffsetDegrees, int SkyBlueMinimum, bool CutHoles);

    private sealed record CuratedFile(IReadOnlyList<CuratedLandscape> Landscapes);

    public static IReadOnlyList<CuratedLandscape> LoadCuration(string path) =>
        JsonSerializer.Deserialize<CuratedFile>(File.ReadAllText(path), Json)?.Landscapes
        ?? throw new InvalidDataException($"{path}: no landscapes.");

    public static string Build(Func<string, string> raw, string curatedPath, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var report = new StringBuilder();
        report.AppendLine("# media build report");

        var panoramaSource = DataSources.MilkyWayPanorama;
        var galactic = ImageFiles.Load(raw(panoramaSource.RelativePath));
        var panorama = PanoramaBuilder.Build(galactic, PanoramaWidth);
        var panoramaPath = Path.Combine(outputDirectory, PanoramaFile);
        ImageFiles.SaveWebp(panorama, panoramaPath, PanoramaQuality);
        report.AppendLine(CultureInfo.InvariantCulture,
            $"panorama: {panorama.Width}×{panorama.Height}, {new FileInfo(panoramaPath).Length / 1024} KB");

        var landscapes = new List<ManifestLandscape>();
        foreach (var curated in LoadCuration(curatedPath))
        {
            var source = DataSources.Landscapes.Single(s => s.RelativePath == curated.Source);
            var photo = ImageFiles.Load(raw(source.RelativePath));
            var result = LandscapeBuilder.Build(photo, new LandscapeSettings(curated.AzimuthOffsetDegrees, curated.SkyBlueMinimum, curated.CutHoles));
            var image = LandscapeBuilder.Downsample(result.Image, Math.Max(1, result.Image.Width / LandscapeWidth));
            var file = $"landscapes/{curated.Id}.webp";
            var path = Path.Combine(outputDirectory, file);
            ImageFiles.SaveWebp(image, path, LandscapeQuality);
            landscapes.Add(new ManifestLandscape(
                curated.Id, curated.Name, file, image.Width, image.Height, result.TopAltitudeDegrees,
                result.Horizon, Sha256(path),
                new ManifestCredit($"{curated.Author}, Poly Haven", source.License, curated.Url,
                    "Sky made transparent; horizon profile derived from the outline.")));
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{curated.Id}: {image.Width}×{image.Height}, top {result.TopAltitudeDegrees:0.0}°, horizon {result.Horizon.Min():0.0}°–{result.Horizon.Max():0.0}°, {new FileInfo(path).Length / 1024} KB");
        }

        var manifest = new MediaManifest(
            ManifestVersion,
            new ManifestPanorama(PanoramaFile, panorama.Width, panorama.Height, Sha256(panoramaPath),
                new ManifestCredit("ESO/S. Brunier", panoramaSource.License, "https://www.eso.org/public/images/eso0932a/",
                    "Stars removed, background set to black, reprojected from galactic to J2000 equatorial coordinates.")),
            landscapes);
        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName), JsonSerializer.Serialize(manifest, Json) + "\n", new UTF8Encoding(false));
        return report.ToString();
    }

    /// <summary>The manifest exists, every file is there with its SHA-256, within budget, and the profiles are sane.</summary>
    public static IReadOnlyList<string> Verify(string directory)
    {
        var failures = new List<string>();
        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return [$"media: {ManifestFileName} missing (run build-media)"];
        }

        MediaManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<MediaManifest>(File.ReadAllText(manifestPath), Json);
        }
        catch (JsonException ex)
        {
            return [$"media: {ManifestFileName} is not valid: {ex.Message}"];
        }

        if (manifest is null || manifest.Version != ManifestVersion)
        {
            return [$"media: {ManifestFileName} has no version {ManifestVersion}"];
        }

        CheckFile(manifest.Panorama.File, manifest.Panorama.Sha256, PanoramaBudgetBytes);
        if (manifest.Panorama.Width != 2 * manifest.Panorama.Height)
        {
            failures.Add("media: the panorama is not 2:1");
        }

        if (manifest.Landscapes.Count < 3)
        {
            failures.Add($"media: {manifest.Landscapes.Count} landscapes, expected at least 3");
        }

        foreach (var landscape in manifest.Landscapes)
        {
            CheckFile(landscape.File, landscape.Sha256, LandscapeBudgetBytes);
            if (landscape.Horizon.Count != 360 || landscape.Horizon.Any(h => h is < -10 or > 90))
            {
                failures.Add($"media: {landscape.Id} needs 360 horizon altitudes between -10° and 90°");
            }
            else if (landscape.Horizon.Max() > landscape.TopAltitudeDegrees)
            {
                failures.Add($"media: {landscape.Id} has ground above its image");
            }
        }

        return failures;

        void CheckFile(string file, string sha256, long budget)
        {
            var path = Path.Combine(directory, file);
            if (!File.Exists(path))
            {
                failures.Add($"media: {file} missing");
                return;
            }

            if (Sha256(path) != sha256)
            {
                failures.Add($"media: {file} does not match its SHA-256 in the manifest");
            }

            if (new FileInfo(path).Length > budget)
            {
                failures.Add($"media: {file} is over its {budget / 1024 / 1024} MB budget");
            }
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

// The manifest's shape (camelCase JSON). The app reads it in Application/Services/MediaLibrary
// and the renderer in sky-engine/src/render/media.ts: keep the three in step.

/// <summary>Who made an image, its license, where it comes from and what was changed.</summary>
internal sealed record ManifestCredit(string Author, string License, string Url, string Changes);

/// <summary>J2000 equirectangular: u = RA / 360° from the left edge, v = (90° − Dec) / 180°.</summary>
internal sealed record ManifestPanorama(string File, int Width, int Height, string Sha256, ManifestCredit Credit);

/// <summary>
/// Columns = azimuth 0°–360° from the left edge (north, then east); rows from
/// <see cref="TopAltitudeDegrees"/> down to −90°; transparent above the ground.
/// <see cref="Horizon"/>: the ground's altitude for each degree of azimuth.
/// </summary>
internal sealed record ManifestLandscape(
    string Id, string Name, string File, int Width, int Height, double TopAltitudeDegrees,
    IReadOnlyList<double> Horizon, string Sha256, ManifestCredit Credit);

internal sealed record MediaManifest(int Version, ManifestPanorama Panorama, IReadOnlyList<ManifestLandscape> Landscapes);
