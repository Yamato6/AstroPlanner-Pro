using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Imaging;

/// <summary>An 8-bit RGB image, rows top to bottom, 3 bytes per pixel.</summary>
public sealed class RgbImage
{
    public RgbImage(int width, int height, byte[]? pixels = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Pixels = pixels ?? new byte[width * height * 3];
        if (Pixels.Length != width * height * 3)
        {
            throw new ArgumentException("The pixel buffer does not match the size.", nameof(pixels));
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public (byte R, byte G, byte B) this[int x, int y]
    {
        get
        {
            var i = ((y * Width) + x) * 3;
            return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
        }
    }
}

/// <summary>A HiPS survey (IVOA HiPS 1.0) as the preview needs it.</summary>
/// <param name="Id">Short stable id, also the cache folder key, e.g. "DSSColor".</param>
/// <param name="MaximumOrder">The survey's <c>hips_order</c> (deepest tiles).</param>
/// <param name="TileWidth">The survey's <c>hips_tile_width</c> (a power of two).</param>
public sealed record HipsSurvey(string Id, string Name, int MaximumOrder, int TileWidth = 512);

/// <summary>One HiPS tile (<c>Norder{Order}/Dir…/Npix{Index}</c>), or the order-3 <c>Allsky</c> mosaic.</summary>
public readonly record struct HipsTileKey(int Order, long Index)
{
    /// <summary>The order-3 Allsky image: all 768 order-3 tiles reduced to 64 px, 27 per row.</summary>
    public static readonly HipsTileKey Allsky = new(3, -1);

    public bool IsAllsky => Index < 0;
}

/// <summary>What to frame: the rig's field centred on a target, rotated by the camera angle.</summary>
/// <param name="RotationDegrees">Angle of the image's up side from north through east (0 = north up, east left).</param>
/// <param name="OutputWidth">Width of the preview in pixels; the height follows the field's aspect.</param>
public sealed record FramingRequest(
    double CenterRaDegrees,
    double CenterDecDegrees,
    double WidthDegrees,
    double HeightDegrees,
    double RotationDegrees = 0,
    int OutputWidth = 640);

/// <summary>
/// Framing preview from HiPS tiles (spec §4.4): the rig's field drawn with a gnomonic (TAN)
/// projection, as the camera sees it, sampled from the survey's HEALPix tiles. <see cref="Plan"/>
/// says which tiles are needed; <see cref="FramingPlan.Compose"/> builds the image once they are
/// decoded. Inside a JPEG tile, the pixel of face coordinates (x, y) at order + log2(width) is at
/// row x, column y (checked against DSS stars; tests/fixtures/online).
/// </summary>
public static class FramingPreview
{
    /// <summary>Order of the Allsky mosaic and its reduced tile width.</summary>
    public const int AllskyOrder = 3;
    public const int AllskyTileWidth = 64;
    public const int AllskyColumns = 27;

    /// <summary>Lowest tile order (HiPS tiles start at order 3).</summary>
    public const int MinimumTileOrder = 3;

    /// <summary>The image may be enlarged by up to this factor before a deeper order is used.</summary>
    public const double AcceptedUpsampling = 1.5;

    /// <summary>Most tiles fetched for one preview; a coarser order is used beyond that.</summary>
    public const int MaximumTiles = 36;

    public static FramingPlan Plan(FramingRequest request, HipsSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(survey);
        if (request.WidthDegrees is <= 0 or >= 170 || request.HeightDegrees is <= 0 or >= 170)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The field must be between 0 and 170°.");
        }

        var tileBits = Log2(survey.TileWidth);
        var halfWidth = Math.Tan(request.WidthDegrees * Math.PI / 360);
        var halfHeight = Math.Tan(request.HeightDegrees * Math.PI / 360);
        var width = Math.Max(16, request.OutputWidth);
        var height = Math.Max(16, (int)Math.Round(width * halfHeight / halfWidth));
        var scale = 2 * halfWidth / width;   // tangent-plane units per output pixel
        var pixelDegrees = Math.Atan(scale) * 180 / Math.PI;

        var directions = Project(request, width, height, scale);

        // Smallest order whose tile pixels are fine enough, then coarser while too many tiles.
        var order = Enumerable.Range(MinimumTileOrder, Math.Max(0, survey.MaximumOrder - MinimumTileOrder + 1))
            .Cast<int?>()
            .FirstOrDefault(k => Healpix.PixelSizeDegrees(k!.Value + tileBits) <= pixelDegrees * AcceptedUpsampling)
            ?? survey.MaximumOrder;
        if (Healpix.PixelSizeDegrees(AllskyOrder + Log2(AllskyTileWidth)) <= pixelDegrees * AcceptedUpsampling)
        {
            return Sample(directions, width, height, request, scale, HipsTileKey.Allsky.Order, Log2(AllskyTileWidth), allsky: true);
        }

        while (true)
        {
            var plan = Sample(directions, width, height, request, scale, order, tileBits, allsky: false);
            if (plan.Tiles.Count <= MaximumTiles)
            {
                return plan;
            }

            if (order <= MinimumTileOrder)
            {
                return Sample(directions, width, height, request, scale, AllskyOrder, Log2(AllskyTileWidth), allsky: true);
            }

            order--;
        }
    }

    /// <summary>RA/Dec (degrees) of every output pixel centre, row by row.</summary>
    private static (double Ra, double Dec)[] Project(FramingRequest request, int width, int height, double scale)
    {
        var ra0 = request.CenterRaDegrees * Math.PI / 180;
        var dec0 = request.CenterDecDegrees * Math.PI / 180;
        var (sinDec0, cosDec0) = Math.SinCos(dec0);
        var (sinRot, cosRot) = Math.SinCos(request.RotationDegrees * Math.PI / 180);
        var result = new (double, double)[width * height];
        for (var y = 0; y < height; y++)
        {
            var v = ((height / 2.0) - y - 0.5) * scale;   // up
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5 - (width / 2.0)) * scale;   // right
                // Image up = (sin θ, cos θ) and right = (−cos θ, sin θ) in (ξ east, η north).
                var xi = (v * sinRot) - (u * cosRot);
                var eta = (v * cosRot) + (u * sinRot);
                var rho = Math.Sqrt((xi * xi) + (eta * eta));
                double ra;
                double dec;
                if (rho < 1e-15)
                {
                    ra = ra0;
                    dec = dec0;
                }
                else
                {
                    var c = Math.Atan(rho);
                    var (sinC, cosC) = Math.SinCos(c);
                    dec = Math.Asin(Math.Clamp((cosC * sinDec0) + (eta * sinC * cosDec0 / rho), -1, 1));
                    ra = ra0 + Math.Atan2(xi * sinC, (rho * cosDec0 * cosC) - (eta * sinDec0 * sinC));
                }

                result[(y * width) + x] = (ra * 180 / Math.PI, dec * 180 / Math.PI);
            }
        }

