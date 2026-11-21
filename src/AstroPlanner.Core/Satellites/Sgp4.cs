using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Satellites;

/// <summary>The SGP4 model can no longer describe the orbit (the satellite has decayed or the elements are nonsense).</summary>
public sealed class SatellitePropagationException : Exception
{
    public SatellitePropagationException()
    {
    }

    public SatellitePropagationException(string message) : base(message)
    {
    }

    public SatellitePropagationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>Position (km) and velocity (km/s) in the TEME frame (true equator, mean equinox of date).</summary>
public readonly record struct TemeState(Vector3d PositionKm, Vector3d VelocityKmPerSecond);

/// <summary>
/// The SGP4 propagator for near-Earth orbits (period under 225 minutes), following Vallado,
/// Crawford, Hujsak and Kelso, "Revisiting Spacetrack Report #3", AIAA 2006-6753, with the WGS-72
/// constants the element sets are fitted with. The deep-space terms (SDP4) are not implemented:
/// every satellite of CelesTrak's <c>visual</c> and <c>stations</c> groups is near-Earth, and
/// <see cref="Create"/> rejects the others. Checked against the paper's verification set
/// (tests/fixtures/satellites).
/// </summary>
public sealed class Sgp4
{
    // WGS-72 (the constants the two-line element sets are fitted with).
    private const double Mu = 398600.8;
    private const double EarthRadiusKm = 6378.135;
    private const double J2 = 0.001082616;
    private const double J3 = -0.00000253881;
    private const double J4 = -0.00000165597;
    private const double J3OverJ2 = J3 / J2;
    private const double TwoThirds = 2.0 / 3.0;
    private const double TwoPi = 2 * Math.PI;

    /// <summary>sqrt(GM / Re³) in 1/min.</summary>
    private static readonly double Xke = 60.0 / Math.Sqrt(EarthRadiusKm * EarthRadiusKm * EarthRadiusKm / Mu);

    private static readonly double KmPerSecondUnit = EarthRadiusKm * Xke / 60.0;

    private readonly double _bstar;
    private readonly double _ecco;
    private readonly double _argpo;
    private readonly double _inclo;
    private readonly double _mo;
    private readonly double _nodeo;
    private readonly double _no;
    private readonly bool _isimp;
    private readonly double _aycof, _con41, _cc1, _cc4, _cc5, _d2, _d3, _d4, _delmo, _eta, _argpdot, _omgcof,
        _sinmao, _t2cof, _t3cof, _t4cof, _t5cof, _x1mth2, _x7thm1, _mdot, _nodedot, _xlcof, _xmcof, _nodecf;

