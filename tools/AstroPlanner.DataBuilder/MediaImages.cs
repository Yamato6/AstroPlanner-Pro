using System.Runtime.InteropServices;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Sky;
using SkiaSharp;

namespace AstroPlanner.DataBuilder;

/// <summary>An 8-bit RGBA image (unpremultiplied), row-major from the top-left pixel.</summary>
internal sealed class Raster
{
    public Raster(int width, int height, byte[]? rgba = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        Rgba = rgba ?? new byte[width * height * 4];
        if (Rgba.Length != width * height * 4)
        {
            throw new ArgumentException("The pixel buffer does not match the size.", nameof(rgba));
        }
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public int Offset(int x, int y) => (y * Width + x) * 4;

    /// <summary>Bilinear sample of channel <paramref name="c"/> at pixel coordinates (centres at +0.5), wrapping in x.</summary>
    public double Sample(double x, double y, int c)
    {
        x -= 0.5;
        y = Math.Clamp(y - 0.5, 0, Height - 1);
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        var y1 = Math.Min(y0 + 1, Height - 1);
        var xa = Wrap(x0);
        var xb = Wrap(x0 + 1);
        var top = Rgba[Offset(xa, y0) + c] * (1 - fx) + Rgba[Offset(xb, y0) + c] * fx;
        var bottom = Rgba[Offset(xa, y1) + c] * (1 - fx) + Rgba[Offset(xb, y1) + c] * fx;
        return top * (1 - fy) + bottom * fy;
    }

    private int Wrap(int x) => ((x % Width) + Width) % Width;
}

/// <summary>Decoding and WebP encoding (SkiaSharp; the DataBuilder only).</summary>
internal static class ImageFiles
{
    public static Raster Load(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException($"{path}: not an image SkiaSharp can read.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            throw new InvalidDataException($"{path}: decoding failed ({result}).");
        }

        var raster = new Raster(info.Width, info.Height);
        Marshal.Copy(bitmap.GetPixels(), raster.Rgba, 0, raster.Rgba.Length);
        return raster;
    }