        return result;
    }

    private static FramingPlan Sample(
        (double Ra, double Dec)[] directions, int width, int height, FramingRequest request, double scale, int order, int tileBits, bool allsky)
    {
        var fineOrder = order + tileBits;
        var tileWidth = 1 << tileBits;
        var tiles = new Dictionary<HipsTileKey, int>();
        var slot = new int[directions.Length];
        var row = new float[directions.Length];
        var column = new float[directions.Length];
        for (var i = 0; i < directions.Length; i++)
        {
            var point = Healpix.ToFacePoint(fineOrder, directions[i].Ra, directions[i].Dec);
            var tileX = (long)Math.Floor(point.X) >> tileBits;
            var tileY = (long)Math.Floor(point.Y) >> tileBits;
            var index = Healpix.FaceXyToNested(order, point.Face, tileX, tileY);
            var key = allsky ? HipsTileKey.Allsky : new HipsTileKey(order, index);
            if (!tiles.TryGetValue(key, out var s))
            {
                s = tiles.Count;
                tiles.Add(key, s);
            }

            // Pixel centres are at integer + 0.5: rows follow x, columns follow y.
            var localRow = point.X - (tileX * tileWidth) - 0.5;
            var localColumn = point.Y - (tileY * tileWidth) - 0.5;
            if (allsky)
            {
                // Offset to the reduced tile inside the mosaic; kept inside it when composing.
                localRow = Math.Clamp(localRow, 0, tileWidth - 1) + ((index / AllskyColumns) * tileWidth);
                localColumn = Math.Clamp(localColumn, 0, tileWidth - 1) + ((index % AllskyColumns) * tileWidth);
            }

            slot[i] = s;
            row[i] = (float)localRow;
            column[i] = (float)localColumn;
        }

        var keys = new HipsTileKey[tiles.Count];
        foreach (var (key, s) in tiles)
        {
            keys[s] = key;
        }

        return new FramingPlan(request, width, height, Math.Atan(scale) * 180 / Math.PI, scale, order, allsky, tileWidth, keys, slot, row, column);
    }

    internal static int Log2(int value)
    {
        if (value <= 0 || (value & (value - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "The tile width must be a power of two.");
        }

        return System.Numerics.BitOperations.Log2((uint)value);
    }
}

/// <summary>The tiles a preview needs and where each output pixel samples them.</summary>
public sealed class FramingPlan
{
    private readonly int[] _slot;
    private readonly float[] _row;
    private readonly float[] _column;
    private readonly double _scale;

    internal FramingPlan(
        FramingRequest request, int width, int height, double pixelDegrees, double scale, int order, bool usesAllsky, int tileWidth,
        IReadOnlyList<HipsTileKey> tiles, int[] slot, float[] row, float[] column)
    {
        Request = request;
        Width = width;
        Height = height;
        PixelDegrees = pixelDegrees;
        _scale = scale;
        Order = order;
        UsesAllsky = usesAllsky;
        TileWidth = tileWidth;
        Tiles = tiles;
        _slot = slot;
        _row = row;
        _column = column;
    }

