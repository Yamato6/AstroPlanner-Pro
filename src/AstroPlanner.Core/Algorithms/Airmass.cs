namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Relative optical air mass and atmospheric extinction.
/// </summary>
public static class Airmass
{
    /// <summary>Default V-band extinction coefficient (magnitudes per air mass) of a good site.</summary>
    public const double DefaultExtinctionCoefficient = 0.25;

    /// <summary>
    /// Relative air mass for an object at <paramref name="altitudeDegrees"/> (Kasten &amp;
    /// Young 1989, Applied Optics 28, 4735): X = 1 / (sin h + 0.50572 (h + 6.07995°)^-1.6364).
    /// It is ~1 at the zenith and ~38 at the horizon. Returns positive infinity below
    /// -6.07995°, where the formula is undefined (the object is far below the horizon).
    /// </summary>
    public static double KastenYoung(double altitudeDegrees)
    {
        var shifted = altitudeDegrees + 6.07995;
        if (double.IsNaN(altitudeDegrees) || shifted <= 0)
        {
            return double.PositiveInfinity;
        }

        var altitudeRadians = altitudeDegrees * Math.PI / 180.0;
        var denominator = Math.Sin(altitudeRadians) + 0.50572 * Math.Pow(shifted, -1.6364);
        return denominator <= 0 ? double.PositiveInfinity : 1.0 / denominator;
    }
}
