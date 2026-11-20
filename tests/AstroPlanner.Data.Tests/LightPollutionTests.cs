using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Data.LightPollution;

namespace AstroPlanner.Data.Tests;

public sealed class LightPollutionTests
{
    private static LightPollutionGrid Grid(double north, double west, double cell, double[,] values)
    {
        var rows = values.GetLength(0);
        var columns = values.GetLength(1);
        var cells = new byte[rows * columns];
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                cells[r * columns + c] = LightPollutionGrid.Encode(values[r, c]);
            }
        }

        return new LightPollutionGrid(rows, columns, north, west, cell, cells);
    }

    [Theory]
    [InlineData(0.0002)]
    [InlineData(0.171)]
    [InlineData(1.84)]
    [InlineData(250)]
    public void Encode_RoundTripsWithinHalfAStep(double brightness)
    {
        var decoded = LightPollutionGrid.Decode(LightPollutionGrid.Encode(brightness));

        Math.Abs(Math.Log10(decoded) - Math.Log10(brightness)).Should().BeLessThanOrEqualTo(LightPollutionGrid.Log10Step / 2 + 1e-12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3.4e38)]
    [InlineData(double.NaN)]
    [InlineData(0.00005)]
    public void Encode_MapsDarkAndNoDataToZero(double brightness)
    {
        LightPollutionGrid.Encode(brightness).Should().Be(0);
        LightPollutionGrid.Decode(0).Should().Be(0);
    }

    [Fact]
    public void Encode_ClampsVeryBrightCells()
    {
        LightPollutionGrid.Encode(1e6).Should().Be(255);
    }

    [Fact]
    public void Sample_InterpolatesBetweenCellCentres_AndReturnsNullOutside()
    {
        var grid = Grid(10, 0, 1, new double[,] { { 1, 3 }, { 1, 3 } });

        grid.Sample(9.5, 0.5)!.Value.Should().BeApproximately(1, 0.05);
        grid.Sample(9.5, 1.0)!.Value.Should().BeApproximately(2, 0.1);   // halfway between centres
        grid.Sample(9.5, 1.9)!.Value.Should().BeApproximately(3, 0.15);   // edge cells are extended
        grid.Sample(11, 1).Should().BeNull();
        grid.Sample(9, -0.1).Should().BeNull();
    }

    [Fact]
    public void WriteAndRead_RoundTrip()
    {
        var grid = Grid(33, -118, 1.0 / 120, new double[,] { { 0, 0.5, 2 }, { 0.01, 10, 0.3 } });
        using var stream = new MemoryStream();

        grid.Write(stream);
        stream.Position = 0;
        var read = LightPollutionGrid.Read(stream);

        read.Rows.Should().Be(2);
        read.Columns.Should().Be(3);
        read.NorthDegrees.Should().Be(33);
        read.WestDegrees.Should().Be(-118);
        read.CellDegrees.Should().Be(1.0 / 120);
        read.CellValue(1, 1).Should().Be(grid.CellValue(1, 1));
    }

    [Fact]
    public void Read_RejectsOtherFiles()
    {
        using var stream = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
        {
            gzip.Write(new byte[64]);
        }

        stream.Position = 0;
        var act = () => LightPollutionGrid.Read(stream);

        act.Should().Throw<InvalidDataException>().WithMessage("*magic*");
    }

    [Fact]
    public void Atlas_PrefersTheFinerGrid_ThenFallsBack_AndWrapsLongitude()
    {
        var fine = Grid(22, -103, 1, new double[,] { { 5 } });
        var coarse = Grid(80, -180, 90, new double[,] { { 0.5, 0.5, 0.5, 0.5 } });
        var atlas = new LightPollutionAtlas([fine, coarse]);

        atlas.ArtificialBrightness(21.5, -102.5)!.Value.Should().BeApproximately(5, 0.2);
        atlas.ArtificialBrightness(40, 10)!.Value.Should().BeApproximately(0.5, 0.02);
        atlas.ArtificialBrightness(40, 370)!.Value.Should().BeApproximately(0.5, 0.02);
        atlas.ArtificialBrightness(-50, 0).Should().BeNull();
        atlas.ArtificialBrightness(double.NaN, 0).Should().BeNull();
        new LightPollutionAtlas([]).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void EmbeddedAtlas_GivesAnUrbanSkyInAguascalientes_AndADarkOneInTheSierra()
    {
        var atlas = LightPollutionAtlas.FromEmbedded();

        atlas.IsAvailable.Should().BeTrue();
        var city = atlas.ArtificialBrightness(21.88, -102.29)!.Value;
        SkyBrightness.BortleFromSqm(SkyBrightness.SqmFromArtificial(city)).Should().BeInRange(6, 9);
        var sierra = atlas.ArtificialBrightness(31.04, -115.46)!.Value;
        SkyBrightness.SqmFromArtificial(sierra).Should().BeGreaterThan(21.8);
        // Outside Mexico the world grid answers.
        atlas.ArtificialBrightness(40.76, -73.99)!.Value.Should().BeGreaterThan(1);
    }

    [Fact]
    public void DirectoryAtlas_SkipsMissingFiles()
    {
        var atlas = LightPollutionAtlas.FromDirectory(Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N")));

        atlas.IsAvailable.Should().BeFalse();
        atlas.ArtificialBrightness(21.88, -102.29).Should().BeNull();
    }
}