    public FramingRequest Request { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Angular size of an output pixel at the centre, in degrees.</summary>
    public double PixelDegrees { get; }

    /// <summary>Tile order sampled (3 for the Allsky mosaic).</summary>
    public int Order { get; }

    public bool UsesAllsky { get; }

    /// <summary>Width of one tile (64 for the Allsky mosaic's reduced tiles).</summary>
    public int TileWidth { get; }

    /// <summary>The tiles to fetch, each once.</summary>
    public IReadOnlyList<HipsTileKey> Tiles { get; }

    /// <summary>
    /// The preview from the decoded tiles. A missing tile (not fetched or failed) leaves its pixels
    /// black; <paramref name="coverage"/> is the fraction of pixels that had a tile.
    /// </summary>
    public RgbImage Compose(IReadOnlyDictionary<HipsTileKey, RgbImage> tiles, out double coverage)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        var images = Tiles.Select(k => tiles.TryGetValue(k, out var image) ? image : null).ToArray();
        var output = new RgbImage(Width, Height);
        var covered = 0;
        for (var i = 0; i < _slot.Length; i++)
        {
            if (images[_slot[i]] is not { } image)
            {
                continue;
            }

            var (minRow, maxRow, minColumn, maxColumn) = UsesAllsky
                ? MiniTileBounds(_row[i], _column[i])
                : (0, image.Height - 1, 0, image.Width - 1);
            maxRow = Math.Min(maxRow, image.Height - 1);
            maxColumn = Math.Min(maxColumn, image.Width - 1);
            if (minRow > maxRow || minColumn > maxColumn)
            {
                continue;   // the image received is smaller than the survey says: not covered
            }

            covered++;
            Bilinear(image, _row[i], _column[i], minRow, maxRow, minColumn, maxColumn, output.Pixels, i * 3);
        }

        coverage = _slot.Length == 0 ? 0 : (double)covered / _slot.Length;
        return output;
    }

    /// <summary>
    /// Clockwise angle on screen, from the image's up direction, of a sky position angle (north
    /// through east): e.g. a galaxy's major axis.
    /// </summary>
    public double ScreenAngleDegrees(double positionAngleDegrees) =>
        Modulo(Request.RotationDegrees - positionAngleDegrees, 360);

    /// <summary>Output pixel (x right, y down; pixel centres at integers) of a sky position, or null behind the tangent plane.</summary>
    public (double X, double Y)? ToPixel(double raDegrees, double decDegrees)
    {
        var dec0 = Request.CenterDecDegrees * Math.PI / 180;
        var dec = decDegrees * Math.PI / 180;
        var dRa = (raDegrees - Request.CenterRaDegrees) * Math.PI / 180;
        var cosC = (Math.Sin(dec0) * Math.Sin(dec)) + (Math.Cos(dec0) * Math.Cos(dec) * Math.Cos(dRa));
        if (cosC <= 1e-9)
        {
            return null;
        }

        var xi = Math.Cos(dec) * Math.Sin(dRa) / cosC;
        var eta = ((Math.Cos(dec0) * Math.Sin(dec)) - (Math.Sin(dec0) * Math.Cos(dec) * Math.Cos(dRa))) / cosC;
        var (sinRot, cosRot) = Math.SinCos(Request.RotationDegrees * Math.PI / 180);
        var u = (eta * sinRot) - (xi * cosRot);
        var v = (xi * sinRot) + (eta * cosRot);
        return ((u / _scale) + (Width / 2.0) - 0.5, (Height / 2.0) - 0.5 - (v / _scale));
    }

    /// <summary>Length on screen, in output pixels, of an angle on the sky near the centre.</summary>
    public double PixelsFor(double arcminutes) => Math.Tan(arcminutes * Math.PI / 10800) / _scale;

    private (int, int, int, int) MiniTileBounds(float row, float column)
    {
        var top = (int)(Math.Floor((row + 0.5) / TileWidth) * TileWidth);
        var left = (int)(Math.Floor((column + 0.5) / TileWidth) * TileWidth);
        return (top, top + TileWidth - 1, left, left + TileWidth - 1);
    }

    private static void Bilinear(RgbImage image, double row, double column, int minRow, int maxRow, int minColumn, int maxColumn, byte[] output, int offset)
    {
        row = Math.Clamp(row, minRow, maxRow);
        column = Math.Clamp(column, minColumn, maxColumn);
        var r0 = (int)Math.Floor(row);
        var c0 = (int)Math.Floor(column);
        var r1 = Math.Min(r0 + 1, maxRow);
        var c1 = Math.Min(c0 + 1, maxColumn);
        var fr = row - r0;
        var fc = column - c0;
        var pixels = image.Pixels;
        var stride = image.Width * 3;
        for (var channel = 0; channel < 3; channel++)
        {
            var top = (pixels[(r0 * stride) + (c0 * 3) + channel] * (1 - fc)) + (pixels[(r0 * stride) + (c1 * 3) + channel] * fc);
            var bottom = (pixels[(r1 * stride) + (c0 * 3) + channel] * (1 - fc)) + (pixels[(r1 * stride) + (c1 * 3) + channel] * fc);
            output[offset + channel] = (byte)Math.Clamp(Math.Round((top * (1 - fr)) + (bottom * fr)), 0, 255);
        }
    }

    private static double Modulo(double value, double period)
    {
        var result = value % period;
        return result < 0 ? result + period : result;
    }
}
