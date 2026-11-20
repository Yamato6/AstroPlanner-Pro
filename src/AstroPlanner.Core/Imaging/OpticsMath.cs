namespace AstroPlanner.Core.Imaging;

/// <summary>Angular size of a camera's field, degrees.</summary>
public readonly record struct FieldOfView(double WidthDegrees, double HeightDegrees);

/// <summary>Geometry of a camera behind a lens or telescope (spec §8.2).</summary>
public static class OpticsMath
{
    /// <summary>Arcseconds per radian / 1000, so that µm / mm gives arcseconds.</summary>
    private const double ArcsecPerRadianOver1000 = 206.264806;

    /// <summary>Field of view across a sensor side: 2·atan(W / 2f), degrees.</summary>
    public static double FieldOfViewDegrees(double sensorSideMm, double focalLengthMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sensorSideMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(focalLengthMm);
        return 2 * double.RadiansToDegrees(Math.Atan(sensorSideMm / (2 * focalLengthMm)));
    }

    /// <summary>Field of view of a whole sensor.</summary>
    public static FieldOfView FieldOfView(double sensorWidthMm, double sensorHeightMm, double focalLengthMm) =>
        new(FieldOfViewDegrees(sensorWidthMm, focalLengthMm), FieldOfViewDegrees(sensorHeightMm, focalLengthMm));

    /// <summary>Image scale s = 206.265 · p[µm] / f[mm], arcseconds per pixel.</summary>
    public static double ImageScaleArcsecPerPixel(double pixelSizeUm, double focalLengthMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSizeUm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(focalLengthMm);
        return ArcsecPerRadianOver1000 * pixelSizeUm / focalLengthMm;
    }
}
