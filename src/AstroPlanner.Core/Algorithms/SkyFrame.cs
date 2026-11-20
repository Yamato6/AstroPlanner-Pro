using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// The transform from J2000 unit vectors to the observer's horizontal frame, in the form the sky
/// renderer applies on the GPU: <c>H(t) = L(φ) · Rz(−LST(t)) · P</c>, where P is the precession
/// J2000 → equator of date, LST(t) = LST₀ + rate · (t − t₀) the local mean sidereal time, and L(φ)
/// maps hour-angle coordinates to (north, east, up). It reproduces exactly
/// <see cref="Precession.FromJ2000"/> + <see cref="CoordinateTransformer.ToHorizontal"/> (no
/// nutation, aberration or refraction — refraction is applied separately).
/// </summary>
/// <param name="Precession">J2000 → mean equator and equinox of <paramref name="EpochUtc"/>.</param>
/// <param name="LatitudeDegrees">Geodetic latitude of the observer.</param>
/// <param name="LocalSiderealTimeDegrees">Local mean sidereal time at <paramref name="EpochUtc"/>.</param>
/// <param name="EpochUtc">Instant the frame was computed for.</param>
public sealed record SkyFrame(
    Matrix3 Precession,
    double LatitudeDegrees,
    double LocalSiderealTimeDegrees,
    DateTime EpochUtc)
{
    /// <summary>Earth rotation rate in mean sidereal time (degrees per SI second; Meeus 12.4).</summary>
    public const double SiderealRateDegreesPerSecond = 360.98564736629 / 86400.0;

    /// <summary>Computes the frame for <paramref name="observer"/> at <paramref name="utcDateTime"/>.</summary>
    public static SkyFrame Compute(ObserverLocation observer, DateTime utcDateTime)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return new SkyFrame(
            Algorithms.Precession.Matrix(Algorithms.Precession.J2000JulianDate, AstronomyMath.ToJulianDate(utc)),
            observer.LatitudeDegrees,
            AstronomyMath.CalculateLocalSiderealTimeDegrees(utc, observer.LongitudeDegrees),
            utc);
    }

    /// <summary>Local sidereal time at <paramref name="utcDateTime"/>, extrapolated linearly from the epoch.</summary>
    public double LocalSiderealTimeAt(DateTime utcDateTime) =>
        AstronomyMath.NormalizeDegrees(
            LocalSiderealTimeDegrees + SiderealRateDegreesPerSecond * (utcDateTime - EpochUtc).TotalSeconds);

    /// <summary>
    /// Matrix from hour-angle coordinates (x toward the meridian, y toward −H, z toward the pole)
    /// to (north, east, up). It is orthogonal with determinant −1 because (N, E, U) is left-handed.
    /// </summary>
    public static Matrix3 HorizonMatrix(double latitudeDegrees)
    {
        var (s, c) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(latitudeDegrees));
        return new Matrix3(-s, 0, c, 0, 1, 0, c, 0, s);
    }

    /// <summary>Full J2000 → (north, east, up) matrix at <paramref name="utcDateTime"/>.</summary>
    public Matrix3 J2000ToHorizontalAt(DateTime utcDateTime) =>
        HorizonMatrix(LatitudeDegrees) * Matrix3.RotationZ(-LocalSiderealTimeAt(utcDateTime)) * Precession;

    /// <summary>Converts a (north, east, up) vector to geometric altitude/azimuth (azimuth from north through east).</summary>
    public static HorizontalCoordinates ToHorizontal(Vector3d northEastUp)
    {
        var v = northEastUp.Normalized();
        var altitude = AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(v.Z, -1.0, 1.0)));
        var azimuth = Math.Abs(v.X) < 1e-12 && Math.Abs(v.Y) < 1e-12
            ? 0
            : AstronomyMath.NormalizeDegrees(AstronomyMath.RadiansToDegreesValue(Math.Atan2(v.Y, v.X)));
        return new HorizontalCoordinates(altitude, azimuth);
    }

    /// <summary>Unit (north, east, up) vector of a horizontal position.</summary>
    public static Vector3d FromHorizontal(HorizontalCoordinates horizontal)
    {
        ArgumentNullException.ThrowIfNull(horizontal);
        return Vector3d.FromSpherical(horizontal.AzimuthDegrees, horizontal.AltitudeDegrees);
    }

    /// <summary>J2000 unit vector of catalog coordinates.</summary>
    public static Vector3d FromEquatorial(EquatorialCoordinates coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        return Vector3d.FromSpherical(coordinates.RightAscensionHours * 15.0, coordinates.DeclinationDegrees);
    }

    /// <summary>Catalog coordinates of a J2000 direction (inverse of <see cref="FromEquatorial"/>).</summary>
    public static EquatorialCoordinates ToEquatorial(Vector3d j2000)
    {
        var v = j2000.Normalized();
        var ra = Math.Abs(v.X) < 1e-12 && Math.Abs(v.Y) < 1e-12
            ? 0
            : AstronomyMath.NormalizeDegrees(AstronomyMath.RadiansToDegreesValue(Math.Atan2(v.Y, v.X)));
        return new EquatorialCoordinates(ra / 15.0, AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(v.Z, -1.0, 1.0))));
    }
}