    private Sgp4(SatelliteElements elements)
    {
        Elements = elements;
        const double degrees = Math.PI / 180;
        _bstar = elements.BStar;
        _ecco = elements.Eccentricity;
        _argpo = elements.ArgumentOfPerigeeDegrees * degrees;
        _inclo = elements.InclinationDegrees * degrees;
        _mo = elements.MeanAnomalyDegrees * degrees;
        _nodeo = elements.AscendingNodeDegrees * degrees;
        var noKozai = elements.MeanMotionRevPerDay * TwoPi / 1440.0;

        // initl: recover the Brouwer mean motion and semi-major axis from the Kozai mean motion.
        var eccsq = _ecco * _ecco;
        var omeosq = 1 - eccsq;
        var rteosq = Math.Sqrt(omeosq);
        var cosio = Math.Cos(_inclo);
        var cosio2 = cosio * cosio;
        var ak = Math.Pow(Xke / noKozai, TwoThirds);
        var d1 = 0.75 * J2 * (3 * cosio2 - 1) / (rteosq * omeosq);
        var del = d1 / (ak * ak);
        var adel = ak * (1 - del * del - del * (1.0 / 3.0 + 134 * del * del / 81));
        del = d1 / (adel * adel);
        _no = noKozai / (1 + del);
        var ao = Math.Pow(Xke / _no, TwoThirds);
        var sinio = Math.Sin(_inclo);
        var po = ao * omeosq;
        var con42 = 1 - 5 * cosio2;
        _con41 = -con42 - cosio2 - cosio2;
        var posq = po * po;
        var rp = ao * (1 - _ecco);

        if (omeosq <= 0 || _no <= 0)
        {
            throw new SatellitePropagationException("The elements do not describe an orbit.");
        }

        // A perigee under 220 km uses the simplified drag equations.
        _isimp = rp < 220 / EarthRadiusKm + 1;
        var sfour = 78 / EarthRadiusKm + 1;
        var qzms24 = Math.Pow((120 - 78) / EarthRadiusKm, 4);
        var perigeeKm = (rp - 1) * EarthRadiusKm;
        if (perigeeKm < 156)
        {
            sfour = perigeeKm < 98 ? 20 : perigeeKm - 78;
            qzms24 = Math.Pow((120 - sfour) / EarthRadiusKm, 4);
            sfour = sfour / EarthRadiusKm + 1;
        }

        var pinvsq = 1 / posq;
        var tsi = 1 / (ao - sfour);
        _eta = ao * _ecco * tsi;
        var etasq = _eta * _eta;
        var eeta = _ecco * _eta;
        var psisq = Math.Abs(1 - etasq);
        var coef = qzms24 * Math.Pow(tsi, 4);
        var coef1 = coef / Math.Pow(psisq, 3.5);
        var cc2 = coef1 * _no * (ao * (1 + 1.5 * etasq + eeta * (4 + etasq))
            + 0.375 * J2 * tsi / psisq * _con41 * (8 + 3 * etasq * (8 + etasq)));
        _cc1 = _bstar * cc2;
        var cc3 = _ecco > 1e-4 ? -2 * coef * tsi * J3OverJ2 * _no * sinio / _ecco : 0;
        _x1mth2 = 1 - cosio2;
        _cc4 = 2 * _no * coef1 * ao * omeosq * (_eta * (2 + 0.5 * etasq) + _ecco * (0.5 + 2 * etasq)
            - J2 * tsi / (ao * psisq) * (-3 * _con41 * (1 - 2 * eeta + etasq * (1.5 - 0.5 * eeta))
                + 0.75 * _x1mth2 * (2 * etasq - eeta * (1 + etasq)) * Math.Cos(2 * _argpo)));
        _cc5 = 2 * coef1 * ao * omeosq * (1 + 2.75 * (etasq + eeta) + eeta * etasq);
        var cosio4 = cosio2 * cosio2;
        var temp1 = 1.5 * J2 * pinvsq * _no;
        var temp2 = 0.5 * temp1 * J2 * pinvsq;
        var temp3 = -0.46875 * J4 * pinvsq * pinvsq * _no;
        _mdot = _no + 0.5 * temp1 * rteosq * _con41 + 0.0625 * temp2 * rteosq * (13 - 78 * cosio2 + 137 * cosio4);
        _argpdot = -0.5 * temp1 * con42 + 0.0625 * temp2 * (7 - 114 * cosio2 + 395 * cosio4)
            + temp3 * (3 - 36 * cosio2 + 49 * cosio4);
        var xhdot1 = -temp1 * cosio;
        _nodedot = xhdot1 + (0.5 * temp2 * (4 - 19 * cosio2) + 2 * temp3 * (3 - 7 * cosio2)) * cosio;
        _omgcof = _bstar * cc3 * Math.Cos(_argpo);
        _xmcof = _ecco > 1e-4 ? -TwoThirds * coef * _bstar / eeta : 0;
        _nodecf = 3.5 * omeosq * xhdot1 * _cc1;
        _t2cof = 1.5 * _cc1;
        _xlcof = Math.Abs(cosio + 1) > 1.5e-12
            ? -0.25 * J3OverJ2 * sinio * (3 + 5 * cosio) / (1 + cosio)
            : -0.25 * J3OverJ2 * sinio * (3 + 5 * cosio) / 1.5e-12;
        _aycof = -0.5 * J3OverJ2 * sinio;
        _delmo = Math.Pow(1 + _eta * Math.Cos(_mo), 3);
        _sinmao = Math.Sin(_mo);
        _x7thm1 = 7 * cosio2 - 1;

        if (!_isimp)
        {
            var cc1sq = _cc1 * _cc1;
            _d2 = 4 * ao * tsi * cc1sq;
            var temp = _d2 * tsi * _cc1 / 3;
            _d3 = (17 * ao + sfour) * temp;
            _d4 = 0.5 * temp * ao * tsi * (221 * ao + 31 * sfour) * _cc1;
            _t3cof = _d2 + 2 * cc1sq;
            _t4cof = 0.25 * (3 * _d3 + _cc1 * (12 * _d2 + 10 * cc1sq));
            _t5cof = 0.2 * (3 * _d4 + 12 * _cc1 * _d3 + 6 * _d2 * _d2 + 15 * cc1sq * (2 * _d2 + cc1sq));
        }
    }

