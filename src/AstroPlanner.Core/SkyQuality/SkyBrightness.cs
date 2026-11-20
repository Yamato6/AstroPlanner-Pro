namespace AstroPlanner.Core.SkyQuality;

/// <summary>
/// Night-sky brightness at the zenith, in magnitudes per square arcsecond (the unit of an SQM
/// meter: larger = darker). Conversions follow spec §8.4; the twilight and moonlight terms are a
/// <b>display heuristic</b> for the sky renderer (how washed-out the sky looks), not the
/// photometric model of the feasibility engine (Krisciunas &amp; Schaefer, M3).
/// </summary>
public static class SkyBrightness
{
    /// <summary>Natural zenith brightness of a pristine site (≈ 0.171 mcd/m², spec §8.4).</summary>
    public const double NaturalSqm = 22.0;

    /// <summary>Darkest plausible SQM reading and brightest city sky accepted as manual input.</summary>
    public const double MinimumSqm = 16.0;

    public const double MaximumSqm = 22.5;

    /// <summary>Full Moon at the zenith over a dark site, mag/arcsec² (display heuristic).</summary>
    private const double FullMoonAtZenithSqm = 18.0;

    /// <summary>Luminance in mcd/m² that corresponds to 0 mag/arcsec² (spec §8.4: 1.08·10⁸).</summary>
    private const double ZeroPointMillicandela = 1.08e8;

    /// <summary>
    /// Zenith brightness added by twilight against the Sun's altitude (degrees, mag/arcsec²):
    /// daylight, sunset, end of civil, nautical and astronomical twilight. Interpolated linearly;
    /// below −18° twilight adds nothing.
    /// </summary>
    private static readonly (double SunAltitude, double Sqm)[] TwilightTable =
    [
        (10, 4.0),
        (0, 7.0),
        (-6, 12.0),
        (-12, 17.5),
        (-18, 23.0)
    ];

    /// <summary>Natural zenith luminance used with the light-pollution atlas, mcd/m² (spec §8.4).</summary>
    public const double NaturalLuminance = 0.171;

    /// <summary>
    /// Zenith SQM of a site from the atlas' artificial brightness (mcd/m²):
    /// −2.5·log10((L_artificial + 0.171) / 1.08·10⁸), clamped to the accepted SQM range.
    /// </summary>
    public static double SqmFromArtificial(double artificialLuminance) =>
        Math.Clamp(SqmFromLuminance(Math.Max(0, artificialLuminance) + NaturalLuminance), MinimumSqm, MaximumSqm);

    /// <summary>Luminance (mcd/m²) of a sky of <paramref name="sqm"/> mag/arcsec².</summary>
    public static double LuminanceFromSqm(double sqm) => ZeroPointMillicandela * Math.Pow(10, -0.4 * sqm);

    /// <summary>SQM (mag/arcsec²) of a luminance in mcd/m²: −2.5·log10(L / 1.08·10⁸).</summary>
    public static double SqmFromLuminance(double luminanceMillicandela)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(luminanceMillicandela);
        return -2.5 * Math.Log10(luminanceMillicandela / ZeroPointMillicandela);
    }

    /// <summary>Adds sky brightnesses in linear units (each in mag/arcsec²; infinity = nothing).</summary>
    public static double Combine(params ReadOnlySpan<double> sqms)
    {
        var flux = 0.0;
        foreach (var sqm in sqms)
        {
            if (!double.IsPositiveInfinity(sqm))
            {
                flux += Math.Pow(10, -0.4 * sqm);
            }
        }

        return flux > 0 ? -2.5 * Math.Log10(flux) : double.PositiveInfinity;
    }

    /// <summary>
    /// Approximate naked-eye limiting magnitude at the zenith for a sky of <paramref name="sqm"/>:
    /// NELM = 7.93 − 5·log10(10^(4.316 − SQM/5) + 1) (Schaefer 1990, as used by Unihedron's SQM
    /// conversion). Gives ≈ 6.4 at 21.5 and ≈ 4.4 at 18.5.
    /// </summary>
    public static double NakedEyeLimitingMagnitude(double sqm) =>
        7.93 - 5 * Math.Log10(Math.Pow(10, 4.316 - sqm / 5) + 1);

    /// <summary>
    /// Bortle class for display only (spec §8.4: approximate SQM ranges, not a measurement):
    /// 1 ≥ 21.99, 2 ≥ 21.89, 3 ≥ 21.69, 4 ≥ 20.49, 5 ≥ 19.50, 6 ≥ 18.94, 7 ≥ 18.38, 8 ≥ 17.80, else 9.
    /// </summary>
    public static int BortleFromSqm(double sqm) => sqm switch
    {
        >= 21.99 => 1,
        >= 21.89 => 2,
        >= 21.69 => 3,
        >= 20.49 => 4,
        >= 19.50 => 5,
        >= 18.94 => 6,
        >= 18.38 => 7,
        >= 17.80 => 8,
        _ => 9
    };

    /// <summary>Zenith brightness added by twilight, mag/arcsec² (infinity below −18°).</summary>
    public static double Twilight(double sunAltitudeDegrees)
    {
        if (sunAltitudeDegrees <= TwilightTable[^1].SunAltitude)
        {
            return double.PositiveInfinity;
        }

        if (sunAltitudeDegrees >= TwilightTable[0].SunAltitude)
        {
            return TwilightTable[0].Sqm;
        }

        for (var i = 1; i < TwilightTable.Length; i++)
        {
            var (upperAltitude, upperSqm) = TwilightTable[i - 1];
            var (lowerAltitude, lowerSqm) = TwilightTable[i];
            if (sunAltitudeDegrees >= lowerAltitude)
            {
                var t = (sunAltitudeDegrees - lowerAltitude) / (upperAltitude - lowerAltitude);
                return lowerSqm + t * (upperSqm - lowerSqm);
            }
        }

        return double.PositiveInfinity;
    }

    /// <summary>
    /// Zenith brightness added by the Moon, mag/arcsec² (infinity when it is down): a full Moon at
    /// the zenith gives <see cref="FullMoonAtZenithSqm"/>; it fades with the Moon's brightness
    /// against its phase angle (the same Allen relation as its magnitude) and with sin(altitude).
    /// </summary>
    public static double Moonlight(double moonAltitudeDegrees, double phaseAngleDegrees)
    {
        if (moonAltitudeDegrees <= 0)
        {
            return double.PositiveInfinity;
        }

        var i = Math.Abs(phaseAngleDegrees);
        var dimming = 0.026 * i + 4e-9 * Math.Pow(i, 4);   // magnitudes fainter than full
        var relative = Math.Pow(10, -0.4 * dimming) * Math.Sin(double.DegreesToRadians(moonAltitudeDegrees));
        return FullMoonAtZenithSqm - 2.5 * Math.Log10(relative);
    }

    /// <summary>
    /// Zenith brightness for the renderer: the site's dark-sky baseline (manual SQM, else a
    /// natural sky) plus twilight and moonlight, in linear units.
    /// </summary>
    public static double Zenith(double? siteSqm, double sunAltitudeDegrees, double moonAltitudeDegrees, double moonPhaseAngleDegrees) =>
        Combine(siteSqm ?? NaturalSqm, Twilight(sunAltitudeDegrees), Moonlight(moonAltitudeDegrees, moonPhaseAngleDegrees));
}
