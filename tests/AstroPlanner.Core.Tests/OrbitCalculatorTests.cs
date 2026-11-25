using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using Xunit.Abstractions;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// Comets and asteroids from MPC elements against JPL Horizons (tests/fixtures/horizons/*_2026-11.csv,
/// geocentric astrometric RA/Dec and distance). Horizons integrates perturbed orbits (and
/// non-gravitational forces for comets); the app propagates the MPC osculating elements as a
/// two-body orbit, so the agreement is best near the elements' epoch and degrades with time.
/// </summary>
public sealed class OrbitCalculatorTests(ITestOutputHelper output)
{
    private static double Jd(int year, int month, double day) =>
        AstronomyMath.ToJulianDate(new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1));

    // Lines of CometEls.txt (MPC, 2026-11-20), epoch 2026-11-20:
    // 0002P         2027 02 10.2276  0.338591  0.847328  187.2881  334.0197   11.3479  20261120  14.6  4.0  2P/Encke
    private static readonly SmallBodyOrbit Encke = new(SmallBodyKind.Comet,
        new OrbitalElements(0.338591, 0.847328, Jd(2027, 2, 10.2276), 187.2881, 334.0197, 11.3479, Jd(2026, 11, 20)), 14.6, 4.0, "2P");

    //     CK23A030  2024 09 27.8471  0.391277  1.000184  308.5914   21.6863  139.0974  20261120   6.5  3.2  C/2023 A3 (Tsuchinshan-ATLAS)
    private static readonly SmallBodyOrbit TsuchinshanAtlas = new(SmallBodyKind.Comet,
        new OrbitalElements(0.391277, 1.000184, Jd(2024, 9, 27.8471), 308.5914, 21.6863, 139.0974, Jd(2026, 11, 20)), 6.5, 3.2, "C/2023 A3");

    //     CK25A060  2025 11  8.5187  0.529804  0.995604  132.9558  108.1001  143.6634  20261120   8.6  4.0  C/2025 A6 (Lemmon)
    private static readonly SmallBodyOrbit Lemmon = new(SmallBodyKind.Comet,
        new OrbitalElements(0.529804, 0.995604, Jd(2025, 11, 8.5187), 132.9558, 108.1001, 143.6634, Jd(2026, 11, 20)), 8.6, 4.0, "C/2025 A6");

    // Soft00Bright.txt (MPC, 2026 file), epoch K25BL = 2025-11-21.0:
    // 00004    3.25  0.15 K25BL  26.80968  151.53712  103.70232    7.14406  0.0901676  0.27158812   2.3615413
    private static readonly SmallBodyOrbit Vesta = new(SmallBodyKind.Asteroid,
        OrbitalElements.FromMeanAnomaly(2.3615413, 0.0901676, 7.14406, 103.70232, 151.53712, 26.80968, Jd(2025, 11, 21)), 3.25, 0.15, "(4) Vesta");

    private static List<(DateTime Utc, double Ra, double Dec, double Delta)> Load(string name) =>
        [.. File.ReadLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "horizons", name + ".csv"))
            .Where(l => !l.StartsWith('#') && !l.StartsWith("utc", StringComparison.Ordinal))
            .Select(l => l.Split(','))
            .Select(p => (DateTime.SpecifyKind(DateTime.ParseExact(p[0], "yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture)))];

    private (double MaxArcmin, double MaxDeltaRelative) Compare(SmallBodyOrbit orbit, string fixture)
    {
        double maxArcmin = 0, maxDelta = 0;
        foreach (var (utc, ra, dec, delta) in Load(fixture))
        {
            var position = OrbitCalculator.GetPosition(orbit, utc);
            var separation = AstronomyMath.AngularSeparationDegrees(position.AstrometricJ2000, new EquatorialCoordinates(ra / 15, dec)) * 60;
            maxArcmin = Math.Max(maxArcmin, separation);
            maxDelta = Math.Max(maxDelta, Math.Abs(position.DistanceAu - delta) / delta);
        }

        output.WriteLine($"{fixture}: max {maxArcmin:0.000}′, distance {maxDelta:P4}");
        return (maxArcmin, maxDelta);
    }

    [Theory]
    // Measured 2026-11-20: 2P 0.29′, C/2023 A3 0.07′, C/2025 A6 0.06′ over the month around the epoch.
    [InlineData("comet_2P_2026-11", 0.5)]
    [InlineData("comet_C2023A3_2026-11", 0.25)]
    [InlineData("comet_C2025A6_2026-11", 0.25)]
    public void Comets_MatchHorizons_NearTheEpoch(string fixture, double toleranceArcmin)
    {
        var orbit = fixture.Contains("2P", StringComparison.Ordinal) ? Encke
            : fixture.Contains("C2023A3", StringComparison.Ordinal) ? TsuchinshanAtlas : Lemmon;

        var (arcmin, delta) = Compare(orbit, fixture);

        arcmin.Should().BeLessThan(toleranceArcmin);
        delta.Should().BeLessThan(1e-3);
    }

    [Fact]
    public void Vesta_MatchesHorizons_NearTheEpoch_AndAYearLater()
    {
        var (nearEpoch, delta) = Compare(Vesta, "asteroid_4_2025-11");
        nearEpoch.Should().BeLessThan(0.25, "measured 0.07′");
        delta.Should().BeLessThan(1e-4);

        // The bright-asteroid file is yearly: a year after the epoch, Jupiter's perturbations
        // are not followed. The error must stay small enough for planning and framing.
        var (yearLater, _) = Compare(Vesta, "asteroid_4_2026-11");
        yearLater.Should().BeLessThan(1.5, "measured 0.74′");
    }

    [Fact]
    public void Parabolic_IsContinuousWithNearlyParabolicEllipsesAndHyperbolas()
    {
        var t = Jd(2026, 1, 1);
        foreach (var days in new[] { -200.0, -3, 0, 0.5, 40, 400 })
        {
            (double X, double Y, double Z) At(double e) =>
                OrbitCalculator.Heliocentric(new OrbitalElements(0.8, e, t, 30, 60, 100, t), t + days);

            var parabola = At(1);
            foreach (var e in new[] { 1 - 1e-6, 1 + 1e-6 })
            {
                var other = At(e);
                var gap = Math.Sqrt(Math.Pow(other.X - parabola.X, 2) + Math.Pow(other.Y - parabola.Y, 2) + Math.Pow(other.Z - parabola.Z, 2));
                gap.Should().BeLessThan(1e-4, $"e = {e}, {days} days from perihelion");
            }

            Math.Sqrt((parabola.X * parabola.X) + (parabola.Y * parabola.Y) + (parabola.Z * parabola.Z)).Should().BeGreaterThanOrEqualTo(0.8 - 1e-12);
        }
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(1e-6, 0.99999)]
    [InlineData(3.1, 0.9999)]
    [InlineData(-2.0, 0.3)]
    public void Kepler_Elliptic_SolvesTheEquation(double mean, double e)
    {
        var x = OrbitCalculator.SolveElliptic(mean, e);
        (x - e * Math.Sin(x) - mean).Should().BeApproximately(0, 1e-12);
    }

    [Theory]
    [InlineData(1e-5, 1.0001)]
    [InlineData(0.7, 1.5)]
    [InlineData(-40.0, 1.0002)]
    [InlineData(5000.0, 3.0)]
    public void Kepler_Hyperbolic_SolvesTheEquation(double mean, double e)
    {
        var h = OrbitCalculator.SolveHyperbolic(mean, e);
        (e * Math.Sinh(h) - h - mean).Should().BeApproximately(0, 1e-9 * Math.Max(1, Math.Abs(mean)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.7)]
    [InlineData(-25.0)]
    public void Barker_SolvesTheCubic(double w)
    {
        var s = OrbitCalculator.SolveBarker(w);
        (s * s * s + 3 * s - w).Should().BeApproximately(0, 1e-12 * Math.Max(1, Math.Abs(w)));
    }

    [Fact]
    public void Magnitudes_FollowTheMpcAndHGConventions()
    {
        // Comet: m = H + 5 log Δ + 2.5·K·log r.
        OrbitCalculator.Magnitude(Encke, 2.0, 0.5, 30).Should().BeApproximately(14.6 + 5 * Math.Log10(0.5) + 10 * Math.Log10(2.0), 1e-12);
        // Asteroid at opposition (phase 0): m = H + 5 log(rΔ); phase darkens it.
        OrbitCalculator.Magnitude(Vesta, 2.2, 1.2, 0).Should().BeApproximately(3.25 + 5 * Math.Log10(2.2 * 1.2), 1e-12);
        OrbitCalculator.Magnitude(Vesta, 2.2, 1.2, 20).Should().BeGreaterThan(OrbitCalculator.Magnitude(Vesta, 2.2, 1.2, 0) + 0.5);
        OrbitCalculator.Magnitude(Vesta with { AbsoluteMagnitude = null }, 2.2, 1.2, 0).Should().Be(double.NaN);
    }

    [Fact]
    public void Position_HasConsistentGeometry()
    {
        var position = OrbitCalculator.GetPosition(Vesta, new DateTime(2025, 11, 21, 0, 0, 0, DateTimeKind.Utc));

        position.SunDistanceAu.Should().BeInRange(2.1, 2.6);
        position.ElongationDegrees.Should().BeInRange(0, 180);
        position.PhaseAngleDegrees.Should().BeInRange(0, 40);
        position.Magnitude.Should().BeInRange(5, 9);
        AstronomyMath.AngularSeparationDegrees(position.Equatorial, Precession.FromJ2000(position.AstrometricJ2000, new DateTime(2025, 11, 21, 0, 0, 0, DateTimeKind.Utc)))
            .Should().BeLessThan(1e-9);
        FluentActions.Invoking(() => OrbitalElements.FromMeanAnomaly(2, 1.2, 1, 1, 1, 1, 2451545)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
