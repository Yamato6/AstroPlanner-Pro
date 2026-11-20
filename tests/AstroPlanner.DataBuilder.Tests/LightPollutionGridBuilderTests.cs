using System.Buffers.Binary;
using System.Security.Cryptography;
using AstroPlanner.Data.LightPollution;

namespace AstroPlanner.DataBuilder.Tests;

/// <summary>build-lp-grid on a tiny synthetic GeoTIFF laid out like the World Atlas 2015 file.</summary>
public sealed class LightPollutionGridBuilderTests : IDisposable
{
    private const int Width = 30;
    private const int Height = 20;
    private const int Tile = 16;
    private const float NoData = -3.4028234663852886e38f;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public LightPollutionGridBuilderTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>1° cells, north-west corner at (10°N, 10°W).</summary>
    private static float Value(int row, int column) => row == 0 ? NoData : row == 1 ? 0f : 0.01f * (row + 1) * (column + 1);

    private static readonly LightPollutionRegion Region = new("region.bin.gz", 6, 2, -6, 0);

    private string WriteTiff(ushort compression = 1)
    {
        var tilesAcross = (Width + Tile - 1) / Tile;
        var tilesDown = (Height + Tile - 1) / Tile;
        var tileBytes = Tile * Tile * 4;
        const int entryCount = 11;
        var ifdOffset = 8;
        var ifdBytes = 2 + entryCount * 12 + 4;
        var offsetsAt = ifdOffset + ifdBytes;
        var scaleAt = offsetsAt + tilesAcross * tilesDown * 4;
        var tieAt = scaleAt + 24;
        var dataAt = tieAt + 48;
        var bytes = new byte[dataAt + tilesAcross * tilesDown * tileBytes];
        var span = bytes.AsSpan();

        "II"u8.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)ifdOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(span[ifdOffset..], entryCount);
        var entry = ifdOffset + 2;
        void Tag(ushort tag, ushort type, uint count, uint value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entry + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 4), count);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(entry + 8), value);
            entry += 12;
        }

        Tag(256, 3, 1, Width);
        Tag(257, 3, 1, Height);
        Tag(258, 3, 1, 32);
        Tag(259, 3, 1, compression);
        Tag(277, 3, 1, 1);
        Tag(322, 3, 1, Tile);
        Tag(323, 3, 1, Tile);
        Tag(324, 4, (uint)(tilesAcross * tilesDown), (uint)offsetsAt);
        Tag(339, 3, 1, 3);
        Tag(33550, 12, 3, (uint)scaleAt);
        Tag(33922, 12, 6, (uint)tieAt);

        BinaryPrimitives.WriteDoubleLittleEndian(span[scaleAt..], 1.0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[(scaleAt + 8)..], 1.0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[(tieAt + 24)..], -10.0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[(tieAt + 32)..], 10.0);

        for (var t = 0; t < tilesAcross * tilesDown; t++)
        {
            var at = dataAt + t * tileBytes;
            BinaryPrimitives.WriteUInt32LittleEndian(span[(offsetsAt + t * 4)..], (uint)at);
            var (tileRow, tileColumn) = Math.DivRem(t, tilesAcross);
            for (var y = 0; y < Tile; y++)
            {
                for (var x = 0; x < Tile; x++)
                {
                    var row = tileRow * Tile + y;
                    var column = tileColumn * Tile + x;
                    var value = row < Height && column < Width ? Value(row, column) : 12345f;   // padding
                    BinaryPrimitives.WriteSingleLittleEndian(span[(at + (y * Tile + x) * 4)..], value);
                }
            }
        }

        var path = Path.Combine(_directory, "atlas.tif");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static LightPollutionGrid Load(string path)
    {
        using var stream = File.OpenRead(path);
        return LightPollutionGrid.Read(stream);
    }

    [Fact]
    public void Raster_ReadsTheGeoreferenceAndTiles()
    {
        using var raster = GeoTiffRaster.Open(WriteTiff());

        raster.Width.Should().Be(Width);
        raster.Height.Should().Be(Height);
        raster.NorthDegrees.Should().Be(10);
        raster.WestDegrees.Should().Be(-10);
        raster.CellDegrees.Should().Be(1);
        var band = new float[Tile * Width];
        raster.ReadTileRow(1, band).Should().Be(Height - Tile);
        band[2 * Width + 25].Should().Be(Value(Tile + 2, 25));
    }

    [Fact]
    public void Raster_RejectsCompressedFiles()
    {
        var act = () => GeoTiffRaster.Open(WriteTiff(compression: 5));

        act.Should().Throw<InvalidDataException>().WithMessage("*uncompressed*");
    }

    [Fact]
    public void Build_KeepsTheRegionAtNativeResolution_AndAveragesTheWorldGrid()
    {
        var output = Path.Combine(_directory, "lp");
        var result = LightPollutionGridBuilder.Build(WriteTiff(), output, Region, "world.bin.gz", 4);

        var region = Load(Path.Combine(output, "region.bin.gz"));
        region.Rows.Should().Be(4);
        region.Columns.Should().Be(6);
        region.NorthDegrees.Should().Be(6);
        region.WestDegrees.Should().Be(-6);
        // Row 4 of the raster, column 4: quantised to within half a step (≈ 3 %).
        region.CellValue(0, 0).Should().BeApproximately(Value(4, 4), Value(4, 4) * 0.03);

        var world = Load(Path.Combine(output, "world.bin.gz"));
        world.Rows.Should().Be(5);
        world.Columns.Should().Be(8);
        world.CellDegrees.Should().Be(4);
        // Block of rows 4–7, columns 0–3: mean of the linear values.
        var mean = Enumerable.Range(4, 4).SelectMany(r => Enumerable.Range(0, 4).Select(c => (double)Value(r, c))).Average();
        world.CellValue(1, 0).Should().BeApproximately(mean, mean * 0.03);
        // Top block holds no-data and zeros: dark, not garbage.
        world.CellValue(0, 0).Should().BeApproximately(Enumerable.Range(2, 2).SelectMany(r => Enumerable.Range(0, 4).Select(c => (double)Value(r, c))).Sum() / 16, 0.002);
        // The partial last column block (columns 28–29) averages only real cells, never the tile padding.
        var corner = Enumerable.Range(16, 4).SelectMany(r => Enumerable.Range(28, 2).Select(c => (double)Value(r, c))).Average();
        world.CellValue(4, 7).Should().BeApproximately(corner, corner * 0.03);
        result.Report.Should().Contain("region.bin.gz").And.Contain("world.bin.gz");
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var tiff = WriteTiff();
        LightPollutionGridBuilder.Build(tiff, Path.Combine(_directory, "a"), Region, "world.bin.gz", 4);
        LightPollutionGridBuilder.Build(tiff, Path.Combine(_directory, "b"), Region, "world.bin.gz", 4);

        SHA256.HashData(File.ReadAllBytes(Path.Combine(_directory, "a", "region.bin.gz")))
            .Should().Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(_directory, "b", "region.bin.gz"))));
    }

    [Fact]
    public void Verify_ReportsMissingFiles_AndCheckpointsOutOfRange()
    {
        var empty = Path.Combine(_directory, "empty");
        Directory.CreateDirectory(empty);
        LightPollutionGridBuilder.Verify(empty, []).Should().HaveCount(2);

        var output = Path.Combine(_directory, "lp");
        LightPollutionGridBuilder.Build(WriteTiff(), output, Region with { FileName = LightPollutionAtlas.FileNames[0] },
            LightPollutionAtlas.FileNames[1], 4);
        var bright = new LightPollutionCheckpoint("bright cell", 5.5, -5.5, 0.15, 0.3);
        var wrong = new LightPollutionCheckpoint("expected dark", 5.5, -5.5, 0, 0.01);

        LightPollutionGridBuilder.Verify(output, [bright]).Should().BeEmpty();
        LightPollutionGridBuilder.Verify(output, [wrong]).Should().ContainSingle().Which.Should().Contain("expected dark");
    }
}
