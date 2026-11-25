using AstroPlanner.Core.Algorithms;

namespace AstroPlanner.Core.Sky;

/// <summary>
/// Galactic coordinates (l, b) and the J2000 (ICRS) equatorial frame. The rotation is the one that
/// defines the galactic frame in the Hipparcos catalogue (ESA 1997, vol. 1, §1.5.3, matrix A_G):
/// north galactic pole at RA 192.85948°, Dec +27.12825°, and longitude 0 towards RA 266.40499°,
/// Dec −28.93617°. Used by the DataBuilder to turn the galactic Milky Way panorama into the J2000
/// frame the renderer draws.
/// </summary>
public static class GalacticCoordinates
{
    /// <summary>J2000 equatorial → galactic: r_gal = M · r_J2000 (rows are the galactic axes).</summary>
    public static Matrix3 FromJ2000 { get; } = new(
        -0.0548755604, -0.8734370902, -0.4838350155,
        +0.4941094279, -0.4448296300, +0.7469822445,
        -0.8676661490, -0.1980763734, +0.4559837762);

    /// <summary>Galactic → J2000 equatorial (the transpose: the matrix is orthogonal).</summary>
    public static Matrix3 ToJ2000 { get; } = FromJ2000.Transpose();

    /// <summary>Galactic longitude and latitude (degrees, l in [0, 360)) of a J2000 direction.</summary>
    public static (double LongitudeDegrees, double LatitudeDegrees) FromEquatorial(Vector3d j2000)
    {
        var g = FromJ2000.Transform(j2000.Normalized());
        var l = AstronomyMath.RadiansToDegreesValue(Math.Atan2(g.Y, g.X));
        var b = AstronomyMath.RadiansToDegreesValue(Math.Asin(Math.Clamp(g.Z, -1, 1)));
        return (l < 0 ? l + 360 : l, b);
    }

    /// <summary>The J2000 direction of galactic longitude and latitude (degrees).</summary>
    public static Vector3d ToEquatorial(double longitudeDegrees, double latitudeDegrees) =>
        ToJ2000.Transform(Vector3d.FromSpherical(longitudeDegrees, latitudeDegrees));
}
