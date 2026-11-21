using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroPlanner.Core.Imaging;

namespace AstroPlanner.UI.Services;

/// <summary>Decodes images (survey tiles are JPEG) to RGB pixels for <see cref="FramingPlan.Compose"/>.</summary>
public interface IImageDecoder
{
    /// <exception cref="NotSupportedException">Not a decodable image.</exception>
    RgbImage Decode(byte[] content);
}

/// <summary>WPF imaging (WIC): safe on any thread, the bitmap never leaves it.</summary>
public sealed class WpfImageDecoder : IImageDecoder
{
    public RgbImage Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var rgb = new FormatConvertedBitmap(frame, PixelFormats.Rgb24, null, 0);
            var image = new RgbImage(rgb.PixelWidth, rgb.PixelHeight);
            rgb.CopyPixels(image.Pixels, rgb.PixelWidth * 3, 0);
            return image;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or ArgumentException or IOException)
        {
            throw new NotSupportedException("Not a valid image.", ex);
        }
    }
}
