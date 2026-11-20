using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// The matrix form of the J2000 → horizontal transform used by the sky renderer must reproduce
/// the spherical Core model exactly, and the moving bodies must match Meeus and JPL Horizons.
/// </summary>
public sealed class SkyFrameTests
{
    private static DateTime FromJulianDate(double jd) =>
        new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(jd - 2451545.0);

    [Fact]
    public void Matrix3_RotationsAreOrthogonal_AndComposeLikeAngles()
    {
        var a = Matrix3.RotationZ(30) * Matrix3.RotationZ(15);
        var b = Matrix3.RotationZ(45);

        a.ToArray().Should().BeEquivalentTo(b.ToArray(), o => o.Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-12)).WhenTypeIs<double>());
        (Matrix3.RotationX(23) * Matrix3.RotationX(23).Transpose()).ToArray()
            .Should().BeEquivalentTo(Matrix3.Identity.ToArray(), o => o.Using<double>(c => c.Subject.Should().BeApproximately(c.Expectation, 1e-12)).WhenTypeIs<double>());
        Matrix3.RotationY(70).Determinant().Should().BeApproximately(1, 1e-12);
        SkyFrame.HorizonMatrix(21.88).Determinant().Should().BeApproximately(-1, 1e-12);
    }

    [Fact]
    public void Matrix3_RotationZ_TurnsXTowardY()
    {
        var v = Matrix3.RotationZ(90).Transform(new Vector3d(1, 0, 0));

        v.X.Should().BeApproximately(0, 1e-12);
        v.Y.Should().BeApproximately(1, 1e-12);
    }

    [Fact]
    public void PrecessionMatrix_MatchesMeeusExample21b()
    {
        // θ Persei to 2028-11-13.19 (Meeus 21.b): α = 41.547214°, δ = +49.348483°.
        var j2000 = Vector3d.FromSpherical(41.054063, 49.227750);

        var ofDate = Precession.Matrix(Precession.J2000JulianDate, 2462088.69).Transform(j2000);

        var ra = AstronomyMath.NormalizeDegrees(Math.Atan2(ofDate.Y, ofDate.X) * 180 / Math.PI);
        var dec = Math.Asin(ofDate.Z) * 180 / Math.PI;
        ra.Should().BeApproximately(41.547214, 0.001);
        dec.Should().BeApproximately(49.348483, 0.001);
    }

    [Fact]
    public void PrecessionMatrix_EqualsSphericalPrecession()
    {
        var random = new Random(2026);
        for (var i = 0; i < 500; i++)
        {
            var coordinates = new EquatorialCoordinates(random.NextDouble() * 24, random.NextDouble() * 178 - 89);
            var toJd = Precession.J2000JulianDate + (random.NextDouble() - 0.5) * 36525 * 2;

            var spherical = Precession.Precess(coordinates, Precession.J2000JulianDate, toJd);
            var matrix = Precession.Matrix(Precession.J2000JulianDate, toJd).Transform(SkyFrame.FromEquatorial(coordinates));

            AngleBetween(matrix, SkyFrame.FromEquatorial(spherical)).Should().BeLessThan(1e-9);
        }
    }

    [Fact]
    public void J2000ToHorizontal_ReproducesCoordinateTransformer()
    {
        var random = new Random(42);
        for (var i = 0; i < 1000; i++)
        {
            var observer = new ObserverLocation(random.NextDouble() * 178 - 89, random.NextDouble() * 360 - 180);
            var utc = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(random.NextDouble() * 60 * 365.25 * 86400);
            var j2000 = new EquatorialCoordinates(random.NextDouble() * 24, random.NextDouble() * 180 - 90);

            var expected = CoordinateTransformer.ToHorizontal(Precession.FromJ2000(j2000, utc), observer, utc);
            var actual = SkyFrame.Compute(observer, utc).J2000ToHorizontalAt(utc).Transform(SkyFrame.FromEquatorial(j2000));

            AngleBetween(actual, SkyFrame.FromHorizontal(expected)).Should().BeLessThan(1e-6);
        }
    }

    [Fact]
    public void J2000ToHorizontal_ExtrapolatesSiderealTimeOverANight()
    {
        // The renderer keeps one frame and advances LST linearly: 12 h later the position must
        // still agree with a fresh computation (precession drift ≪ 0.01°).
        var observer = new ObserverLocation(21.88, -102.29);
        var epoch = new DateTime(2026, 3, 20, 2, 0, 0, DateTimeKind.Utc);
        var later = epoch.AddHours(12).AddMinutes(17);
        var m42 = new EquatorialCoordinates(5.5881, -5.3911);

        var expected = CoordinateTransformer.ToHorizontal(Precession.FromJ2000(m42, later), observer, later);
        var actual = SkyFrame.Compute(observer, epoch).J2000ToHorizontalAt(later).Transform(SkyFrame.FromEquatorial(m42));

        AngleBetween(actual, SkyFrame.FromHorizontal(expected)).Should().BeLessThan(1e-4);
    }

    [Fact]
    public void ToHorizontal_InvertsFromHorizontal()
    {
        var horizontal = new HorizontalCoordinates(37.5, 251.25);

        var back = SkyFrame.ToHorizontal(SkyFrame.FromHorizontal(horizontal));

        back.AltitudeDegrees.Should().BeApproximately(37.5, 1e-9);
        back.AzimuthDegrees.Should().BeApproximately(251.25, 1e-9);
        SkyFrame.ToHorizontal(new Vector3d(0, 0, 1)).AzimuthDegrees.Should().Be(0);
    }

    [Fact]
    public void BrightLimb_MatchesMeeusExample48a()
    {
        // 1992-04-12 0h TD (Meeus 48.a): Sun α0 = 20.6579°, δ0 = +8.6964°; Moon α = 134.6885°,
        // δ = +13.7684° → χ = 285.0°.
        var chi = SolarSystemCalculator.GetBrightLimbPositionAngleDegrees(
            new EquatorialCoordinates(134.6885 / 15, 13.7684),
            new EquatorialCoordinates(20.6579 / 15, 8.6964));

        chi.Should().BeApproximately(285.0, 0.05);
    }

    [Fact]
    public void MoonPhaseAngle_MatchesMeeusExample48a()
    {
        // Meeus 48.a: i = 69.0756° (our low-precision Moon is good to a few tenths of a degree).
        var utc = FromJulianDate(2448724.5);

        var phase = SolarSystemCalculator.GetMoonPhaseAngleDegrees(
            SolarSystemCalculator.GetMoonPosition(utc), SolarSystemCalculator.GetSunPosition(utc));

        phase.Should().BeApproximately(69.0756, 1.0);
    }

    [Fact]
    public void Bodies_AgreeWithTheAstronomyService()
    {
        var service = new AstronomyService();
        var observer = new ObserverLocation(40.42, -3.70, 667);
        var utc = new DateTime(2026, 1, 3, 22, 0, 0, DateTimeKind.Utc);

        var bodies = MovingBodyCalculator.GetBodies(observer, utc);

        bodies.Select(b => b.Id).Should().Equal(
            ["Sun", "Moon", .. SolarSystemCatalog.Planets.Select(p => p.Id)]);
        bodies[0].Horizontal.AltitudeDegrees.Should().BeApproximately(service.GetSunAltitudeDegrees(observer, utc), 1e-9);

        var moon = service.GetMoon(observer, utc);
        Refraction.ApparentAltitude(bodies[1].Horizontal.AltitudeDegrees).Should().BeApproximately(moon.Horizontal.AltitudeDegrees, 1e-9);
        bodies[1].IlluminatedFraction.Should().BeApproximately(moon.IlluminatedFraction, 1e-12);
        bodies[1].IlluminatedFraction.Should().BeGreaterThan(0.95); // full Moon of 2026-01-03
        bodies[1].ApparentDiameterArcsec.Should().BeInRange(29.3 * 60, 34.1 * 60);
        bodies[1].Magnitude.Should().BeInRange(-13, -12.5);
        bodies[0].ApparentDiameterArcsec.Should().BeInRange(31.4 * 60, 32.6 * 60);

        var jupiter = SolarSystemCatalog.Planets.Single(p => p.Planet == Planet.Jupiter);
        var expected = service.GetHorizontal(jupiter, observer, utc);
        var actual = bodies.Single(b => b.Id == jupiter.Id).Horizontal;
        Refraction.ApparentAltitude(actual.AltitudeDegrees).Should().BeApproximately(expected.AltitudeDegrees, 1e-9);
        actual.AzimuthDegrees.Should().BeApproximately(expected.AzimuthDegrees, 1e-9);
    }

    [Fact]
    public void Tracks_ReproduceTheBodiesAtEverySample()
    {
        var observer = new ObserverLocation(21.88, -102.29, 1888);
        var state = new DateTime(2026, 6, 21, 4, 0, 0, DateTimeKind.Utc);

        var tracks = MovingBodyCalculator.GetTracks(observer, state, state.AddHours(-1), state.AddHours(1), TimeSpan.FromMinutes(10));

        tracks.Should().HaveCount(9);
        var frame = SkyFrame.Compute(observer, state);
        foreach (var track in tracks)
        {
            track.Samples.Should().HaveCount(13);
            foreach (var sample in track.Samples)
            {
                var expected = MovingBodyCalculator.GetBodies(observer, sample.Utc).Single(b => b.Id == track.State.Id).Horizontal;
                var actual = frame.J2000ToHorizontalAt(sample.Utc).Transform(sample.J2000Direction);
                AngleBetween(actual, SkyFrame.FromHorizontal(expected)).Should().BeLessThan(1e-6);
            }
        }
    }

    [Theory]
    [InlineData("sun_aguascalientes_2026-06-20", 21.88, -102.29)]
    [InlineData("sun_madrid_2023-12-15", 40.42, -3.70)]
    public void Sun_MatchesHorizons(string fixture, double latitude, double longitude)
    {
        // Airless (geometric) Horizons positions, sampled every 30 min while the Sun is up.
        var observer = new ObserverLocation(latitude, longitude);
        var rows = HorizonsFixture.Load(fixture).Rows.Where((row, i) => i % 30 == 0 && row.Elevation > 5);

        foreach (var row in rows)
        {
            var sun = MovingBodyCalculator.GetBodies(observer, row.Utc)[0].Horizontal;
            sun.AltitudeDegrees.Should().BeApproximately(row.Elevation, 0.05);
            AngleBetween(SkyFrame.FromHorizontal(sun), SkyFrame.FromHorizontal(new HorizontalCoordinates(row.Elevation, row.Azimuth)))
                .Should().BeLessThan(0.05);
        }
    }

    [Fact]
    public void Tracks_RejectInvalidArguments()
    {
        var observer = new ObserverLocation(0, 0);
        var utc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        FluentActions.Invoking(() => MovingBodyCalculator.GetTracks(observer, utc, utc, utc, TimeSpan.Zero))
            .Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => MovingBodyCalculator.GetTracks(observer, utc, utc, utc.AddHours(-1), TimeSpan.FromMinutes(1)))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(5.5881, -5.3911)]
    [InlineData(23.99, 89.5)]
    [InlineData(0, 0)]
    [InlineData(12.5, -63.1)]
    public void ToEquatorial_InvertsFromEquatorial(double raHours, double decDegrees)
    {
        var back = SkyFrame.ToEquatorial(SkyFrame.FromEquatorial(new EquatorialCoordinates(raHours, decDegrees)));

        back.RightAscensionHours.Should().BeApproximately(raHours, 1e-9);
        back.DeclinationDegrees.Should().BeApproximately(decDegrees, 1e-9);
        SkyFrame.ToEquatorial(new Vector3d(0, 0, 2)).DeclinationDegrees.Should().BeApproximately(90, 1e-12);
    }

    /// <summary>Angle between two vectors, in degrees.</summary>
    private static double AngleBetween(Vector3d a, Vector3d b)
    {
        var u = a.Normalized();
        var v = b.Normalized();
        // atan2(|u×v|, u·v) is accurate for tiny angles, unlike acos.
        var cross = new Vector3d(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
        return Math.Atan2(cross.Length, u.X * v.X + u.Y * v.Y + u.Z * v.Z) * 180 / Math.PI;
    }
}
