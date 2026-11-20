namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Atmospheric refraction, Saemundsson's formula (Meeus, ch. 16), from true to apparent altitude.
/// The formula is for 10 °C and 1010 hPa (~34' at the horizon, ~1' at 45°, 0 at the zenith);
/// Meeus scales it by (P / 1010) · (283 / (273 + T)). Here the pressure comes from the site's
/// elevation (standard atmosphere, relative to sea level), the temperature stays at 10 °C.
/// </summary>
public static class Refraction
{
    /// <summary>
    /// Below this value the formula is no longer valid; the object is below the horizon anyway
    /// and the uncorrected altitude is returned.
    /// </summary>
    private const double MinimumAltitudeDegrees = -2.0;

    /// <summary>
    /// Scale of the refraction at an elevation: P(h) / P(0) of the standard atmosphere,
    /// (1 − 2.25577·10⁻⁵ h)^5.25588. 1 at sea level, ≈ 0.80 at 1888 m (Aguascalientes).
    /// </summary>
    public static double PressureScale(double elevationMeters)
    {
        if (!double.IsFinite(elevationMeters))
        {
            return 1;
        }

        var h = Math.Clamp(elevationMeters, -500, 9000);
        return Math.Pow(1 - 2.25577e-5 * h, 5.25588);
    }

    /// <summary>Correction (degrees, ≥ 0) to add to the true altitude.</summary>
    /// <param name="trueAltitudeDegrees">Geometric altitude.</param>
    /// <param name="scale">Pressure/temperature scale (<see cref="PressureScale"/>); 1 = 1010 hPa, 10 °C.</param>
    public static double CorrectionDegrees(double trueAltitudeDegrees, double scale = 1)
    {
        if (double.IsNaN(trueAltitudeDegrees) || trueAltitudeDegrees < MinimumAltitudeDegrees)
        {
            return 0;
        }

        var argument = AstronomyMath.DegreesToRadiansValue(
            trueAltitudeDegrees + 10.3 / (trueAltitudeDegrees + 5.11));

        // R in arcminutes; +0.0019279 makes R = 0 exactly at the zenith.
        var minutes = 1.02 / Math.Tan(argument) + 0.0019279;
        return Math.Max(0, scale * minutes / 60.0);
    }

    /// <summary>Apparent (observed) altitude from the true one.</summary>
    public static double ApparentAltitude(double trueAltitudeDegrees, double scale = 1) =>
        trueAltitudeDegrees + CorrectionDegrees(trueAltitudeDegrees, scale);
}