    public SatelliteElements Elements { get; }

    /// <summary>Initialises the propagator; throws <see cref="ArgumentException"/> for a deep-space orbit.</summary>
    public static Sgp4 Create(SatelliteElements elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (!(elements.MeanMotionRevPerDay > 0) || !(elements.Eccentricity is >= 0 and < 1))
        {
            throw new ArgumentException("The mean motion must be positive and the eccentricity in [0, 1).", nameof(elements));
        }

        if (!elements.IsNearEarth)
        {
            throw new ArgumentException($"Deep-space orbits (period {elements.PeriodMinutes:F0} min ≥ 225 min) are not supported.", nameof(elements));
        }

        return new Sgp4(elements);
    }

    /// <summary>State at a UTC instant.</summary>
    public TemeState Propagate(DateTime utc) =>
        Propagate((DateTime.SpecifyKind(utc, DateTimeKind.Utc) - Elements.EpochUtc).TotalMinutes);

    /// <summary>State <paramref name="minutesSinceEpoch"/> minutes after the element epoch.</summary>
    public TemeState Propagate(double minutesSinceEpoch)
    {
        var t = minutesSinceEpoch;

        // Secular gravity and atmospheric drag.
        var xmdf = _mo + _mdot * t;
        var argpdf = _argpo + _argpdot * t;
        var nodedf = _nodeo + _nodedot * t;
        var argpm = argpdf;
        var mm = xmdf;
        var t2 = t * t;
        var nodem = nodedf + _nodecf * t2;
        var tempa = 1 - _cc1 * t;
        var tempe = _bstar * _cc4 * t;
        var templ = _t2cof * t2;

        if (!_isimp)
        {
            var delomg = _omgcof * t;
            var delmtemp = 1 + _eta * Math.Cos(xmdf);
            var delm = _xmcof * (delmtemp * delmtemp * delmtemp - _delmo);
            var shift = delomg + delm;
            mm = xmdf + shift;
            argpm = argpdf - shift;
            var t3 = t2 * t;
            var t4 = t3 * t;
            tempa = tempa - _d2 * t2 - _d3 * t3 - _d4 * t4;
            tempe += _bstar * _cc5 * (Math.Sin(mm) - _sinmao);
            templ = templ + _t3cof * t3 + t4 * (_t4cof + t * _t5cof);
        }

        var am = Math.Pow(Xke / _no, TwoThirds) * tempa * tempa;
        var nm = Xke / Math.Pow(am, 1.5);
        var em = _ecco - tempe;
        if (em >= 1 || em < -0.001 || !(am > 0))
        {
            throw new SatellitePropagationException($"The orbit of {Elements.Name} has decayed (e = {em:G4}).");
        }

        if (em < 1e-6)
        {
            em = 1e-6;
        }

        mm += _no * templ;
        var xlm = mm + argpm + nodem;
        nodem %= TwoPi;
        argpm %= TwoPi;
        xlm %= TwoPi;
        mm = (xlm - argpm - nodem) % TwoPi;

        var sinip = Math.Sin(_inclo);
        var cosip = Math.Cos(_inclo);

        // Long-period periodics.
        var axnl = em * Math.Cos(argpm);
        var temp0 = 1 / (am * (1 - em * em));
        var aynl = em * Math.Sin(argpm) + temp0 * _aycof;
        var xl = mm + argpm + nodem + temp0 * _xlcof * axnl;

        // Kepler's equation (in the Lyddane form).
        var u = (xl - nodem) % TwoPi;
        var eo1 = u;
        var tem5 = 9999.9;
        double sineo1 = 0, coseo1 = 0;
        for (var ktr = 1; Math.Abs(tem5) >= 1e-12 && ktr <= 10; ktr++)
        {
            sineo1 = Math.Sin(eo1);
            coseo1 = Math.Cos(eo1);
            tem5 = 1 - coseo1 * axnl - sineo1 * aynl;
            tem5 = (u - aynl * coseo1 + axnl * sineo1 - eo1) / tem5;
            if (Math.Abs(tem5) >= 0.95)
            {
                tem5 = tem5 > 0 ? 0.95 : -0.95;
            }

            eo1 += tem5;
        }

        // Short-period periodics.
        var ecose = axnl * coseo1 + aynl * sineo1;
        var esine = axnl * sineo1 - aynl * coseo1;
        var el2 = axnl * axnl + aynl * aynl;
        var pl = am * (1 - el2);
        if (pl < 0)
        {
            throw new SatellitePropagationException($"The orbit of {Elements.Name} has decayed (p < 0).");
        }

        var rl = am * (1 - ecose);
        var rdotl = Math.Sqrt(am) * esine / rl;
        var rvdotl = Math.Sqrt(pl) / rl;
        var betal = Math.Sqrt(1 - el2);
        var temp = esine / (1 + betal);
        var sinu = am / rl * (sineo1 - aynl - axnl * temp);
        var cosu = am / rl * (coseo1 - axnl + aynl * temp);
        var su = Math.Atan2(sinu, cosu);
        var sin2u = (cosu + cosu) * sinu;
        var cos2u = 1 - 2 * sinu * sinu;
        temp = 1 / pl;
        var temp1 = 0.5 * J2 * temp;
        var temp2 = temp1 * temp;

        var mrt = rl * (1 - 1.5 * temp2 * betal * _con41) + 0.5 * temp1 * _x1mth2 * cos2u;
        su -= 0.25 * temp2 * _x7thm1 * sin2u;
        var xnode = nodem + 1.5 * temp2 * cosip * sin2u;
        var xinc = _inclo + 1.5 * temp2 * cosip * sinip * cos2u;
        var mvt = rdotl - nm * temp1 * _x1mth2 * sin2u / Xke;
        var rvdot = rvdotl + nm * temp1 * (_x1mth2 * cos2u + 1.5 * _con41) / Xke;

        // Orientation vectors.
        var (sinsu, cossu) = Math.SinCos(su);
        var (snod, cnod) = Math.SinCos(xnode);
        var (sini, cosi) = Math.SinCos(xinc);
        var xmx = -snod * cosi;
        var xmy = cnod * cosi;
        var ux = xmx * sinsu + cnod * cossu;
        var uy = xmy * sinsu + snod * cossu;
        var uz = sini * sinsu;
        var vx = xmx * cossu - cnod * sinsu;
        var vy = xmy * cossu - snod * sinsu;
        var vz = sini * cossu;

        if (mrt < 1)
        {
            throw new SatellitePropagationException($"{Elements.Name} is below the Earth's surface: it has decayed.");
        }

        var r = mrt * EarthRadiusKm;
        return new TemeState(
            new Vector3d(r * ux, r * uy, r * uz),
            new Vector3d(
                (mvt * ux + rvdot * vx) * KmPerSecondUnit,
                (mvt * uy + rvdot * vy) * KmPerSecondUnit,
                (mvt * uz + rvdot * vz) * KmPerSecondUnit));
    }
}
