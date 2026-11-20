using System.Buffers.Binary;
using System.IO.Compression;

namespace AstroPlanner.Data.LightPollution;

/// <summary>
/// A regular latitude/longitude grid of artificial zenith sky brightness (mcd/m²), quantised to one
/// byte per cell in log10 space (spec §7.2). Written by the DataBuilder (<c>build-lp-grid</c>) from
/// the World Atlas 2015 GeoTIFF and read by <see cref="LightPollutionAtlas"/>.
/// </summary>
/// <remarks>
/// File = gzip of: magic "APLP", uint16 format version, uint16 reserved, int32 rows, int32 columns,
/// float64 north edge (degrees), float64 west edge, float64 cell size (degrees), then rows × columns
/// bytes, row by row from north to south. Byte 0 = below <see cref="MinimumBrightness"/> (nothing
/// that matters next to the 0.171 mcd/m² natural sky); byte c ≥ 1 = 10^(<see cref="Log10Minimum"/>
/// + (c − 1)·<see cref="Log10Step"/>). The step is 0.0256 dex ≈ 0.064 mag, below the atlas' own
/// uncertainty. All little-endian.
/// </remarks>
public sealed class LightPollutionGrid
{
    public const int FormatVersion = 1;
    public const int HeaderBytes = 40;
    public const double Log10Minimum = -4.0;
    public const double Log10Maximum = 2.5;
    public const double Log10Step = (Log10Maximum - Log10Minimum) / 254;
    public static readonly double MinimumBrightness = Math.Pow(10, Log10Minimum);

    private static ReadOnlySpan<byte> Magic => "APLP"u8;

    private static readonly double[] Decoded = BuildDecodeTable();

    private readonly byte[] _cells;

    public LightPollutionGrid(int rows, int columns, double northDegrees, double westDegrees, double cellDegrees, byte[] cells)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellDegrees);
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.Length != (long)rows * columns)
        {
            throw new ArgumentException($"Expected {rows} × {columns} cells, got {cells.Length}.", nameof(cells));
        }

        Rows = rows;
        Columns = columns;
        NorthDegrees = northDegrees;
        WestDegrees = westDegrees;
        CellDegrees = cellDegrees;
        _cells = cells;
    }

    public int Rows { get; }
    public int Columns { get; }
    public double NorthDegrees { get; }
    public double WestDegrees { get; }
    public double CellDegrees { get; }
    public double SouthDegrees => NorthDegrees - Rows * CellDegrees;
    public double EastDegrees => WestDegrees + Columns * CellDegrees;

    /// <summary>Byte code of a brightness in mcd/m² (rounded to the nearest step, clamped).</summary>
    public static byte Encode(double brightness)
    {
        if (!(brightness >= MinimumBrightness))
        {
            return 0;   // also NaN and the GeoTIFF's no-data value
        }

        var step = Math.Round((Math.Log10(brightness) - Log10Minimum) / Log10Step);
        return (byte)(1 + Math.Clamp(step, 0, 254));
    }

    /// <summary>Brightness in mcd/m² of a byte code.</summary>
    public static double Decode(byte code) => Decoded[code];

    /// <summary>Brightness stored in one cell.</summary>
    public double CellValue(int row, int column) => Decoded[_cells[(long)row * Columns + column]];

    /// <summary>True when the point lies inside the grid's area.</summary>
    public bool Contains(double latitudeDegrees, double longitudeDegrees) =>
        latitudeDegrees <= NorthDegrees && latitudeDegrees >= SouthDegrees &&
        longitudeDegrees >= WestDegrees && longitudeDegrees <= EastDegrees;

    /// <summary>
    /// Brightness at a point, interpolated bilinearly between cell centres in linear units (edge
    /// cells are extended), or null outside the grid.
    /// </summary>
    public double? Sample(double latitudeDegrees, double longitudeDegrees)
    {
        if (!Contains(latitudeDegrees, longitudeDegrees))
        {
            return null;
        }

        var y = (NorthDegrees - latitudeDegrees) / CellDegrees - 0.5;
        var x = (longitudeDegrees - WestDegrees) / CellDegrees - 0.5;
        var row0 = Math.Clamp((int)Math.Floor(y), 0, Rows - 1);
        var col0 = Math.Clamp((int)Math.Floor(x), 0, Columns - 1);
        var row1 = Math.Min(row0 + 1, Rows - 1);
        var col1 = Math.Min(col0 + 1, Columns - 1);
        var ty = Math.Clamp(y - row0, 0, 1);
        var tx = Math.Clamp(x - col0, 0, 1);

        var top = CellValue(row0, col0) * (1 - tx) + CellValue(row0, col1) * tx;
        var bottom = CellValue(row1, col0) * (1 - tx) + CellValue(row1, col1) * tx;
        return top * (1 - ty) + bottom * ty;
    }

    /// <summary>Writes the grid (gzip-compressed, deterministic: same cells ⇒ same bytes).</summary>
    public void Write(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        using var gzip = new GZipStream(destination, CompressionLevel.SmallestSize, leaveOpen: true);
        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], Rows);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], Columns);
        BinaryPrimitives.WriteDoubleLittleEndian(header[16..], NorthDegrees);
        BinaryPrimitives.WriteDoubleLittleEndian(header[24..], WestDegrees);
        BinaryPrimitives.WriteDoubleLittleEndian(header[32..], CellDegrees);
        gzip.Write(header);
        gzip.Write(_cells);
    }

    /// <summary>Reads a grid written by <see cref="Write"/>.</summary>
    /// <exception cref="InvalidDataException">Not a grid file, or an unknown version.</exception>
    public static LightPollutionGrid Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: true);
        var header = new byte[HeaderBytes];
        gzip.ReadExactly(header);
        if (!header.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a light-pollution grid (bad magic).");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4));
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported light-pollution grid version {version}.");
        }

        var rows = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
        var columns = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
        if (rows <= 0 || columns <= 0 || (long)rows * columns > 100_000_000)
        {
            throw new InvalidDataException($"Implausible light-pollution grid size {rows} × {columns}.");
        }

        var cells = new byte[rows * columns];
        gzip.ReadExactly(cells);
        return new LightPollutionGrid(
            rows,
            columns,
            BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(16)),
            BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(24)),
            BinaryPrimitives.ReadDoubleLittleEndian(header.AsSpan(32)),
            cells);
    }

    private static double[] BuildDecodeTable()
    {
        var table = new double[256];
        for (var code = 1; code < table.Length; code++)
        {
            table[code] = Math.Pow(10, Log10Minimum + (code - 1) * Log10Step);
        }

        return table;
    }
}
