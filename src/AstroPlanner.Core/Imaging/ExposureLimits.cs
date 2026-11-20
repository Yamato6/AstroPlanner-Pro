using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Imaging;

/// <summary>Where the longest usable sub-exposure comes from.</summary>
public enum ExposureLimitSource
{
    /// <summary>Star trailing on an untracked tripod (NPF rule).</summary>
    Npf,
    /// <summary>The mount's tracking (user value or the default for its kind).</summary>
    Mount,
    /// <summary>The sky background swamps the read noise: longer subs add little.</summary>
    SkyLimited,
    /// <summary>The model's overall cap (FeasibilityConstants.MaximumSubSeconds).</summary>
    Cap
}

/// <summary>Maximum sub-exposure rules of spec §8.3.</summary>
public static class ExposureLimits
{
    /// <summary>Declination beyond which the NPF cos δ correction is clamped.</summary>
    public const double MaximumCorrectedDeclinationDegrees = 75;

    /// <summary>
    /// NPF rule (F. Michaud, Société Astronomique du Havre): t = (35·N + 30·p) / f seconds for
    /// pinpoint stars on a fixed tripod (N = f-number, p = pixel pitch µm, f = focal length mm).
    /// </summary>
    public static double Npf(double focalRatio, double pixelSizeUm, double focalLengthMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(focalRatio);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSizeUm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(focalLengthMm);
        return (35 * focalRatio + 30 * pixelSizeUm) / focalLengthMm;
    }

    /// <summary>
    /// NPF corrected for declination: stars near the pole move slower, t / cos δ, with δ clamped
    /// to ±75° (at most ×3.9).
    /// </summary>
    public static double NpfAtDeclination(double npfSeconds, double declinationDegrees)
    {
        var clamped = Math.Min(Math.Abs(declinationDegrees), MaximumCorrectedDeclinationDegrees);
        return npfSeconds / Math.Cos(double.DegreesToRadians(clamped));
    }

    /// <summary>The old "500 rule" (500 / (f · crop)), shown for reference only.</summary>
    public static double Rule500(double focalLengthMm, double cropFactor)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(focalLengthMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cropFactor);
        return 500 / (focalLengthMm * cropFactor);
    }

    /// <summary>
    /// Default longest unguided sub for a tracking mount when the user gave none (spec §8.3:
    /// star tracker 120 s ≤ 50 mm, 60 s ≤ 135 mm, …; guided EQ 300 s).
    /// </summary>
    public static double DefaultMountLimitSeconds(MountKind kind, double focalLengthMm) => kind switch
    {
        MountKind.StarTracker => focalLengthMm switch
        {
            <= 50 => 120,
            <= 135 => 60,
            <= 300 => 30,
            _ => 15
        },
        MountKind.AltAz => 30,
        MountKind.Equatorial => focalLengthMm <= 300 ? 120 : 60,
        MountKind.EquatorialGuided => 300,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "A fixed tripod has no tracking limit; use the NPF rule.")
    };

    /// <summary>
    /// Longest sub the rig can take on a target at <paramref name="declinationDegrees"/>: NPF
    /// (declination-corrected) on a fixed tripod, else the mount's value or its default.
    /// </summary>
    public static (double Seconds, ExposureLimitSource Source) MountLimit(RigSetup rig, double declinationDegrees)
    {
        ArgumentNullException.ThrowIfNull(rig);
        if (!rig.Mount.Tracks)
        {
            var npf = Npf(rig.FocalRatio, rig.PixelSizeUm, rig.FocalLengthMm);
            return (NpfAtDeclination(npf, declinationDegrees), ExposureLimitSource.Npf);
        }

        return (rig.Mount.MaxUnguidedExposureSeconds ?? DefaultMountLimitSeconds(rig.Mount.Kind, rig.FocalLengthMm), ExposureLimitSource.Mount);
    }

    /// <summary>
    /// Sky-limited sub (spec §8.3): the shortest t for which the sky background reaches
    /// 10·RN² electrons, so read noise is swamped. Infinity when the sky gives no electrons.
    /// </summary>
    public static double SkyLimitedSeconds(double skyElectronsPerSecondPerPixel, double readNoiseElectrons)
    {
        if (!(skyElectronsPerSecondPerPixel > 0))
        {
            return double.PositiveInfinity;
        }

        return 10 * readNoiseElectrons * readNoiseElectrons / skyElectronsPerSecondPerPixel;
    }

    private static readonly double[] NiceSeconds =
        [0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10, 13, 15, 20, 25, 30, 40, 45, 60, 90, 120, 150, 180, 240, 300, 360, 420, 480, 600];

    /// <summary>
    /// Rounds a sub length down to a value a camera offers (…, 8, 10, 13, 15, 20, 25, 30 s, …);
    /// below 0.5 s it is rounded down to 0.1 s (at least 0.1 s).
    /// </summary>
    public static double RoundDownToNice(double seconds)
    {
        if (!(seconds > 0))
        {
            return 0;
        }

        if (seconds < NiceSeconds[0])
        {
            return Math.Max(0.1, Math.Floor(seconds * 10) / 10);
        }

        var nice = NiceSeconds[0];
        foreach (var value in NiceSeconds)
        {
            if (value <= seconds + 1e-9)
            {
                nice = value;
            }
        }

        return nice;
    }
}