    /// <summary>Writes a lossy WebP (the alpha channel, when used, is kept lossless by the encoder).</summary>
    public static void SaveWebp(Raster raster, string path, int quality)
    {
        var info = new SKImageInfo(raster.Width, raster.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(info);
        Marshal.Copy(raster.Rgba, 0, bitmap.GetPixels(), raster.Rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality)
            ?? throw new InvalidDataException("WebP encoding failed.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        data.SaveTo(file);
    }
}

/// <summary>
/// The Milky Way panorama (spec §8.3): the galactic ESO image without its stars (the renderer
/// draws its own), with the sky's background set to black (it is added to the sky), reprojected
/// to the J2000 frame the renderer samples: equirectangular, u = RA / 360° (RA 0 at the left
/// edge, growing to the right), v = (90° − Dec) / 180°.
/// </summary>
internal static class PanoramaBuilder
{
    /// <summary>Half-size of the opening window: wider than a star's image in the 6000×3000 source.</summary>
    public const int StarRadius = 3;

    /// <summary>How much brighter than the opened background a pixel may stay (0–255).</summary>
    public const int StarThreshold = 10;

    /// <summary>Share of the darkest pixels that become black, per channel.</summary>
    public const double BlackPointFraction = 0.05;

    public static Raster Build(Raster galactic, int width)
    {
        ArgumentNullException.ThrowIfNull(galactic);
        var clean = SuppressStars(galactic, StarRadius, StarThreshold);
        SetBlackPoint(clean, BlackPointFraction);
        return ToEquatorial(clean, width);
    }

    /// <summary>
    /// Clips point sources: each channel is limited to its grey-scale opening (erosion then
    /// dilation over a (2r+1)² square, which removes anything smaller than the square) plus
    /// <paramref name="threshold"/>. Extended light (the Milky Way, nebulae) passes unchanged.
    /// </summary>
    public static Raster SuppressStars(Raster source, int radius, int threshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new Raster(source.Width, source.Height, (byte[])source.Rgba.Clone());
        Parallel.For(0, 3, c =>
        {
            var channel = Channel(source, c);
            var opened = Dilate(Erode(channel, source.Width, source.Height, radius), source.Width, source.Height, radius);
            for (var i = 0; i < channel.Length; i++)
            {
                output.Rgba[i * 4 + c] = (byte)Math.Min(channel[i], opened[i] + threshold);
            }
        });
        return output;
    }

    /// <summary>Subtracts each channel's <paramref name="fraction"/> quantile and stretches the rest back to 0–255.</summary>
    public static void SetBlackPoint(Raster raster, double fraction)
    {
        ArgumentNullException.ThrowIfNull(raster);
        var pixels = raster.Width * raster.Height;
        for (var c = 0; c < 3; c++)
        {
            var histogram = new long[256];
            for (var i = 0; i < pixels; i++)
            {
                histogram[raster.Rgba[i * 4 + c]]++;
            }

            var black = 0;
            for (long count = 0; black < 255 && (count += histogram[black]) < fraction * pixels;)
            {
                black++;
            }

            var scale = 255.0 / Math.Max(1, 255 - black);
            for (var i = 0; i < pixels; i++)
            {
                var o = i * 4 + c;
                raster.Rgba[o] = (byte)Math.Clamp(Math.Round((raster.Rgba[o] - black) * scale), 0, 255);
            }
        }

        for (var i = 0; i < pixels; i++)
        {
            raster.Rgba[i * 4 + 3] = 255;
        }
    }

    /// <summary>
    /// Galactic equirectangular (l = 0 at the centre, l growing to the left, b = +90° at the top)
    /// → J2000 equirectangular of <paramref name="width"/> × width/2, bilinear.
    /// </summary>
    public static Raster ToEquatorial(Raster galactic, int width)
    {
        ArgumentNullException.ThrowIfNull(galactic);
        var height = width / 2;
        var output = new Raster(width, height);
        Parallel.For(0, height, y =>
        {
            var dec = 90 - (y + 0.5) * 180.0 / height;
            for (var x = 0; x < width; x++)
            {
                var ra = (x + 0.5) * 360.0 / width;
                var (l, b) = GalacticCoordinates.FromEquatorial(Vector3d.FromSpherical(ra, dec));
                var (sx, sy) = GalacticPixel(galactic, l, b);
                var o = output.Offset(x, y);
                for (var c = 0; c < 3; c++)
                {
                    output.Rgba[o + c] = (byte)Math.Clamp(Math.Round(galactic.Sample(sx, sy, c)), 0, 255);
                }

                output.Rgba[o + 3] = 255;
            }
        });
        return output;
    }

    /// <summary>Where (l, b) is in the galactic source image (pixel coordinates, centres at +0.5).</summary>
    public static (double X, double Y) GalacticPixel(Raster galactic, double longitudeDegrees, double latitudeDegrees)
    {
        ArgumentNullException.ThrowIfNull(galactic);
        var l = longitudeDegrees > 180 ? longitudeDegrees - 360 : longitudeDegrees;
        return ((0.5 - l / 360) * galactic.Width, (0.5 - latitudeDegrees / 180) * galactic.Height);
    }

    private static byte[] Channel(Raster raster, int c)
    {
        var values = new byte[raster.Width * raster.Height];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = raster.Rgba[i * 4 + c];
        }

        return values;
    }

    private static byte[] Erode(byte[] values, int width, int height, int radius) => Filter(values, width, height, radius, minimum: true);

    private static byte[] Dilate(byte[] values, int width, int height, int radius) => Filter(values, width, height, radius, minimum: false);

    /// <summary>Square min/max filter, separable (rows, then columns); wraps in x like the sky.</summary>
    private static byte[] Filter(byte[] values, int width, int height, int radius, bool minimum)
    {
        var rows = new byte[values.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = values[y * width + x];
                for (var d = -radius; d <= radius; d++)
                {
                    var v = values[y * width + ((x + d) % width + width) % width];
                    best = minimum ? Math.Min(best, v) : Math.Max(best, v);
                }

                rows[y * width + x] = best;
            }
        }

        var output = new byte[values.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var best = rows[y * width + x];
                for (var d = Math.Max(0, y - radius); d <= Math.Min(height - 1, y + radius); d++)
                {
                    var v = rows[d * width + x];
                    best = minimum ? Math.Min(best, v) : Math.Max(best, v);
                }

                output[y * width + x] = best;
            }
        }

