using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Planet positions from JPL's Keplerian orbital elements ("Approximate Positions of the
/// Planets", E. M. Standish, table 1, valid 1800-2050). Typical error: ~1' for the inner
/// planets, up to ~10' for Jupiter and Saturn; enough for planning. Magnitudes and phase
/// after Meeus, "Astronomical Algorithms", ch. 41; diameters, ch. 55.
/// </summary>
public static class PlanetCalculator
{
    /// <summary>Light time for 1 AU, in days.</summary>
    internal const double LightTimeDaysPerAu = 0.0057755183;

    /// <summary>Obliquity of the J2000 ecliptic (degrees).</summary>
    private const double ObliquityJ2000 = 23.43928;

    /// <summary>
    /// Elements at J2000 and their rate per century: semi-major axis (AU), eccentricity,
    /// inclination, mean longitude, longitude of perihelion, longitude of the node (degrees).
    /// </summary>
    private sealed record OrbitalElements(
        double A, double E, double I, double L, double Perihelion, double Node,
        double ARate, double ERate, double IRate, double LRate, double PerihelionRate, double NodeRate);

    private static readonly OrbitalElements Mercury = new(
        0.38709927, 0.20563593, 7.00497902, 252.25032350, 77.45779628, 48.33076593,
        0.00000037, 0.00001906, -0.00594749, 149472.67411175, 0.16047689, -0.12534081);

    private static readonly OrbitalElements Venus = new(
        0.72333566, 0.00677672, 3.39467605, 181.97909950, 131.60246718, 76.67984255,
        0.00000390, -0.00004107, -0.00078890, 58517.81538729, 0.00268329, -0.27769418);

    private static readonly OrbitalElements EarthMoonBarycenter = new(
        1.00000261, 0.01671123, -0.00001531, 100.46457166, 102.93768193, 0.0,
        0.00000562, -0.00004392, -0.01294668, 35999.37244981, 0.32327364, 0.0);

    private static readonly OrbitalElements Mars = new(
        1.52371034, 0.09339410, 1.84969142, -4.55343205, -23.94362959, 49.55953891,
        0.00001847, 0.00007882, -0.00813131, 19140.30268499, 0.44441088, -0.29257343);

    private static readonly OrbitalElements Jupiter = new(
        5.20288700, 0.04838624, 1.30439695, 34.39644051, 14.72847983, 100.47390909,
        -0.00011607, -0.00013253, -0.00183714, 3034.74612775, 0.21252668, 0.20469106);

    private static readonly OrbitalElements Saturn = new(
        9.53667594, 0.05386179, 2.48599187, 49.95424423, 92.59887831, 113.66242448,
        -0.00125060, -0.00050991, 0.00193609, 1222.49362201, -0.41897216, -0.28867794);

    private static readonly OrbitalElements Uranus = new(
        19.18916464, 0.04725744, 0.77263783, 313.23810451, 170.95427630, 74.01692503,
        -0.00196176, -0.00004397, -0.00242939, 428.48202785, 0.40805281, 0.04240589);

    private static readonly OrbitalElements Neptune = new(
        30.06992276, 0.00859048, 1.77004347, -55.12002969, 44.96476227, 131.78422574,
        0.00026291, 0.00005105, 0.00035372, 218.45945325, -0.32241464, -0.00508664);

    private static OrbitalElements ElementsOf(Planet planet) => planet switch
    {
        Planet.Mercury => Mercury,
        Planet.Venus => Venus,
        Planet.Mars => Mars,
        Planet.Jupiter => Jupiter,
        Planet.Saturn => Saturn,
        Planet.Uranus => Uranus,
        Planet.Neptune => Neptune,
        _ => throw new ArgumentOutOfRangeException(nameof(planet))
    };

    /// <summary>Equatorial diameter at 1 AU, in arcseconds (Meeus, ch. 55).</summary>
    private static double DiameterAtOneAu(Planet planet) => planet switch
    {
        Planet.Mercury => 6.72,
        Planet.Venus => 16.82,
        Planet.Mars => 9.36,
        Planet.Jupiter => 196.88,
        Planet.Saturn => 165.46,
        Planet.Uranus => 70.04,
        Planet.Neptune => 67.00,
        _ => throw new ArgumentOutOfRangeException(nameof(planet))
    };

