using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroPlanner.Core.Imaging;

namespace AstroPlanner.UI.Converters;

/// <summary>Shows an <see cref="RgbImage"/> (framing preview) as a frozen bitmap.</summary>
public sealed class RgbImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RgbImage image)
        {
            return null;
        }

        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Rgb24, null, image.Pixels, image.Width * 3);
        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows encoded image bytes (a Wikipedia thumbnail); null when they cannot be decoded.</summary>
public sealed class ImageBytesConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } content)
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(content, writable: false);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