        return output;
    }
}

/// <summary>A landscape's build settings (data/curated/landscapes.json).</summary>
/// <param name="CutHoles">Also cut out sky seen inside the ground (between branches); off where distant hazy hills look like sky.</param>
internal sealed record LandscapeSettings(double AzimuthOffsetDegrees, int SkyBlueMinimum, bool CutHoles);

/// <summary>
/// The ground of a 360° photo (spec §8.5): sky pixels become transparent, and the outline gives the
/// horizon profile. A pixel is sky-like when it is blue (blue above red and 0.85 × green by
/// <see cref="LandscapeSettings.SkyBlueMinimum"/>), not dark and smooth, and above −2°. The sky is
/// what is sky-like and connected to the top of the photo (a flood fill), plus whatever is not
/// connected to its bottom through ground (the Sun, wires floating in the sky) and, with
/// <see cref="LandscapeSettings.CutHoles"/>, clearly blue pixels enclosed by the ground (by
/// <see cref="HoleBlueMargin"/> more: grey-blue walls stay).
/// </summary>
internal static class LandscapeBuilder
{
    public const double MinimumSkyLuminance = 45;
    public const double MaximumSkyRoughness = 10;
    public const double LowestSkyAltitude = -2;

    /// <summary>How much bluer than a sky-like pixel a hole in the ground must be to be cut out.</summary>
    public const int HoleBlueMargin = 20;

    /// <summary>The image above the highest ground is not stored: one degree of margin.</summary>
    public const double TopMarginDegrees = 1;

    /// <summary>Ground pixels this close (px) to the sky mix in the photo's sky: they take the colour of the ground behind them.</summary>
    public const int OutlineWidth = 2;

    /// <summary>Brightness kept by outline pixels with no ground behind them (wires against the sky).</summary>
    public const double ThinShade = 0.35;

    /// <param name="Image">Columns cover azimuth 0°–360° from the left edge (north); rows run from <see cref="TopAltitudeDegrees"/> down to −90°.</param>
    /// <param name="Horizon">Altitude of the ground's top for each degree of azimuth (index = azimuth), the highest in that degree.</param>
    public sealed record Result(Raster Image, double TopAltitudeDegrees, double[] Horizon);

    public static Result Build(Raster photo, LandscapeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(settings);
        var ground = Ground(photo, settings.SkyBlueMinimum, settings.CutHoles);
        var outline = Outline(ground, photo.Width, photo.Height, OutlineWidth);
        var shift = (int)Math.Round(settings.AzimuthOffsetDegrees / 360 * photo.Width);
        var width = photo.Width;
        var height = photo.Height;

        // Top of the ground per column of the output (azimuth = column / width × 360°).
        var top = new int[width];
        for (var x = 0; x < width; x++)
        {
            var sx = (((x - shift) % width) + width) % width;
            var y = 0;
            while (y < height && !ground[y * width + sx])
            {
                y++;
            }

            top[x] = y;
        }

        var horizon = new double[360];
        Array.Fill(horizon, -90);
        for (var x = 0; x < width; x++)
        {
            var degree = Math.Min(359, (int)((x + 0.5) * 360.0 / width));
            horizon[degree] = Math.Max(horizon[degree], Altitude(top[x], height));
        }

        var highest = horizon.Max();
        var topAltitude = Math.Min(90, Math.Ceiling(highest + TopMarginDegrees));
        var firstRow = Math.Max(0, (int)Math.Floor((90 - topAltitude) / 180 * height));
        topAltitude = 90 - firstRow * 180.0 / height;

        var image = new Raster(width, height - firstRow);
        Parallel.For(0, width, x =>
        {
            var sx = (((x - shift) % width) + width) % width;
            for (var y = firstRow; y < height; y++)
            {
                var source = photo.Offset(sx, y);
                var target = image.Offset(x, y - firstRow);
                if (ground[y * width + sx])
                {
                    var (r, g, b) = outline[y * width + sx] ? InteriorColour(photo, ground, outline, sx, y) : Pixel(photo, source);
                    image.Rgba[target] = r;
                    image.Rgba[target + 1] = g;
                    image.Rgba[target + 2] = b;
                    image.Rgba[target + 3] = 255;
                }
            }

            // Transparent pixels take the colour of the ground below them: bilinear filtering and
            // mipmaps then never mix in the photo's daylight sky.
            byte fr = 0, fg = 0, fb = 0;
            for (var y = height - 1; y >= firstRow; y--)
            {
                var target = image.Offset(x, y - firstRow);
                if (image.Rgba[target + 3] == 255)
                {
                    (fr, fg, fb) = (image.Rgba[target], image.Rgba[target + 1], image.Rgba[target + 2]);
                }
                else
                {
                    image.Rgba[target] = fr;
                    image.Rgba[target + 1] = fg;
                    image.Rgba[target + 2] = fb;
                }
            }
        });

        return new Result(image, topAltitude, [.. horizon.Select(h => Math.Round(h, 2))]);
    }

