namespace AstroPlanner.Core.SkyQuality;

/// <summary>
/// Brightness of scattered moonlight, Krisciunas &amp; Schaefer (1991), "A model of the brightness
/// of moonlight", PASP 103, 1033 (V band, fitted to Mauna Kea data; quoted accuracy 8–23 %,
/// worse near full Moon). Brightnesses are in nanolamberts (nL) as in the paper, with its own
/// conversion to V mag/arcsec².
/// </summary>
public static class KrisciunasSchaefer
{
    /// <summary>Extinction coefficient the paper used for Mauna Kea, mag/air mass.</summary>
    public const double MaunaKeaExtinction = 0.172;

    /// <summary>
    /// Air mass of the paper for scattering paths: X(Z) = (1 − 0.96 sin² Z)^−½. It stays
    /// finite at the horizon (≈ 5), unlike sec Z.
    /// </summary>
    public static double Airmass(double zenithDistanceDegrees)
    {
        var sine = Math.Sin(double.DegreesToRadians(zenithDistanceDegrees));
        return 1 / Math.Sqrt(1 - 0.96 * sine * sine);
    }

    /// <summary>
    /// Illuminance of the Moon outside the atmosphere, in the paper's units (V magnitude
    /// of the Moon m = −12.73 + 0.026|α| + 4·10⁻⁹ α⁴): I* = 10^(−0.4 (3.84 + 0.026|α| + 4·10⁻⁹ α⁴)),
    /// with α the phase angle in degrees (0 = full).
    /// </summary>
    public static double MoonIlluminance(double phaseAngleDegrees)
    {
        var alpha = Math.Abs(phaseAngleDegrees);
        return Math.Pow(10, -0.4 * (3.84 + 0.026 * alpha + 4e-9 * Math.Pow(alpha, 4)));
    }

    /// <summary>
    /// Scattering function: Rayleigh 10^5.36 (1.06 + cos² ρ) plus Mie 10^(6.15 − ρ/40),
    /// with ρ the Moon–sky separation in degrees.
    /// </summary>
    public static double ScatteringFunction(double separationDegrees)
    {
        var cosine = Math.Cos(double.DegreesToRadians(separationDegrees));
        return Math.Pow(10, 5.36) * (1.06 + cosine * cosine) + Math.Pow(10, 6.15 - separationDegrees / 40);
    }

    /// <summary>
    /// Moonlight at a sky position, nL:
    /// B = f(ρ) · I* · 10^(−0.4 k X(Z_moon)) · (1 − 10^(−0.4 k X(Z))). Zero when the Moon is
    /// below the horizon.
    /// </summary>
    /// <param name="phaseAngleDegrees">Lunar phase angle α (0 = full Moon, 180 = new).</param>
    /// <param name="separationDegrees">Angle ρ between the Moon and the sky position.</param>
    /// <param name="moonZenithDistanceDegrees">Zenith distance of the Moon.</param>
    /// <param name="skyZenithDistanceDegrees">Zenith distance of the sky position.</param>
    /// <param name="extinction">V extinction coefficient k (mag/air mass).</param>
    public static double MoonlightNanolamberts(
        double phaseAngleDegrees,
        double separationDegrees,
        double moonZenithDistanceDegrees,
        double skyZenithDistanceDegrees,
        double extinction)
    {
        if (moonZenithDistanceDegrees >= 90)
        {
            return 0;
        }

        var sky = Math.Min(skyZenithDistanceDegrees, 90);
        return ScatteringFunction(separationDegrees)
               * MoonIlluminance(phaseAngleDegrees)
               * Math.Pow(10, -0.4 * extinction * Airmass(moonZenithDistanceDegrees))
               * (1 - Math.Pow(10, -0.4 * extinction * Airmass(sky)));
    }

    /// <summary>The paper's conversion inverted: V mag/arcsec² of a brightness in nL.</summary>
    public static double MagnitudeFromNanolamberts(double nanolamberts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(nanolamberts);
        return (20.7233 - Math.Log(nanolamberts / 34.08)) / 0.92104;
    }

    /// <summary>The paper's conversion: brightness in nL of V mag/arcsec², B = 34.08 · exp(20.7233 − 0.92104 V).</summary>
    public static double NanolambertsFromMagnitude(double magnitudesPerArcsec2) =>
        34.08 * Math.Exp(20.7233 - 0.92104 * magnitudesPerArcsec2);
}
