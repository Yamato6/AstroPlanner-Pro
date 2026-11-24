using AstroPlanner.Core.Imaging;

namespace AstroPlanner.Application.Services;

/// <summary>Decodes images (survey tiles are JPEG) to RGB pixels for <see cref="FramingPlan.Compose"/>.</summary>
public interface IImageDecoder
{
    /// <exception cref="NotSupportedException">Not a decodable image.</exception>
    RgbImage Decode(byte[] content);
}
