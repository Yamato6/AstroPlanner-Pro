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

    /// <summary>Seeing FWHM spread over this many pixels below which stars are undersampled.</summary>
    public const double UndersampledBelowPixels = 1.5;

    /// <summary>Seeing FWHM spread over this many pixels above which the image is oversampled.</summary>
    public const double OversampledAbovePixels = 4.0;

    /// <summary>
    /// How the pixels sample the seeing disc (spec §8.2): FWHM_px = seeing / s, undersampled below
    /// 1.5 px, oversampled above 4 px (rule-of-thumb thresholds, documented in feasibility-model.md).
    /// </summary>
    public static Sampling SamplingFor(double seeingArcsec, double imageScaleArcsecPerPixel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seeingArcsec);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageScaleArcsecPerPixel);
        var fwhmPixels = seeingArcsec / imageScaleArcsecPerPixel;
        var verdict = fwhmPixels < UndersampledBelowPixels ? SamplingVerdict.Undersampled
            : fwhmPixels > OversampledAbovePixels ? SamplingVerdict.Oversampled
            : SamplingVerdict.Good;
        return new Sampling(fwhmPixels, verdict);
    }

    /// <summary>Pixels spanned by an angle in arcminutes at an image scale.</summary>
    public static double Pixels(double arcmin, double imageScaleArcsecPerPixel) => arcmin * 60.0 / imageScaleArcsecPerPixel;
}

public enum SamplingVerdict
{
    Undersampled,
    Good,
    Oversampled
}

/// <summary>Seeing FWHM in pixels and what it means.</summary>
public readonly record struct Sampling(double FwhmPixels, SamplingVerdict Verdict);
