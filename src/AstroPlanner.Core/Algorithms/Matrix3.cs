namespace AstroPlanner.Core.Algorithms;

/// <summary>A 3-vector of doubles (unit direction vectors on the celestial sphere).</summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

    public Vector3d Normalized()
    {
        var length = Length;
        return length == 0 ? this : new Vector3d(X / length, Y / length, Z / length);
    }

    /// <summary>Unit vector of a spherical position (longitude/RA and latitude/Dec in degrees).</summary>
    public static Vector3d FromSpherical(double longitudeDegrees, double latitudeDegrees)
    {
        var lon = AstronomyMath.DegreesToRadiansValue(longitudeDegrees);
        var lat = AstronomyMath.DegreesToRadiansValue(latitudeDegrees);
        return new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vector3d operator *(Vector3d a, double s) => new(a.X * s, a.Y * s, a.Z * s);

    public static Vector3d Add(Vector3d a, Vector3d b) => a + b;

    public static Vector3d Subtract(Vector3d a, Vector3d b) => a - b;

    public static Vector3d Multiply(Vector3d a, double s) => a * s;
}

/// <summary>
/// A 3×3 matrix in row-major order. Used for the frame transforms sent to the sky renderer
/// (J2000 → equator of date → horizontal), so that the GPU applies exactly the Core model.
/// </summary>
public readonly record struct Matrix3(
    double M11, double M12, double M13,
    double M21, double M22, double M23,
    double M31, double M32, double M33)
{
    public static Matrix3 Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>Active rotation by <paramref name="degrees"/> about the x axis.</summary>
    public static Matrix3 RotationX(double degrees)
    {
        var (s, c) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(degrees));
        return new(1, 0, 0, 0, c, -s, 0, s, c);
    }

    /// <summary>Active rotation by <paramref name="degrees"/> about the y axis.</summary>
    public static Matrix3 RotationY(double degrees)
    {
        var (s, c) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(degrees));
        return new(c, 0, s, 0, 1, 0, -s, 0, c);
    }

    /// <summary>Active rotation by <paramref name="degrees"/> about the z axis.</summary>
    public static Matrix3 RotationZ(double degrees)
    {
        var (s, c) = Math.SinCos(AstronomyMath.DegreesToRadiansValue(degrees));
        return new(c, -s, 0, s, c, 0, 0, 0, 1);
    }

    public Matrix3 Transpose() => new(M11, M21, M31, M12, M22, M32, M13, M23, M33);

    public double Determinant() =>
        M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31);

    public Vector3d Transform(Vector3d v) => new(
        M11 * v.X + M12 * v.Y + M13 * v.Z,
        M21 * v.X + M22 * v.Y + M23 * v.Z,
        M31 * v.X + M32 * v.Y + M33 * v.Z);

    public static Matrix3 operator *(Matrix3 a, Matrix3 b) => new(
        a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
        a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
        a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,
        a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
        a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
        a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,
        a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
        a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
        a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33);

    public static Matrix3 Multiply(Matrix3 a, Matrix3 b) => a * b;

    /// <summary>Row-major copy of the nine elements (for serialization).</summary>
    public double[] ToArray() => [M11, M12, M13, M21, M22, M23, M31, M32, M33];
}