    private static double Sin(double degrees) => Math.Sin(AstronomyMath.DegreesToRadiansValue(degrees));
    private static double Cos(double degrees) => Math.Cos(AstronomyMath.DegreesToRadiansValue(degrees));

    public static PlanetPosition GetPosition(Planet planet, DateTime utcDateTime)
    {
        var jd = AstronomyMath.ToJulianDate(utcDateTime);
        var earth = Heliocentric(EarthMoonBarycenter, jd);

        // Light-time correction: the planet is seen where it was when the light left it.
        var position = Heliocentric(ElementsOf(planet), jd);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var delta = Length(Subtract(position, earth));
            position = Heliocentric(ElementsOf(planet), jd - delta * LightTimeDaysPerAu);
        }

        var geocentric = Subtract(position, earth);
        var distance = Length(geocentric);
        var sunDistance = Length(position);
        var earthSunDistance = Length(earth);

        // J2000 ecliptic → J2000 equatorial → equinox of date.
        var ofDate = Precession.FromJ2000(EclipticToEquatorialJ2000(geocentric), utcDateTime);

        var phaseAngle = AngleFromSides(sunDistance, distance, earthSunDistance);
        var elongation = AngleFromSides(earthSunDistance, distance, sunDistance);
        var magnitude = Magnitude(planet, sunDistance, distance, phaseAngle, geocentric, jd);

