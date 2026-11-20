using System.Buffers.Binary;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Minimal reader for the World Atlas 2015 GeoTIFF: classic little-endian TIFF, one band of
/// uncompressed 32-bit floats in tiles, georeferenced by ModelPixelScale + ModelTiepoint on a plain
/// latitude/longitude grid. That is exactly the published file (43200 × 17406 cells of 30″,
/// 128 × 128 tiles), so no TIFF library is needed; anything else is rejected.
/// </summary>
internal sealed class GeoTiffRaster : IDisposable
{
    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagTileWidth = 322;
    private const ushort TagTileLength = 323;
    private const ushort TagTileOffsets = 324;
    private const ushort TagSampleFormat = 339;
    private const ushort TagModelPixelScale = 33550;
    private const ushort TagModelTiepoint = 33922;

    private readonly FileStream _file;
    private readonly long[] _tileOffsets;

    private GeoTiffRaster(FileStream file, int width, int height, int tileWidth, int tileHeight, long[] tileOffsets,
        double westDegrees, double northDegrees, double cellDegrees)
    {
        _file = file;
        Width = width;
        Height = height;
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        _tileOffsets = tileOffsets;
        WestDegrees = westDegrees;
        NorthDegrees = northDegrees;
        CellDegrees = cellDegrees;
    }

    public int Width { get; }
    public int Height { get; }
    public int TileWidth { get; }
    public int TileHeight { get; }
    public double WestDegrees { get; }
    public double NorthDegrees { get; }
    public double CellDegrees { get; }
    public int TilesAcross => (Width + TileWidth - 1) / TileWidth;
    public int TilesDown => (Height + TileHeight - 1) / TileHeight;

    /// <exception cref="InvalidDataException">Not a TIFF of the supported kind.</exception>
    public static GeoTiffRaster Open(string path)
    {
        var file = File.OpenRead(path);
        try
        {
            return Parse(file);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static GeoTiffRaster Parse(FileStream file)
    {
        Span<byte> head = stackalloc byte[8];
        file.ReadExactly(head);
        if (head[0] != (byte)'I' || head[1] != (byte)'I' || BinaryPrimitives.ReadUInt16LittleEndian(head[2..]) != 42)
        {
            throw new InvalidDataException("Only classic little-endian TIFF files are supported.");
        }

        file.Position = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
        var countBytes = new byte[2];
        file.ReadExactly(countBytes);
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(countBytes);
        var entries = new byte[entryCount * 12];
        file.ReadExactly(entries);

        var tags = new Dictionary<ushort, (ushort Type, uint Count, uint Value)>();
        for (var i = 0; i < entryCount; i++)
        {
            var entry = entries.AsSpan(i * 12, 12);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var count = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            // SHORT values sit in the first two bytes of the value field.
            var value = type == 3 && count == 1
                ? BinaryPrimitives.ReadUInt16LittleEndian(entry[8..])
                : BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            tags[tag] = (type, count, value);
        }

        uint Required(ushort tag) =>
            tags.TryGetValue(tag, out var t) ? t.Value : throw new InvalidDataException($"TIFF tag {tag} is missing.");

        if (Required(TagBitsPerSample) != 32 || Required(TagSampleFormat) != 3 || Required(TagCompression) != 1 ||
            (tags.TryGetValue(TagSamplesPerPixel, out var spp) && spp.Value != 1))
        {
            throw new InvalidDataException("Only single-band, uncompressed 32-bit float TIFF files are supported.");
        }

        if (!tags.TryGetValue(TagTileOffsets, out var offsetsTag))
        {
            throw new InvalidDataException("Only tiled TIFF files are supported.");
        }

        var width = (int)Required(TagImageWidth);
        var height = (int)Required(TagImageLength);
        var tileWidth = (int)Required(TagTileWidth);
        var tileHeight = (int)Required(TagTileLength);
        var expectedTiles = (long)((width + tileWidth - 1) / tileWidth) * ((height + tileHeight - 1) / tileHeight);
        if (offsetsTag.Count != expectedTiles)
        {
            throw new InvalidDataException($"Expected {expectedTiles} tile offsets, found {offsetsTag.Count}.");
        }

        var offsets = new long[offsetsTag.Count];
        if (offsetsTag.Count == 1)
        {
            offsets[0] = offsetsTag.Value;
        }
        else
        {
            var raw = ReadAt(file, offsetsTag.Value, checked((int)offsetsTag.Count * 4));
            for (var i = 0; i < offsets.Length; i++)
            {
                offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4));
            }
        }

        var scale = ReadDoubles(file, tags, TagModelPixelScale, 3);
        var tiepoint = ReadDoubles(file, tags, TagModelTiepoint, 6);
        if (Math.Abs(scale[0] - scale[1]) > 1e-12 || tiepoint[0] != 0 || tiepoint[1] != 0)
        {
            throw new InvalidDataException("Only square cells tied at the top-left corner are supported.");
        }

        return new GeoTiffRaster(file, width, height, tileWidth, tileHeight, offsets, tiepoint[3], tiepoint[4], scale[0]);
    }

    private static double[] ReadDoubles(FileStream file, Dictionary<ushort, (ushort Type, uint Count, uint Value)> tags, ushort tag, int count)
    {
        if (!tags.TryGetValue(tag, out var t) || t.Type != 12 || t.Count < count)
        {
            throw new InvalidDataException($"GeoTIFF tag {tag} is missing or malformed.");
        }

        var raw = ReadAt(file, t.Value, count * 8);
        return [.. Enumerable.Range(0, count).Select(i => BinaryPrimitives.ReadDoubleLittleEndian(raw.AsSpan(i * 8)))];
    }

    private static byte[] ReadAt(FileStream file, long position, int length)
    {
        var buffer = new byte[length];
        file.Position = position;
        file.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>
    /// Reads one row of tiles (TileHeight image rows, fewer at the bottom) into
    /// <paramref name="band"/>, laid out as rows of <see cref="Width"/> values.
    /// </summary>
    /// <returns>The number of valid image rows in the band.</returns>
    public int ReadTileRow(int tileRow, float[] band)
    {
        ArgumentNullException.ThrowIfNull(band);
        if (band.Length < TileHeight * Width)
        {
            throw new ArgumentException("Band buffer too small.", nameof(band));
        }

        var rows = Math.Min(TileHeight, Height - tileRow * TileHeight);
        var tile = new byte[TileWidth * TileHeight * 4];
        for (var tileColumn = 0; tileColumn < TilesAcross; tileColumn++)
        {
            _file.Position = _tileOffsets[tileRow * TilesAcross + tileColumn];
            _file.ReadExactly(tile);
            var x0 = tileColumn * TileWidth;
            var columns = Math.Min(TileWidth, Width - x0);
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < columns; x++)
                {
                    band[y * Width + x0 + x] = BinaryPrimitives.ReadSingleLittleEndian(tile.AsSpan((y * TileWidth + x) * 4));
                }
            }
        }

        return rows;
    }

    public void Dispose() => _file.Dispose();
}
