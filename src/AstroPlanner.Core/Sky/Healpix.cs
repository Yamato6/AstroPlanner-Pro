namespace AstroPlanner.Core.Sky;

/// <summary>A position inside one HEALPix base face: face 0–11 and continuous x, y in [0, nside).</summary>
/// <remarks>Floor(x), floor(y) are the pixel's face coordinates; the fraction is the position inside it.</remarks>
public readonly record struct HealpixFacePoint(int Face, double X, double Y);

/// <summary>
/// HEALPix in the NESTED scheme (Górski et al. 2005, ApJ 622, 759), as used by HiPS surveys
/// (IVOA HiPS 1.0). nside = 2^order. Angles in degrees: right ascension (longitude) and declination
/// (latitude) in the survey's frame. The algorithms follow the reference C++ library
/// (healpix_base: loc2pix / pix2loc), with continuous face coordinates kept so that a caller can
/// interpolate inside a pixel. Tested against astropy-healpix (tests/fixtures/healpix).
/// </summary>
public static class Healpix
{
    /// <summary>Highest order supported (nside² fits in a long with 12 faces).</summary>
    public const int MaximumOrder = 29;

    public static long Nside(int order)
    {
        CheckOrder(order);
        return 1L << order;
    }

    public static long PixelCount(int order) => 12 * Nside(order) * Nside(order);

    /// <summary>Mean pixel side, √(4π / Npix), in degrees.</summary>
    public static double PixelSizeDegrees(int order) =>
        Math.Sqrt(4 * Math.PI / PixelCount(order)) * 180 / Math.PI;

    /// <summary>Face and continuous face coordinates of a direction at <paramref name="order"/>.</summary>
    public static HealpixFacePoint ToFacePoint(int order, double longitudeDegrees, double latitudeDegrees)
    {
        double nside = Nside(order);
        var phi = longitudeDegrees * Math.PI / 180;
        var theta = (90 - latitudeDegrees) * Math.PI / 180;
        var z = Math.Cos(theta);
        var za = Math.Abs(z);
        var tt = Modulo(phi, 2 * Math.PI) * (2 / Math.PI);   // [0, 4)
        if (tt >= 4)
        {
            tt = 0;
        }

        if (za <= 2.0 / 3)
        {
            // Equatorial region.
            var temp1 = nside * (0.5 + tt);
            var temp2 = nside * z * 0.75;
            var jp = temp1 - temp2;   // index of the ascending edge line
            var jm = temp1 + temp2;   // index of the descending edge line
            var ifp = (long)Math.Floor(jp / nside);
            var ifm = (long)Math.Floor(jm / nside);
            var face = ifp == ifm ? (int)(ifp | 4) : ifp < ifm ? (int)ifp : (int)(ifm + 8);
            var x = jm - (Math.Floor(jm / nside) * nside);
            var y = nside - (jp - (Math.Floor(jp / nside) * nside));
            return new HealpixFacePoint(face, Clamp(x, nside), Clamp(y, nside));
        }

        // Polar caps. √(3(1 − |z|)) computed from sin θ near the poles to keep precision.
        var ntt = Math.Min(3, (int)tt);
        var tp = tt - ntt;
        var sinTheta = Math.Sin(theta);
        var tmp = nside * (za < 0.99 ? Math.Sqrt(3 * (1 - za)) : Math.Abs(sinTheta) * Math.Sqrt(3 / (1 + za)));
        var jpPolar = Math.Min(tp * tmp, nside);
        var jmPolar = Math.Min((1 - tp) * tmp, nside);
        return z >= 0
            ? new HealpixFacePoint(ntt, Clamp(nside - jmPolar, nside), Clamp(nside - jpPolar, nside))
            : new HealpixFacePoint(ntt + 8, Clamp(jpPolar, nside), Clamp(jmPolar, nside));
    }

    /// <summary>NESTED pixel index containing a direction.</summary>
    public static long ToNested(int order, double longitudeDegrees, double latitudeDegrees) =>
        ToNested(order, ToFacePoint(order, longitudeDegrees, latitudeDegrees));

