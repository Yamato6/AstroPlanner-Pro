using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// The galactic frame against its definition in the Hipparcos catalogue (ESA 1997, vol. 1,
/// §1.5.3): the north galactic pole is at RA 192.85948°, Dec +27.12825° and the origin of
/// longitude at RA 266.40499°, Dec −28.93617° (J2000). Those angles are given to 5 decimals, so
/// they are checked to 1e-5° (one unit of the last digit, 0.04″).
/// </summary>
public sealed class GalacticCoordinatesTests
{
    private const double Tolerance = 1e-5;

    [Fact]
    public void Pole_AndOrigin_MatchTheHipparcosDefinition()
    {
        var pole = SkyFrame.ToEquatorial(GalacticCoordinates.ToEquatorial(0, 90));
        (pole.RightAscensionHours * 15).Should().BeApproximately(192.85948, Tolerance);
        pole.DeclinationDegrees.Should().BeApproximately(27.12825, Tolerance);

        var origin = SkyFrame.ToEquatorial(GalacticCoordinates.ToEquatorial(0, 0));
        (origin.RightAscensionHours * 15).Should().BeApproximately(266.40499, Tolerance);
        origin.DeclinationDegrees.Should().BeApproximately(-28.93617, Tolerance);
    }

    [Fact]
    public void FromEquatorial_InvertsToEquatorial()
    {
        foreach (var (l, b) in new[] { (0.0, 0.0), (121.17, -21.57), (280.47, -32.89), (359.9, 45.0), (45.0, -80.0) })
        {
            var (lon, lat) = GalacticCoordinates.FromEquatorial(GalacticCoordinates.ToEquatorial(l, b));
            lon.Should().BeApproximately(l, 1e-7);
            lat.Should().BeApproximately(b, 1e-7);
        }

        GalacticCoordinates.FromJ2000.Determinant().Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Andromeda_IsAtItsGalacticPosition()
    {
        // M31 (J2000 00h42m44.3s +41°16′09″) is at l = 121.17°, b = −21.57° (NED).
        var m31 = Vector3d.FromSpherical((0 + 42 / 60.0 + 44.3 / 3600.0) * 15, 41 + 16 / 60.0 + 9 / 3600.0);
        var (l, b) = GalacticCoordinates.FromEquatorial(m31);
        l.Should().BeApproximately(121.17, 0.01);
        b.Should().BeApproximately(-21.57, 0.01);
    }
}