        return new PlanetPosition(
            planet,
            ofDate,
            distance,
            sunDistance,
            elongation,
            phaseAngle,
            (1 + Cos(phaseAngle)) / 2,
            magnitude,
            DiameterAtOneAu(planet) / distance);
    }

    /// <summary>
    /// Visual magnitude (Meeus 41, Astronomical Almanac formulae). For Saturn the brightness of
    /// the rings is included according to their tilt (Meeus 45).
    /// </summary>
    private static double Magnitude(
        Planet planet, double r, double delta, double i, (double X, double Y, double Z) geocentric, double jd)
    {
        var distanceTerm = 5 * Math.Log10(r * delta);
        return planet switch
        {
            Planet.Mercury => -0.42 + distanceTerm + 0.0380 * i - 0.000273 * i * i + 0.000002 * i * i * i,
            Planet.Venus => -4.40 + distanceTerm + 0.0009 * i + 0.000239 * i * i - 0.00000065 * i * i * i,
            Planet.Mars => -1.52 + distanceTerm + 0.016 * i,
            Planet.Jupiter => -9.40 + distanceTerm + 0.005 * i,
            Planet.Saturn => SaturnMagnitude(distanceTerm, geocentric, jd),
            Planet.Uranus => -7.19 + distanceTerm,
            Planet.Neptune => -6.87 + distanceTerm,
            _ => throw new ArgumentOutOfRangeException(nameof(planet))
        };
    }

    private static double SaturnMagnitude(double distanceTerm, (double X, double Y, double Z) geocentric, double jd)
    {
        var sinB = Math.Sin(AstronomyMath.DegreesToRadiansValue(SaturnRingTiltDegrees(geocentric, jd)));
        return -8.88 + distanceTerm - 2.60 * Math.Abs(sinB) + 1.25 * sinB * sinB;
    }

    /// <summary>
    /// Tilt of Saturn's rings as seen from the Earth (B, Meeus 45): near 0° the rings are seen
    /// edge-on and Saturn is fainter.
    /// </summary>
    internal static double SaturnRingTiltDegrees((double X, double Y, double Z) geocentric, double jd)
    {
        var t = (jd - 2451545.0) / 36525.0;
        var inclination = 28.075216 - 0.012998 * t + 0.000004 * t * t;
        var node = 169.508470 + 1.394681 * t + 0.000412 * t * t;

        // Geocentric ecliptic longitude and latitude; the longitude is referred to the equinox of
        // date by adding the general precession (~1.397° per century).
        var lambda = AstronomyMath.RadiansToDegreesValue(Math.Atan2(geocentric.Y, geocentric.X)) + 1.3969713 * t;
        var beta = AstronomyMath.RadiansToDegreesValue(
            Math.Atan2(geocentric.Z, Math.Sqrt(geocentric.X * geocentric.X + geocentric.Y * geocentric.Y)));

        var sinB = Sin(inclination) * Cos(beta) * Sin(lambda - node) - Cos(inclination) * Sin(beta);
        return AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(sinB, -1.0, 1.0)));
    }

    /// <summary>Heliocentric position of the Earth (Earth-Moon barycentre) in the J2000 ecliptic (AU).</summary>
    internal static (double X, double Y, double Z) EarthHeliocentric(double jd) => Heliocentric(EarthMoonBarycenter, jd);

    /// <summary>Direction of a J2000 ecliptic vector as J2000 equatorial coordinates.</summary>
    internal static EquatorialCoordinates EclipticToEquatorialJ2000((double X, double Y, double Z) v)
    {
        var epsilon = AstronomyMath.DegreesToRadiansValue(ObliquityJ2000);
        var x = v.X;
        var y = v.Y * Math.Cos(epsilon) - v.Z * Math.Sin(epsilon);
        var z = v.Y * Math.Sin(epsilon) + v.Z * Math.Cos(epsilon);
        var length = Math.Sqrt(x * x + y * y + z * z);
        return new EquatorialCoordinates(
            AstronomyMath.NormalizeDegrees(AstronomyMath.RadiansToDegreesValue(Math.Atan2(y, x))) / 15.0 % 24,
            AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(z / length, -1.0, 1.0))));
    }

    /// <summary>A point of the orbital plane (x towards perihelion) rotated to the J2000 ecliptic.</summary>
    internal static (double X, double Y, double Z) RotateToEcliptic(
        double xOrbit, double yOrbit, double argumentOfPerihelion, double node, double inclination)
    {
        var cw = Cos(argumentOfPerihelion);
        var sw = Sin(argumentOfPerihelion);
        var cn = Cos(node);
        var sn = Sin(node);
        var ci = Cos(inclination);
        var si = Sin(inclination);

        return (
            (cw * cn - sw * sn * ci) * xOrbit + (-sw * cn - cw * sn * ci) * yOrbit,
            (cw * sn + sw * cn * ci) * xOrbit + (-sw * sn + cw * cn * ci) * yOrbit,
            sw * si * xOrbit + cw * si * yOrbit);
    }

    /// <summary>Heliocentric position in the J2000 ecliptic (AU).</summary>
    private static (double X, double Y, double Z) Heliocentric(OrbitalElements el, double jd)
    {
        var t = (jd - 2451545.0) / 36525.0;
        var a = el.A + el.ARate * t;
        var e = el.E + el.ERate * t;
        var inclination = el.I + el.IRate * t;
        var meanLongitude = el.L + el.LRate * t;
        var perihelion = el.Perihelion + el.PerihelionRate * t;
        var node = el.Node + el.NodeRate * t;

        var argumentOfPerihelion = perihelion - node;
        var meanAnomaly = AstronomyMath.NormalizeDegrees(meanLongitude - perihelion);
        if (meanAnomaly > 180)
        {
            meanAnomaly -= 360;
        }

        // Kepler's equation by Newton-Raphson (in radians).
        var m = AstronomyMath.DegreesToRadiansValue(meanAnomaly);
        var eccentricAnomaly = m + e * Math.Sin(m);
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var step = (eccentricAnomaly - e * Math.Sin(eccentricAnomaly) - m) / (1 - e * Math.Cos(eccentricAnomaly));
            eccentricAnomaly -= step;
            if (Math.Abs(step) < 1e-12)
            {
                break;
            }
        }

        // Coordinates in the orbital plane and rotation to the ecliptic.
        var xOrbit = a * (Math.Cos(eccentricAnomaly) - e);
        var yOrbit = a * Math.Sqrt(1 - e * e) * Math.Sin(eccentricAnomaly);
        return RotateToEcliptic(xOrbit, yOrbit, argumentOfPerihelion, node, inclination);
    }

    private static (double X, double Y, double Z) Subtract((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Length((double X, double Y, double Z) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    /// <summary>Angle (degrees) opposite the side <paramref name="opposite"/>, by the law of cosines.</summary>
    internal static double AngleFromSides(double adjacent1, double adjacent2, double opposite)
    {
        var cosine = (adjacent1 * adjacent1 + adjacent2 * adjacent2 - opposite * opposite) / (2 * adjacent1 * adjacent2);
        return AstronomyMath.RadiansToDegreesValue(Math.Acos(Math.Clamp(cosine, -1.0, 1.0)));
    }
}
