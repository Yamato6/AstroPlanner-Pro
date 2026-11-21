namespace AstroPlanner.Core.Imaging.Feasibility;

/// <summary>
/// The user's calibration of the feasibility model (Settings › Advanced, spec §8.7): the
/// constants docs/feasibility-model.md §9 names as tunable. A null value keeps the default of
/// <see cref="FeasibilityConstants"/>; the constants themselves are never edited.
/// </summary>
public sealed record FeasibilityTuning
{
    /// <summary>Seeing FWHM used when there is no forecast, arcseconds.</summary>
    public double? SeeingArcsec { get; init; }

    /// <summary>How much brighter than their mean surface brightness extended objects are evaluated, magnitudes.</summary>
    public double? CoreBrighteningMagnitudes { get; init; }

    /// <summary>Time lost between subs (download, dither), seconds.</summary>
    public double? OverheadSeconds { get; init; }

    /// <summary>Longest sub the model suggests, seconds.</summary>
    public double? MaximumSubSeconds { get; init; }

    /// <summary>Stack SNR that makes a target Marginal.</summary>
    public double? SnrMarginal { get; init; }

    /// <summary>Stack SNR that makes a target Good.</summary>
    public double? SnrGood { get; init; }

    /// <summary>Stack SNR that makes a target Excellent.</summary>
    public double? SnrExcellent { get; init; }

    /// <summary>Pixels between the stars of a double for the camera to split them.</summary>
    public double? DoubleStarSplitPixels { get; init; }

    /// <summary>True when every constant keeps its default.</summary>
    public bool IsDefault => this == new FeasibilityTuning();

    /// <summary>The constants with the user's values in place of the defaults.</summary>
    public FeasibilityConstants Apply(FeasibilityConstants constants)
    {
        ArgumentNullException.ThrowIfNull(constants);
        return constants with
        {
            SeeingArcsec = SeeingArcsec ?? constants.SeeingArcsec,
            CoreBrighteningMagnitudes = CoreBrighteningMagnitudes ?? constants.CoreBrighteningMagnitudes,
            OverheadSeconds = OverheadSeconds ?? constants.OverheadSeconds,
            MaximumSubSeconds = MaximumSubSeconds ?? constants.MaximumSubSeconds,
            SnrMarginal = SnrMarginal ?? constants.SnrMarginal,
            SnrGood = SnrGood ?? constants.SnrGood,
            SnrExcellent = SnrExcellent ?? constants.SnrExcellent,
            DoubleStarSplitPixels = DoubleStarSplitPixels ?? constants.DoubleStarSplitPixels
        };
    }

    /// <summary>
    /// The tuning that gives <paramref name="values"/>: a value equal to its default is stored as
    /// null, so that a later change of the default reaches users who never touched it.
    /// </summary>
    public static FeasibilityTuning From(FeasibilityConstants values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var defaults = FeasibilityConstants.Default;
        return new FeasibilityTuning
        {
            SeeingArcsec = Changed(values.SeeingArcsec, defaults.SeeingArcsec),
            CoreBrighteningMagnitudes = Changed(values.CoreBrighteningMagnitudes, defaults.CoreBrighteningMagnitudes),
            OverheadSeconds = Changed(values.OverheadSeconds, defaults.OverheadSeconds),
            MaximumSubSeconds = Changed(values.MaximumSubSeconds, defaults.MaximumSubSeconds),
            SnrMarginal = Changed(values.SnrMarginal, defaults.SnrMarginal),
            SnrGood = Changed(values.SnrGood, defaults.SnrGood),
            SnrExcellent = Changed(values.SnrExcellent, defaults.SnrExcellent),
            DoubleStarSplitPixels = Changed(values.DoubleStarSplitPixels, defaults.DoubleStarSplitPixels)
        };
    }

    private static double? Changed(double value, double defaultValue) =>
        Math.Abs(value - defaultValue) < 1e-9 ? null : value;
}
