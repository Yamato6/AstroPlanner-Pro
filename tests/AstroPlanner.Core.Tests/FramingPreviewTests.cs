using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>Framing preview geometry and composition from synthetic HiPS tiles.</summary>
public sealed class FramingPreviewTests
{
    private static readonly HipsSurvey Dss = new("DSSColor", "DSS2 colour", 9);

    /// <summary>Each tile filled with a colour derived from its key, so a pixel tells which tile it came from.</summary>
    private static Dictionary<HipsTileKey, RgbImage> SolidTiles(FramingPlan plan) =>
        plan.Tiles.ToDictionary(k => k, k => Solid(plan.TileWidth, Colour(k.Index)));

    private static (byte, byte, byte) Colour(long index) => ((byte)(index % 251), (byte)(index / 251 % 251), 200);

    private static RgbImage Solid(int width, (byte R, byte G, byte B) colour)
    {
        var image = new RgbImage(width, width);
        for (var i = 0; i < image.Pixels.Length; i += 3)
        {
            (image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2]) = colour;
        }

        return image;
    }

    [Fact]
    public void Centre_AndNorthEast_AreWhereTheSkyPutsThem()
    {
        var plan = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 2.0, 1.33), Dss);

        plan.Width.Should().Be(640);
        plan.Height.Should().BeInRange(424, 428);
        var centre = plan.ToPixel(83.82, -5.39)!.Value;
        centre.X.Should().BeApproximately(319.5, 1e-9);
        centre.Y.Should().BeApproximately(plan.Height / 2.0 - 0.5, 1e-9);

        var north = plan.ToPixel(83.82, -5.0)!.Value;
        north.Y.Should().BeLessThan(centre.Y, "north is up");
        north.X.Should().BeApproximately(centre.X, 0.5);
        plan.ToPixel(84.2, -5.39)!.Value.X.Should().BeLessThan(centre.X, "east is left, as on the sky");
        plan.PixelsFor(120).Should().BeApproximately(640, 2, "the field is 2° wide");
    }

    [Fact]
    public void Rotation_TurnsNorthToTheRight_At90Degrees()
    {
        var plan = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 2.0, 1.33, RotationDegrees: 90), Dss);
        var centre = plan.ToPixel(83.82, -5.39)!.Value;
        var north = plan.ToPixel(83.82, -5.0)!.Value;

        north.X.Should().BeGreaterThan(centre.X + 100);
        north.Y.Should().BeApproximately(centre.Y, 1);
        plan.ScreenAngleDegrees(0).Should().Be(90, "a north-south axis is drawn horizontally");
        FramingPreview.Plan(new FramingRequest(10, 41, 2, 1.33), Dss).ScreenAngleDegrees(35).Should().Be(325);
    }

    [Fact]
    public void CentrePixel_ComesFromTheTileThatContainsTheTarget()
    {
        var plan = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 2.0, 1.33), Dss);
        var image = plan.Compose(SolidTiles(plan), out var coverage);

        coverage.Should().Be(1);
        plan.UsesAllsky.Should().BeFalse();
        var expected = Healpix.ToNested(plan.Order, 83.82, -5.39);
        image[320, plan.Height / 2].Should().Be(Colour(expected));
        plan.Tiles.Should().Contain(new HipsTileKey(plan.Order, expected));
    }

    [Fact]
    public void Order_IsTheSmallestFineEnough_CappedBySurveyAndTileCount()
    {
        // APS-C on 650 mm: ~2.1° × 1.4° → ~12″ per output pixel.
        var telescope = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 2.08, 1.39), Dss);
        Healpix.PixelSizeDegrees(telescope.Order + 9).Should().BeLessThanOrEqualTo(telescope.PixelDegrees * FramingPreview.AcceptedUpsampling);
        Healpix.PixelSizeDegrees(telescope.Order + 8).Should().BeGreaterThan(telescope.PixelDegrees * FramingPreview.AcceptedUpsampling);
        telescope.Tiles.Count.Should().BeInRange(1, FramingPreview.MaximumTiles);

        // A tiny field cannot go deeper than the survey.
        FramingPreview.Plan(new FramingRequest(83.82, -5.39, 0.05, 0.05), Dss).Order.Should().Be(9);

        // A 16 mm lens (~80° × 55°) uses the single Allsky image.
        var wide = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 79.6, 56.1), Dss);
        wide.UsesAllsky.Should().BeTrue();
        wide.Tiles.Should().Equal(HipsTileKey.Allsky);
        wide.TileWidth.Should().Be(64);
    }

    [Fact]
    public void ManyTiles_FallBackToACoarserOrder()
    {
        var plan = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 12, 8, OutputWidth: 1600), Dss);

        plan.Tiles.Count.Should().BeLessThanOrEqualTo(FramingPreview.MaximumTiles);
        plan.Compose(SolidTiles(plan), out var coverage);
        coverage.Should().Be(1);
    }

    [Theory]
    [InlineData(0.05, 10.0)]    // across RA 0/360
    [InlineData(359.95, -10.0)]
    [InlineData(123.0, 89.9)]   // around the north pole
    [InlineData(300.0, -89.95)] // and the south pole
    public void SeamsAndPoles_AreCoveredWithoutGaps(double ra, double dec)
    {
        var plan = FramingPreview.Plan(new FramingRequest(ra, dec, 1.0, 0.7), Dss);
        plan.Compose(SolidTiles(plan), out var coverage);

        coverage.Should().Be(1);
        plan.Tiles.Count.Should().BeGreaterThan(1);
        plan.ToPixel(ra, dec)!.Value.X.Should().BeApproximately(plan.Width / 2.0 - 0.5, 1e-6);
    }

    [Fact]
    public void InsideATile_RowsFollowFaceXAndColumnsFaceY()
    {
        // One bright pixel where a star falls, by the HiPS layout; the preview must show it at the star.
        const double starRa = 56.8711, starDec = 24.1051;   // Alcyone
        var plan = FramingPreview.Plan(new FramingRequest(56.80, 24.15, 0.4, 0.3), Dss);
        var fine = Healpix.ToFacePoint(plan.Order + 9, starRa, starDec);
        var tiles = plan.Tiles.ToDictionary(k => k, _ => new RgbImage(512, 512));
        var key = new HipsTileKey(plan.Order, Healpix.ToNested(plan.Order, starRa, starDec));
        var row = (int)Math.Floor(fine.X) % 512;
        var column = (int)Math.Floor(fine.Y) % 512;
        for (var r = row - 1; r <= row + 1; r++)
        {
            for (var c = column - 1; c <= column + 1; c++)
            {
                var i = ((r * 512) + c) * 3;
                tiles[key].Pixels[i] = tiles[key].Pixels[i + 1] = tiles[key].Pixels[i + 2] = 255;
            }
        }

        var image = plan.Compose(tiles, out _);
        var (x, y) = plan.ToPixel(starRa, starDec)!.Value;

        image[(int)Math.Round(x), (int)Math.Round(y)].R.Should().BeGreaterThan(150);
        image[(int)Math.Round(x) + 20, (int)Math.Round(y)].R.Should().Be(0);
    }

    [Fact]
    public void Allsky_SamplesTheReducedTileOfTheTarget()
    {
        var plan = FramingPreview.Plan(new FramingRequest(83.82, -5.39, 80, 56), Dss);
        var mosaic = new RgbImage(FramingPreview.AllskyColumns * 64, 29 * 64);
        for (var n = 0; n < 768; n++)
        {
            var (r0, c0) = (n / 27 * 64, n % 27 * 64);
            var colour = Colour(n);
            for (var r = r0; r < r0 + 64; r++)
            {
                for (var c = c0; c < c0 + 64; c++)
                {
                    var i = ((r * mosaic.Width) + c) * 3;
                    (mosaic.Pixels[i], mosaic.Pixels[i + 1], mosaic.Pixels[i + 2]) = colour;
                }
            }
        }

        var image = plan.Compose(new Dictionary<HipsTileKey, RgbImage> { [HipsTileKey.Allsky] = mosaic }, out var coverage);

        coverage.Should().Be(1);
        image[320, plan.Height / 2].Should().Be(Colour(Healpix.ToNested(3, 83.82, -5.39)), "the mini tile is never mixed with its neighbour in the mosaic");
    }

    [Fact]
    public void MissingTiles_StayBlack_AndLowerTheCoverage()
    {
        var plan = FramingPreview.Plan(new FramingRequest(0.05, 10, 1.0, 0.7), Dss);
        var some = SolidTiles(plan);
        some.Remove(plan.Tiles[0]);

        plan.Compose(some, out var coverage);
        coverage.Should().BeGreaterThan(0).And.BeLessThan(1);
        plan.Compose(new Dictionary<HipsTileKey, RgbImage>(), out var none).Pixels.Should().OnlyContain(b => b == 0);
        none.Should().Be(0);
    }

    [Fact]
    public void InvalidInput_IsRejected()
    {
        FluentActions.Invoking(() => FramingPreview.Plan(new FramingRequest(0, 0, 0, 1), Dss)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => FramingPreview.Plan(new FramingRequest(0, 0, 1, 1), Dss with { TileWidth = 500 })).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new RgbImage(2, 2, new byte[5])).Should().Throw<ArgumentException>();
    }
}
