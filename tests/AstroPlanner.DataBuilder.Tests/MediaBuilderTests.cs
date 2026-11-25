using System.Security.Cryptography;
using System.Text.Json;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.DataBuilder.Tests;

/// <summary>build-media on small synthetic images: panorama reprojection and cleaning, landscape masks, the manifest check.</summary>
public sealed class MediaBuilderTests : IDisposable
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Raster Filled(int width, int height, byte r, byte g, byte b)
    {
        var raster = new Raster(width, height);
        for (var i = 0; i < width * height; i++)
        {
            raster.Rgba[i * 4] = r;
            raster.Rgba[i * 4 + 1] = g;
            raster.Rgba[i * 4 + 2] = b;
            raster.Rgba[i * 4 + 3] = 255;
        }

        return raster;
    }

    private static void Paint(Raster raster, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var o = raster.Offset(x, y);
                (raster.Rgba[o], raster.Rgba[o + 1], raster.Rgba[o + 2]) = (r, g, b);
            }
        }
    }

    [Fact]
    public void GalacticPixel_PutsTheCentreInTheMiddle_AndLongitudeGrowingToTheLeft()
    {
        var source = new Raster(360, 180);
        PanoramaBuilder.GalacticPixel(source, 0, 0).Should().Be((180.0, 90.0));
        PanoramaBuilder.GalacticPixel(source, 90, 0).Should().Be((90.0, 90.0));
        PanoramaBuilder.GalacticPixel(source, 270, 45).Should().Be((270.0, 45.0));
    }

    [Fact]
    public void ToEquatorial_PutsTheGalacticCentreAtItsRaAndDec()
    {
        // A red spot at l = 0, b = 0 of a galactic image must land at RA 266.4°, Dec −28.9°.
        var galactic = Filled(720, 360, 0, 0, 0);
        Paint(galactic, 356, 176, 364, 184, 255, 0, 0);

        var equatorial = PanoramaBuilder.ToEquatorial(galactic, 720);

        var x = (int)(266.405 / 360 * 720);
        var y = (int)((90 + 28.936) / 180 * 360);
        equatorial.Rgba[equatorial.Offset(x, y)].Should().BeGreaterThan(200);
        equatorial.Rgba[equatorial.Offset((x + 360) % 720, y)].Should().Be(0);
    }

    [Fact]
    public void SuppressStars_ClipsPointsButKeepsExtendedLight()
    {
        var image = Filled(200, 100, 40, 40, 40);
        Paint(image, 20, 20, 22, 22, 250, 250, 250);    // a star (2 px)
        Paint(image, 100, 30, 160, 80, 150, 140, 130);  // a cloud of the Milky Way

        var clean = PanoramaBuilder.SuppressStars(image, PanoramaBuilder.StarRadius, PanoramaBuilder.StarThreshold);

        clean.Rgba[clean.Offset(20, 20)].Should().Be(40 + PanoramaBuilder.StarThreshold);
        clean.Rgba[clean.Offset(130, 55)].Should().Be(150);
        clean.Rgba[clean.Offset(130, 55) + 2].Should().Be(130);
    }

    [Fact]
    public void SetBlackPoint_MakesTheBackgroundBlack()
    {
        var image = Filled(100, 100, 30, 35, 40);
        Paint(image, 0, 0, 10, 10, 200, 200, 200);

        PanoramaBuilder.SetBlackPoint(image, 0.05);

        image.Rgba[image.Offset(50, 50)].Should().Be(0);
        image.Rgba[image.Offset(50, 50) + 2].Should().Be(0);
        image.Rgba[image.Offset(5, 5)].Should().BeInRange(190, 255);
    }

    /// <summary>A 360×180 photo (1 px per degree): blue sky, brown ground up to 10°, a tower to 30° at azimuth 100°–102°.</summary>
    private static Raster Photo()
    {
        var photo = Filled(360, 180, 90, 130, 210);
        Paint(photo, 0, 80, 360, 180, 120, 90, 60);
        Paint(photo, 100, 60, 103, 80, 70, 70, 70);
        Paint(photo, 200, 20, 205, 25, 255, 255, 255);  // the Sun, floating in the sky
        Paint(photo, 300, 83, 303, 86, 90, 130, 210);   // sky seen through the ground (5° up)
        return photo;
    }

    private static int Alpha(LandscapeBuilder.Result result, int azimuth, double altitude)
    {
        var row = (int)((result.TopAltitudeDegrees - altitude) / 180 * 180);
        return result.Image.Rgba[result.Image.Offset(azimuth, row) + 3];
    }

    [Fact]
    public void Landscape_FindsTheHorizon_DropsTheSun_AndCutsHolesOnRequest()
    {
        var result = LandscapeBuilder.Build(Photo(), new LandscapeSettings(0, 12, CutHoles: true));

        result.Horizon.Should().HaveCount(360);
        result.Horizon[50].Should().Be(10);
        result.Horizon[101].Should().Be(30);
        result.TopAltitudeDegrees.Should().Be(31);
        result.Image.Height.Should().Be(121); // 31° down to −90°
        Alpha(result, 50, 5).Should().Be(255);
        Alpha(result, 50, 15).Should().Be(0);
        Alpha(result, 301, 4.5).Should().Be(0);

        var kept = LandscapeBuilder.Build(Photo(), new LandscapeSettings(0, 12, CutHoles: false));
        Alpha(kept, 301, 4.5).Should().Be(255);
        kept.Horizon[202].Should().Be(10); // the Sun is not ground
    }

    [Fact]
    public void Landscape_TurnsByTheAzimuthOffset_AndHidesTheSkyColourUnderTheTransparentPart()
    {
        var result = LandscapeBuilder.Build(Photo(), new LandscapeSettings(90, 12, CutHoles: true));

        result.Horizon[191].Should().Be(30);
        result.Horizon[101].Should().Be(10);
        var sky = result.Image.Offset(50, 0);
        result.Image.Rgba[sky + 3].Should().Be(0);
        result.Image.Rgba[sky + 2].Should().BeLessThan(130, "transparent pixels carry the ground's colour, not the photo's sky");
    }

    [Fact]
    public void Downsample_WeighsColoursByAlpha()
    {
        var image = Filled(4, 2, 200, 100, 50);
        var sky = image.Offset(1, 0);
        (image.Rgba[sky], image.Rgba[sky + 1], image.Rgba[sky + 2], image.Rgba[sky + 3]) = (0, 0, 255, 0);
        for (var x = 2; x < 4; x++)
        {
            for (var y = 0; y < 2; y++)
            {
                image.Rgba[image.Offset(x, y) + 3] = 0;
            }
        }

        var small = LandscapeBuilder.Downsample(image, 2);

        small.Width.Should().Be(2);
        small.Height.Should().Be(1);
        small.Rgba[..4].Should().Equal(200, 100, 50, 191);   // the blue sky pixel adds no colour
        small.Rgba[4..].Should().Equal(200, 100, 50, 0);     // all transparent: the mean colour
    }

    private string WriteManifest(Func<string, string>? tamper = null)
    {
        Directory.CreateDirectory(Path.Combine(_directory, "panorama"));
        Directory.CreateDirectory(Path.Combine(_directory, "landscapes"));
        File.WriteAllBytes(Path.Combine(_directory, "panorama", "p.webp"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(_directory, "landscapes", "l.webp"), [4, 5, 6]);
        static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        var credit = new ManifestCredit("a", "CC0 1.0", "https://example.org", "none");
        var landscapes = Enumerable.Range(0, 3).Select(i => new ManifestLandscape(
            $"l{i}", "L", "landscapes/l.webp", 8, 4, 12, Enumerable.Repeat(5.0, 360).ToList(), Sha([4, 5, 6]), credit)).ToList();
        var manifest = new MediaManifest(MediaBuilder.ManifestVersion,
            new ManifestPanorama("panorama/p.webp", 8, 4, Sha([1, 2, 3]), credit), landscapes);
        var json = JsonSerializer.Serialize(manifest, CamelCase);
        File.WriteAllText(Path.Combine(_directory, MediaBuilder.ManifestFileName), tamper?.Invoke(json) ?? json);
        return _directory;
    }

    [Fact]
    public void Verify_AcceptsAConsistentManifest_AndReportsProblems()
    {
        MediaBuilder.Verify(WriteManifest()).Should().BeEmpty();

        MediaBuilder.Verify(Path.Combine(_directory, "none")).Should().ContainSingle().Which.Should().Contain("missing");

        File.WriteAllBytes(Path.Combine(_directory, "panorama", "p.webp"), [9, 9]);
        MediaBuilder.Verify(_directory).Should().ContainSingle().Which.Should().Contain("SHA-256");

        WriteManifest(json => json.Replace("\"topAltitudeDegrees\":12", "\"topAltitudeDegrees\":2", StringComparison.Ordinal));
        MediaBuilder.Verify(_directory).Should().Contain(f => f.Contains("above its image"));
    }

    [Fact]
    public void Curation_ListsEveryLandscapeWithAPinnedSource()
    {
        var curated = MediaBuilder.LoadCuration(Path.Combine(AppContext.BaseDirectory, "fixtures", "curated", "landscapes.json"));

        curated.Should().HaveCountGreaterThanOrEqualTo(3);
        curated.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        curated.Should().OnlyContain(l => DataSources.Landscapes.Any(s => s.RelativePath == l.Source));
        GalacticCoordinates.FromJ2000.Determinant().Should().BeApproximately(1, 1e-9);
    }
}
