using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Positions of comets and asteroids from osculating elements (two-body motion; Meeus,
/// "Astronomical Algorithms", ch. 30, 33, 34, 35). Kepler's equation is solved for elliptic
/// (e &lt; 1), parabolic (e = 1, Barker's equation) and hyperbolic (e &gt; 1) orbits with a
/// safeguarded Newton iteration, so near-parabolic comets converge too. The Earth comes from
/// <see cref="PlanetCalculator"/>; light time is iterated twice. Without perturbations the error
/// grows away from the elements' epoch (arcseconds to a few arcminutes over months; see the
/// Horizons tests).
/// </summary>
public static class OrbitCalculator
{
    /// <summary>|e − 1| below which the orbit is treated as parabolic.</summary>
    private const double ParabolicTolerance = 1e-7;

    /// <summary>Heliocentric position in the J2000 ecliptic (AU) at <paramref name="julianDate"/>.</summary>
    public static (double X, double Y, double Z) Heliocentric(OrbitalElements elements, double julianDate)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var (x, y) = InPlane(elements, julianDate - elements.PerihelionJulianDate);
        return PlanetCalculator.RotateToEcliptic(x, y,
            elements.ArgumentOfPerihelionDegrees, elements.AscendingNodeDegrees, elements.InclinationDegrees);
    }

    public static SmallBodyPosition GetPosition(SmallBodyOrbit orbit, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        var jd = AstronomyMath.ToJulianDate(utcDateTime);
        var earth = PlanetCalculator.EarthHeliocentric(jd);

        // Light time: the body is seen where it was when the light left it.
        var body = Heliocentric(orbit.Elements, jd);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var travel = Length(Subtract(body, earth)) * PlanetCalculator.LightTimeDaysPerAu;
            body = Heliocentric(orbit.Elements, jd - travel);
        }

        var geocentric = Subtract(body, earth);
        var delta = Length(geocentric);
        var r = Length(body);
        var earthSun = Length(earth);
        var j2000 = PlanetCalculator.EclipticToEquatorialJ2000(geocentric);
        var phase = PlanetCalculator.AngleFromSides(r, delta, earthSun);
        var elongation = PlanetCalculator.AngleFromSides(earthSun, delta, r);

        return new SmallBodyPosition(
            j2000,
            Precession.FromJ2000(j2000, utcDateTime),
            delta,
            r,
            elongation,
            phase,
            Magnitude(orbit, r, delta, phase));
    }

    /// <summary>
    /// Total visual magnitude: comets m = H + 5 log Δ + 2.5·K·log r (MPC convention, K = "slope");
    /// asteroids the IAU H-G system (Bowell et al. 1989; Meeus 33.14). NaN without H.
    /// </summary>
    public static double Magnitude(SmallBodyOrbit orbit, double sunDistanceAu, double distanceAu, double phaseAngleDegrees)
    {
        ArgumentNullException.ThrowIfNull(orbit);
        if (orbit.AbsoluteMagnitude is not { } h)
        {
            return double.NaN;
        }

        if (orbit.Kind == SmallBodyKind.Comet)
        {
            return h + (5 * Math.Log10(distanceAu)) + (2.5 * (orbit.Slope ?? 4.0) * Math.Log10(sunDistanceAu));
        }

        var g = orbit.Slope ?? 0.15;
        var halfPhase = Math.Tan(phaseAngleDegrees * Math.PI / 360);
        var phi1 = Math.Exp(-3.33 * Math.Pow(halfPhase, 0.63));
        var phi2 = Math.Exp(-1.87 * Math.Pow(halfPhase, 1.22));
        return h + (5 * Math.Log10(sunDistanceAu * distanceAu)) - (2.5 * Math.Log10(((1 - g) * phi1) + (g * phi2)));
    }

    /// <summary>Position in the orbital plane (x towards perihelion), AU, <paramref name="days"/> after perihelion.</summary>
    internal static (double X, double Y) InPlane(OrbitalElements elements, double days)
    {
        var q = elements.PerihelionDistanceAu;
        var e = elements.Eccentricity;
        const double k = OrbitalElements.GaussianConstant;

        if (Math.Abs(e - 1) < ParabolicTolerance)
        {
            // Barker: s³ + 3s = W, with s = tan(ν/2).
            var w = 3 * k / Math.Sqrt(2 * q * q * q) * days;
            var s = SolveBarker(w);
            var r = q * (1 + (s * s));
            var nu = 2 * Math.Atan(s);
            return (r * Math.Cos(nu), r * Math.Sin(nu));
        }

        if (e < 1)
        {
            var a = q / (1 - e);
            var mean = k / Math.Pow(a, 1.5) * days;
            mean = Math.IEEERemainder(mean, 2 * Math.PI);   // [−π, π]
            var eccentric = SolveElliptic(mean, e);
            return (a * (Math.Cos(eccentric) - e), a * Math.Sqrt(1 - (e * e)) * Math.Sin(eccentric));
        }

        var aHyperbolic = q / (e - 1);
        var meanHyperbolic = k / Math.Pow(aHyperbolic, 1.5) * days;
        var hAnomaly = SolveHyperbolic(meanHyperbolic, e);
        return (aHyperbolic * (e - Math.Cosh(hAnomaly)), aHyperbolic * Math.Sqrt((e * e) - 1) * Math.Sinh(hAnomaly));
    }

    /// <summary>E − e·sin E = M for M in [−π, π]: Newton kept inside a shrinking bracket (always converges).</summary>
    public static double SolveElliptic(double mean, double e)
    {
        double low = -Math.PI, high = Math.PI;
        var x = e < 0.8 ? mean : Math.Sign(mean) * Math.Min(Math.PI, Math.Cbrt(6 * Math.Abs(mean)));
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var f = x - (e * Math.Sin(x)) - mean;
            if (f > 0)
            {
                high = x;
            }
            else
            {
                low = x;
            }

            var derivative = 1 - (e * Math.Cos(x));
            var next = x - (f / derivative);
            if (derivative <= 0 || next <= low || next >= high || double.IsNaN(next))
            {
                next = (low + high) / 2;
            }

            if (Math.Abs(next - x) < 1e-14)
            {
                return next;
            }

            x = next;
        }

        return x;
    }

    /// <summary>e·sinh H − H = M: Newton from a logarithmic start, bisection as a safeguard.</summary>
    public static double SolveHyperbolic(double mean, double e)
    {
        var sign = Math.Sign(mean);
        var m = Math.Abs(mean);
        double low = 0, high = Math.Max(1, Math.Log((2 * m / e) + 1.8)) * 2;
        while ((e * Math.Sinh(high)) - high < m)
        {
            high *= 2;
        }

        var x = Math.Log((2 * m / e) + 1.8);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var f = (e * Math.Sinh(x)) - x - m;
            if (f > 0)
            {
                high = x;
            }
            else
            {
                low = x;
            }

            var next = x - (f / ((e * Math.Cosh(x)) - 1));
            if (next <= low || next >= high || double.IsNaN(next))
            {
                next = (low + high) / 2;
            }

            if (Math.Abs(next - x) < 1e-14 * Math.Max(1, Math.Abs(x)))
            {
                return sign * next;
            }

            x = next;
        }

        return sign * x;
    }

    /// <summary>The real root of s³ + 3s − W = 0 (closed form, Meeus 34.4).</summary>
    public static double SolveBarker(double w)
    {
        var y = Math.Cbrt((w / 2) + Math.Sqrt(((w * w) / 4) + 1));
        return y - (1 / y);
    }

    private static (double X, double Y, double Z) Subtract((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static double Length((double X, double Y, double Z) v) => Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));
}