    /// <summary>
    /// Shrinks by a whole <paramref name="factor"/> (box filter). Colours are weighted by their
    /// alpha, so the transparent sky never darkens the outline; a fully transparent block keeps
    /// the mean of its (ground-coloured) pixels.
    /// </summary>
    public static Raster Downsample(Raster image, int factor)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(factor);
        if (factor == 1)
        {
            return image;
        }

        var output = new Raster(image.Width / factor, image.Height / factor);
        Parallel.For(0, output.Height, y =>
        {
            for (var x = 0; x < output.Width; x++)
            {
                double r = 0, g = 0, b = 0, alpha = 0, pr = 0, pg = 0, pb = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    for (var dx = 0; dx < factor; dx++)
                    {
                        var o = image.Offset(x * factor + dx, y * factor + dy);
                        var a = image.Rgba[o + 3] / 255.0;
                        r += image.Rgba[o] * a;
                        g += image.Rgba[o + 1] * a;
                        b += image.Rgba[o + 2] * a;
                        alpha += a;
                        pr += image.Rgba[o];
                        pg += image.Rgba[o + 1];
                        pb += image.Rgba[o + 2];
                    }
                }

                var n = factor * factor;
                var t = output.Offset(x, y);
                (output.Rgba[t], output.Rgba[t + 1], output.Rgba[t + 2]) = alpha > 0
                    ? (Byte(r / alpha), Byte(g / alpha), Byte(b / alpha))
                    : (Byte(pr / n), Byte(pg / n), Byte(pb / n));
                output.Rgba[t + 3] = Byte(alpha / n * 255);
            }
        });
        return output;

        static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
    }

    /// <summary>True for ground pixels (see the class summary).</summary>
    public static bool[] Ground(Raster photo, int skyBlueMinimum, bool cutHoles)
    {
        ArgumentNullException.ThrowIfNull(photo);
        var width = photo.Width;
        var height = photo.Height;
        var skyLike = new bool[width * height];
        var clearSky = new bool[width * height];
        Parallel.For(0, height, y =>
        {
            if (Altitude(y, height) - 90.0 / height < LowestSkyAltitude)
            {
                return;
            }

            for (var x = 0; x < width; x++)
            {
                var o = photo.Offset(x, y);
                var lum = Luminance(photo, o);
                // Smooth: like both horizontal neighbours and at least one vertical one (so the
                // sky's last pixel above the ground still counts as sky).
                var left = Luminance(photo, photo.Offset((x + width - 1) % width, y));
                var right = Luminance(photo, photo.Offset((x + 1) % width, y));
                var above = y > 0 ? Luminance(photo, photo.Offset(x, y - 1)) : lum;
                var below = y + 1 < height ? Luminance(photo, photo.Offset(x, y + 1)) : lum;
                var blue = photo.Rgba[o + 2] - Math.Max(photo.Rgba[o], photo.Rgba[o + 1] * 0.85);
                skyLike[y * width + x] = blue > skyBlueMinimum && lum > MinimumSkyLuminance
                    && Math.Max(Math.Abs(left - lum), Math.Abs(right - lum)) < MaximumSkyRoughness
                    && Math.Min(Math.Abs(above - lum), Math.Abs(below - lum)) < MaximumSkyRoughness;
                clearSky[y * width + x] = skyLike[y * width + x] && blue > skyBlueMinimum + HoleBlueMargin;
            }
        });

        // The open sky: sky-like pixels connected to the top row.
        var sky = Fill(width, height, Enumerable.Range(0, width), i => skyLike[i]);

        // The ground: the rest, connected to the bottom row.
        return Fill(width, height, Enumerable.Range((height - 1) * width, width),
            i => !sky[i] && (!cutHoles || !clearSky[i]));
    }

    /// <summary>4-connected flood fill (wrapping in x) from <paramref name="seeds"/> through the pixels <paramref name="passable"/> accepts.</summary>
    private static bool[] Fill(int width, int height, IEnumerable<int> seeds, Func<int, bool> passable)
    {
        var reached = new bool[width * height];
        var queue = new Queue<int>();
        foreach (var seed in seeds)
        {
            Visit(seed);
        }

        while (queue.TryDequeue(out var i))
        {
            var x = i % width;
            var y = i / width;
            if (y > 0)
            {
                Visit(i - width);
            }

            if (y + 1 < height)
            {
                Visit(i + width);
            }

            Visit(y * width + (x + 1) % width);
            Visit(y * width + (x + width - 1) % width);
        }

        return reached;

        void Visit(int j)
        {
            if (!reached[j] && passable(j))
            {
                reached[j] = true;
                queue.Enqueue(j);
            }
        }
    }

    /// <summary>Ground pixels within <paramref name="distance"/> px (a square) of the sky.</summary>
    private static bool[] Outline(bool[] ground, int width, int height, int distance)
    {
        var outline = new bool[ground.Length];
        Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                if (!ground[y * width + x])
                {
                    continue;
                }

                for (var dy = -distance; dy <= distance && !outline[y * width + x]; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    for (var dx = -distance; dx <= distance; dx++)
                    {
                        if (!ground[ny * width + ((x + dx) % width + width) % width])
                        {
                            outline[y * width + x] = true;
                            break;
                        }
                    }
                }
            }
        });
        return outline;
    }

    /// <summary>The altitude of the top edge of row <paramref name="row"/> of an equirectangular image.</summary>
    public static double Altitude(int row, int height) => 90 - row * 180.0 / height;

    private static double Luminance(Raster photo, int o) =>
        0.2126 * photo.Rgba[o] + 0.7152 * photo.Rgba[o + 1] + 0.0722 * photo.Rgba[o + 2];

    private static (byte R, byte G, byte B) Pixel(Raster photo, int o) => (photo.Rgba[o], photo.Rgba[o + 1], photo.Rgba[o + 2]);

    /// <summary>
    /// An outline pixel mixes ground and the photo's sky; it takes the mean colour of the ground
    /// pixels around it that are not on the outline; thin things (wires, twigs) have none and are
    /// darkened instead, as they look against the sky.
    /// </summary>
    private static (byte R, byte G, byte B) InteriorColour(Raster photo, bool[] ground, bool[] outline, int x, int y)
    {
        const int Radius = OutlineWidth + 2;
        int r = 0, g = 0, b = 0, n = 0;
        for (var dy = -Radius; dy <= Radius; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= photo.Height)
            {
                continue;
            }

            for (var dx = -Radius; dx <= Radius; dx++)
            {
                var nx = ((x + dx) % photo.Width + photo.Width) % photo.Width;
                var i = ny * photo.Width + nx;
                if (ground[i] && !outline[i])
                {
                    var o = photo.Offset(nx, ny);
                    r += photo.Rgba[o];
                    g += photo.Rgba[o + 1];
                    b += photo.Rgba[o + 2];
                    n++;
                }
            }
        }

        if (n > 0)
        {
            return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }

        var (pr, pg, pb) = Pixel(photo, photo.Offset(x, y));
        return ((byte)(pr * ThinShade), (byte)(pg * ThinShade), (byte)(pb * ThinShade));
    }
}
