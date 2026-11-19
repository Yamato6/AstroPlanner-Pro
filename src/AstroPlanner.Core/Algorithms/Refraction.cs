namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Standard atmospheric refraction (10 °C, 1010 hPa). The atmosphere lifts objects
/// apparently: ~34' at the horizon, ~1' at 45°, 0 at the zenith. Saemundsson's formula
/// (Meeus, ch. 16), from true to apparent altitude.
/// </summary>
public static class Refraction
{
    /// <summary>
    /// Below this value the formula is no longer valid; the object is below the horizon anyway
    /// and the uncorrected altitude is returned.
    /// </summary>
    private const double MinimumAltitudeDegrees = -2.0;

    /// <summary>Correction (degrees, ≥ 0) to add to the true altitude.</summary>
    public static double CorrectionDegrees(double trueAltitudeDegrees)
    {
        if (double.IsNaN(trueAltitudeDegrees) || trueAltitudeDegrees < MinimumAltitudeDegrees)
        {
            return 0;
        }

        var argument = AstronomyMath.DegreesToRadiansValue(
            trueAltitudeDegrees + 10.3 / (trueAltitudeDegrees + 5.11));

        // R in arcminutes; +0.0019279 makes R = 0 exactly at the zenith.
        var minutes = 1.02 / Math.Tan(argument) + 0.0019279;
        return Math.Max(0, minutes / 60.0);
    }

    /// <summary>Apparent (observed) altitude from the true one.</summary>
    public static double ApparentAltitude(double trueAltitudeDegrees) =>
        trueAltitudeDegrees + CorrectionDegrees(trueAltitudeDegrees);
}
