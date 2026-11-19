using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Precession of equatorial coordinates between two equinoxes (Meeus, "Astronomical
/// Algorithms", ch. 21, rigorous method). Without it catalog coordinates are off by ~0.36° in
/// 2026. Nutation and aberration (&lt; 30") are not included; they are irrelevant for planning.
/// </summary>
public static class Precession
{
    /// <summary>Julian date of the standard equinox J2000.0.</summary>
    public const double J2000JulianDate = 2451545.0;

    /// <summary>Julian date of the Besselian equinox B1875.0, used by the constellation boundaries.</summary>
    public const double B1875JulianDate = 2405889.258550475;

    /// <summary>Julian date of the Besselian equinox B1950.0.</summary>
    public const double B1950JulianDate = 2433282.4235;

    /// <summary>Precesses J2000.0 coordinates to the equinox of <paramref name="utcDateTime"/>.</summary>
    public static EquatorialCoordinates FromJ2000(EquatorialCoordinates j2000, DateTime utcDateTime) =>
        Precess(j2000, J2000JulianDate, AstronomyMath.ToJulianDate(utcDateTime));

    /// <summary>
    /// Precesses <paramref name="coordinates"/> from the equinox <paramref name="fromJulianDate"/>
    /// to the equinox <paramref name="toJulianDate"/> (Meeus 21.2 and 21.4).
    /// </summary>
    public static EquatorialCoordinates Precess(
        EquatorialCoordinates coordinates,
        double fromJulianDate,
        double toJulianDate)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        var (zeta, z, theta) = Angles(fromJulianDate, toJulianDate);

        var alpha0 = AstronomyMath.DegreesToRadiansValue(coordinates.RightAscensionHours * 15.0 + zeta);
        var delta0 = AstronomyMath.DegreesToRadiansValue(coordinates.DeclinationDegrees);
        var thetaRad = AstronomyMath.DegreesToRadiansValue(theta);

        var a = Math.Cos(delta0) * Math.Sin(alpha0);
        var b = Math.Cos(thetaRad) * Math.Cos(delta0) * Math.Cos(alpha0) - Math.Sin(thetaRad) * Math.Sin(delta0);
        var c = Math.Sin(thetaRad) * Math.Cos(delta0) * Math.Cos(alpha0) + Math.Cos(thetaRad) * Math.Sin(delta0);

        var alpha = AstronomyMath.RadiansToDegreesValue(Math.Atan2(a, b)) + z;
        var delta = AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(c, -1.0, 1.0)));

        return new EquatorialCoordinates(AstronomyMath.NormalizeDegrees(alpha) / 15.0, delta);
    }

    /// <summary>
    /// The same precession as a matrix acting on unit vectors (x toward the equinox, z toward the
    /// pole): <c>P = Rz(z) · Ry(−θ) · Rz(ζ)</c>, equivalent to <see cref="Precess"/>.
    /// </summary>
    public static Matrix3 Matrix(double fromJulianDate, double toJulianDate)
    {
        var (zeta, z, theta) = Angles(fromJulianDate, toJulianDate);
        return Matrix3.RotationZ(z) * Matrix3.RotationY(-theta) * Matrix3.RotationZ(zeta);
    }

    /// <summary>Precession angles ζ, z, θ in degrees (Meeus 21.2).</summary>
    private static (double Zeta, double Z, double Theta) Angles(double fromJulianDate, double toJulianDate)
    {
        // T: from J2000 to the starting equinox; t: from the starting to the final equinox
        // (Julian centuries).
        var bigT = (fromJulianDate - J2000JulianDate) / 36525.0;
        var bigT2 = bigT * bigT;
        var t = (toJulianDate - fromJulianDate) / 36525.0;
        var t2 = t * t;
        var t3 = t2 * t;

        // Precession angles in arcseconds → degrees (Meeus 21.2).
        var common = 2306.2181 + 1.39656 * bigT - 0.000139 * bigT2;
        var zeta = (common * t + (0.30188 - 0.000344 * bigT) * t2 + 0.017998 * t3) / 3600.0;
        var z = (common * t + (1.09468 + 0.000066 * bigT) * t2 + 0.018203 * t3) / 3600.0;
        var theta = ((2004.3109 - 0.85330 * bigT - 0.000217 * bigT2) * t
                     - (0.42665 + 0.000217 * bigT) * t2 - 0.041833 * t3) / 3600.0;

        return (zeta, z, theta);
    }
}