    /// <summary>NESTED pixel index of a face point (its integer part).</summary>
    public static long ToNested(int order, HealpixFacePoint point) =>
        FaceXyToNested(order, point.Face, (long)Math.Floor(point.X), (long)Math.Floor(point.Y));

    /// <summary>NESTED index of face pixel (<paramref name="x"/>, <paramref name="y"/>): x in the even bits, y in the odd bits.</summary>
    public static long FaceXyToNested(int order, int face, long x, long y)
    {
        var nside = Nside(order);
        if (face is < 0 or > 11 || x < 0 || y < 0 || x >= nside || y >= nside)
        {
            throw new ArgumentOutOfRangeException(nameof(face), "Face pixel out of range.");
        }

        return (face * nside * nside) + Spread(x) + (Spread(y) << 1);
    }

    /// <summary>Face and face pixel of a NESTED index.</summary>
    public static (int Face, long X, long Y) NestedToFaceXy(int order, long nested)
    {
        var nside = Nside(order);
        var facePixels = nside * nside;
        if (nested < 0 || nested >= 12 * facePixels)
        {
            throw new ArgumentOutOfRangeException(nameof(nested), "Pixel index out of range.");
        }

        var inFace = nested % facePixels;
        return ((int)(nested / facePixels), Compress(inFace), Compress(inFace >> 1));
    }

    /// <summary>Centre of a NESTED pixel (longitude in [0, 360), latitude).</summary>
    public static (double LongitudeDegrees, double LatitudeDegrees) Center(int order, long nested)
    {
        var (face, x, y) = NestedToFaceXy(order, nested);
        return FromFacePoint(order, new HealpixFacePoint(face, x + 0.5, y + 0.5));
    }

    /// <summary>Direction of a continuous face point (inverse of <see cref="ToFacePoint"/>).</summary>
    public static (double LongitudeDegrees, double LatitudeDegrees) FromFacePoint(int order, HealpixFacePoint point)
    {
        double nside = Nside(order);
        // Ring coordinates from face coordinates (healpix_base::pix2loc with continuous ix, iy).
        var jr = (FaceRow[point.Face] * nside) - point.X - point.Y;   // ring number, 1 … 4·nside − 1 (continuous)
        double z;
        double nr;   // pixels per quarter ring
        if (jr < nside)
        {
            nr = jr;
            z = 1 - (nr * nr / (3 * nside * nside));
        }
        else if (jr > 3 * nside)
        {
            nr = (4 * nside) - jr;
            z = (nr * nr / (3 * nside * nside)) - 1;
        }
        else
        {
            nr = nside;
            z = (2 * nside - jr) * 2 / (3 * nside);
        }

        var tmp = (FaceColumn[point.Face] * nr) + point.X - point.Y;   // continuous position along the ring
        var phi = nr <= 0 ? 0 : (Math.PI / 4) * tmp / nr;

        var latitude = Math.Asin(Math.Clamp(z, -1, 1)) * 180 / Math.PI;
        var longitude = Modulo(phi * 180 / Math.PI, 360);
        return (longitude, latitude);
    }

    // Base-face position in the ring/column scheme of healpix_base (jrll, jpll).
    private static readonly int[] FaceRow = [2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4];
    private static readonly int[] FaceColumn = [1, 3, 5, 7, 0, 2, 4, 6, 1, 3, 5, 7];

    private static long Spread(long value)
    {
        var result = 0L;
        for (var bit = 0; bit < 31; bit++)
        {
            result |= ((value >> bit) & 1) << (2 * bit);
        }

        return result;
    }

    private static long Compress(long value)
    {
        var result = 0L;
        for (var bit = 0; bit < 31; bit++)
        {
            result |= ((value >> (2 * bit)) & 1) << bit;
        }

        return result;
    }

    private static double Clamp(double value, double nside) =>
        value < 0 ? 0 : value >= nside ? Math.BitDecrement(nside) : value;

    private static double Modulo(double value, double period)
    {
        var result = value % period;
        return result < 0 ? result + period : result;
    }

    private static void CheckOrder(int order)
    {
        if (order is < 0 or > MaximumOrder)
        {
            throw new ArgumentOutOfRangeException(nameof(order), "HEALPix order must be 0–29.");
        }
    }
}
