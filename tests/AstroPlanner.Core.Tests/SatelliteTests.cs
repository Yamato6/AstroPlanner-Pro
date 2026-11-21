using System.Globalization;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// M5b: SGP4 against the verification set of Vallado et al. (2006), ISS passes against JPL
/// Horizons (tests/fixtures/horizons/iss_buenosaires_2026-11-21.csv, NASA's own trajectory, not a
/// TLE), the Earth's shadow, magnitudes and field crossings.
/// </summary>
public sealed class SatelliteTests
{
    private static readonly ObserverLocation BuenosAires = new(-34.60, -58.38, 25);

    /// <summary>CelesTrak GP elements of the ISS recorded 2026-11-20 (tests/fixtures/online/celestrak-stations.json).</summary>
    private static readonly SatelliteElements Iss = new(25544, "ISS (ZARYA)", "1998-067A",
        new DateTime(2026, 11, 20, 21, 9, 47, DateTimeKind.Utc).AddTicks(7515520),
        MeanMotionRevPerDay: 15.49518511, Eccentricity: 0.00073891, InclinationDegrees: 51.6319,
        AscendingNodeDegrees: 244.6605, ArgumentOfPerigeeDegrees: 48.0812, MeanAnomalyDegrees: 312.0806, BStar: 0.00018413556);

    private static string FixturePath(string folder, string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", folder, name);

    public static TheoryData<string> VerificationCases() => ["00005", "06251", "28057", "28350", "29238", "88888", "22312", "29141", "28872"];

    private static (SatelliteElements Elements, List<double[]> Expected) Verification(string catalogNumber)
    {
        var tle = File.ReadAllLines(FixturePath("satellites", "sgp4-ver-near-earth.tle")).Where(l => !l.StartsWith('#')).ToList();
        var index = tle.FindIndex(l => l.StartsWith('1') && l.Substring(2, 5) == catalogNumber);
        var elements = TwoLineElements.Parse("", tle[index], tle[index + 1])!;

        var expected = new List<double[]>();
        var inSection = false;
        foreach (var line in File.ReadLines(FixturePath("satellites", "tcppver-near-earth.out")))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            if (line.EndsWith(" xx", StringComparison.Ordinal))
            {
                inSection = line.StartsWith(catalogNumber, StringComparison.Ordinal);
                continue;
            }

            if (inSection)
            {
                expected.Add([.. line.Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture))]);
            }
        }

        return (elements, expected);
    }

    [Theory]
    [MemberData(nameof(VerificationCases))]
    public void Sgp4_ReproducesTheVerificationSet(string catalogNumber)
    {
        var (elements, expected) = Verification(catalogNumber);
        var sgp4 = Sgp4.Create(elements);

        expected.Should().NotBeEmpty();
        foreach (var row in expected)
        {
            var state = sgp4.Propagate(row[0]);
            // The reference is printed to 1e-8 km; the same algorithm in double precision agrees to well under a metre.
            new[] { state.PositionKm.X, state.PositionKm.Y, state.PositionKm.Z }
                .Should().Equal([row[1], row[2], row[3]], (a, b) => Math.Abs(a - b) < 1e-3, $"position at {row[0]} min");
            new[] { state.VelocityKmPerSecond.X, state.VelocityKmPerSecond.Y, state.VelocityKmPerSecond.Z }
                .Should().Equal([row[4], row[5], row[6]], (a, b) => Math.Abs(a - b) < 1e-6, $"velocity at {row[0]} min");
        }
    }

    [Theory]
    [InlineData("22312", 494.2028672)]
    [InlineData("29141", 440)]
    [InlineData("28872", 55)]
    public void Sgp4_ReportsDecayedOrbits(string catalogNumber, double minutes)
    {
        // The verification output of these three stops one step earlier, where SGP4 reports the decay.
        var sgp4 = Sgp4.Create(Verification(catalogNumber).Elements);

        FluentActions.Invoking(() => sgp4.Propagate(minutes)).Should().Throw<SatellitePropagationException>();
    }

    [Fact]
    public void TwoLineElements_AreReadColumnByColumn()
    {
        var elements = TwoLineElements.Parse(" VANGUARD 1 ",
            "1 00005U 58002B   00179.78495062  .00000023  00000-0  28098-4 0  4753",
            "2 00005  34.2682 348.7242 1859667 331.7664  19.3264 10.82419157413667")!;

        elements.Should().BeEquivalentTo(new
        {
            CatalogNumber = 5,
            Name = "VANGUARD 1",
            InternationalDesignator = "58002B",
            MeanMotionRevPerDay = 10.82419157,
            Eccentricity = 0.1859667,
            InclinationDegrees = 34.2682,
            AscendingNodeDegrees = 348.7242,
            ArgumentOfPerigeeDegrees = 331.7664,
            MeanAnomalyDegrees = 19.3264
        }, o => o.ExcludingMissingMembers());
        elements.BStar.Should().BeApproximately(0.28098e-4, 1e-12);
        elements.EpochUtc.Should().BeCloseTo(new DateTime(2000, 6, 27, 18, 50, 19, 733, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
        TwoLineElements.Parse("x", "1 too short", "2 too short").Should().BeNull();
    }

    [Fact]
    public void DeepSpaceOrbits_AreRejected()
    {
        var geostationary = Iss with { MeanMotionRevPerDay = 1.0027 };

        geostationary.IsNearEarth.Should().BeFalse();
        FluentActions.Invoking(() => Sgp4.Create(geostationary)).Should().Throw<ArgumentException>();
        TrackedSatellite.From([geostationary, Iss]).Should().ContainSingle().Which.CatalogNumber.Should().Be(25544);
    }

    [Fact]
    public void DockedModules_AreOneSatellite()
    {
        var poisk = Iss with { CatalogNumber = 36086, Name = "POISK" };

        var tracked = TrackedSatellite.From([poisk, Iss]);

        tracked.Should().ContainSingle().Which.Name.Should().Be("ISS (ZARYA)");
        tracked[0].IntrinsicMagnitude.Should().Be(-2.5, "McCants' quicksat.mag");
    }

    private static List<(DateTime Utc, double Azimuth, double Elevation, double RangeKm)> HorizonsIss() =>
        [.. File.ReadLines(FixturePath("horizons", "iss_buenosaires_2026-11-21.csv"))
            .Where(l => !l.StartsWith('#') && !l.StartsWith("utc", StringComparison.Ordinal))
            .Select(l => l.Split(','))
            .Select(p => (DateTime.SpecifyKind(DateTime.Parse(p[0], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture),
                double.Parse(p[3], CultureInfo.InvariantCulture)))];

    [Fact]
    public void Iss_FollowsHorizons_WithinTheElementSetsAccuracy()
    {
        var iss = new TrackedSatellite(Iss);

        foreach (var (utc, azimuth, elevation, range) in HorizonsIss().Where(r => r.Elevation > 5))
        {
            var observation = SatelliteCalculator.Observe(iss, BuenosAires, utc);
            // A day from the epoch, a GP element set is good to a few km along the track: ~0.5° at 500 km.
            observation.Horizontal.AltitudeDegrees.Should().BeApproximately(elevation, 0.6, $"altitude at {utc:HH:mm}");
            AzimuthDifference(observation.Horizontal.AzimuthDegrees, azimuth).Should().BeLessThan(1.5, $"azimuth at {utc:HH:mm}");
            observation.RangeKm.Should().BeApproximately(range, 15, $"range at {utc:HH:mm}");
        }
    }

    [Fact]
    public void IssPasses_MatchHorizons()
    {
        var iss = new TrackedSatellite(Iss);
        var horizons = HorizonsIss();
        var start = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

        var passes = SatelliteCalculator.FindPasses(iss, BuenosAires, start, start.AddHours(36));

        // Reference passes: the 10° crossings and the maximum of the Horizons minute table, interpolated.
        var reference = ReferencePasses(horizons, 10);
        passes.Should().HaveCount(reference.Count);
        foreach (var (pass, expected) in passes.Zip(reference))
        {
            pass.RiseUtc.Should().BeCloseTo(expected.Rise, TimeSpan.FromSeconds(30));
            pass.SetUtc.Should().BeCloseTo(expected.Set, TimeSpan.FromSeconds(30));
            // The minute table can miss a high peak by degrees (the ISS climbs ~0.3°/s near the zenith), never exceed it.
            pass.MaxAltitudeDegrees.Should().BeGreaterThan(expected.MaxAltitude - 0.6);
            pass.MaxUtc.Should().BeCloseTo(expected.MaxUtc, TimeSpan.FromSeconds(60));
            pass.MaxUtc.Should().BeAfter(pass.RiseUtc).And.BeBefore(pass.SetUtc);
        }
    }

    private static List<(DateTime Rise, DateTime Set, double MaxAltitude, DateTime MaxUtc)> ReferencePasses(
        List<(DateTime Utc, double Azimuth, double Elevation, double RangeKm)> rows, double threshold)
    {
        var passes = new List<(DateTime, DateTime, double, DateTime)>();
        DateTime? rise = null;
        var max = double.MinValue;
        var maxUtc = DateTime.MinValue;
        for (var i = 1; i < rows.Count; i++)
        {
            var (a, b) = (rows[i - 1], rows[i]);
            if ((b.Utc - a.Utc) != TimeSpan.FromMinutes(1))
            {
                continue;
            }

            DateTime Crossing() => a.Utc + TimeSpan.FromMinutes((threshold - a.Elevation) / (b.Elevation - a.Elevation));
            if (a.Elevation < threshold && b.Elevation >= threshold)
            {
                rise = Crossing();
                (max, maxUtc) = (b.Elevation, b.Utc);
            }
            else if (rise is not null && b.Elevation >= threshold)
            {
                if (b.Elevation > max)
                {
                    (max, maxUtc) = (b.Elevation, b.Utc);
                }
            }
            else if (rise is { } r && a.Elevation >= threshold && b.Elevation < threshold)
            {
                passes.Add((r, Crossing(), max, maxUtc));
                rise = null;
            }
        }

        return passes;
    }

    private static double AzimuthDifference(double a, double b) => Math.Abs(((a - b + 540) % 360) - 180);

    [Fact]
    public void Passes_KnowWhenTheIssIsVisible()
    {
        var iss = new TrackedSatellite(Iss);
        var start = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

        var passes = SatelliteCalculator.FindPasses(iss, BuenosAires, start, start.AddHours(36));

        passes.Should().Contain(p => p.IsVisible).And.Contain(p => !p.IsVisible);
        foreach (var pass in passes.Where(p => p.IsVisible))
        {
            pass.VisibleStartUtc.Should().BeOnOrAfter(pass.RiseUtc);
            pass.VisibleEndUtc.Should().BeOnOrBefore(pass.SetUtc).And.BeAfter(pass.VisibleStartUtc!.Value);
            pass.BrightestMagnitude.Should().BeInRange(-4.5, 2.5);
            // Visible means a dark sky at the start and a lit satellite.
            var observation = SatelliteCalculator.Observe(iss, BuenosAires, pass.VisibleStartUtc!.Value.AddSeconds(1));
            observation.Illumination.Should().NotBe(SatelliteIllumination.Umbra);
            Sun(pass.VisibleStartUtc!.Value).Should().BeLessThanOrEqualTo(SatelliteCalculator.DarkSkySunAltitudeDegrees + 0.05);
        }

        foreach (var pass in passes.Where(p => !p.IsVisible))
        {
            pass.VisibleEndUtc.Should().BeNull();
            pass.BrightestMagnitude.Should().BeNull();
        }
    }

    private static double Sun(DateTime utc) =>
        CoordinateTransformer.ToHorizontal(SolarSystemCalculator.GetSunPosition(utc).Equatorial, BuenosAires, utc).AltitudeDegrees;

    [Fact]
    public void Shadow_IsBehindTheEarth()
    {
        var sun = new Vector3d(149_597_870.7, 0, 0);
        const double r = 6378 + 420;

        SatelliteCalculator.Illumination(new Vector3d(0, r, 0), sun).Should().Be(SatelliteIllumination.Sunlit);
        SatelliteCalculator.Illumination(new Vector3d(r, 0, 0), sun).Should().Be(SatelliteIllumination.Sunlit);
        SatelliteCalculator.Illumination(new Vector3d(-r, 0, 0), sun).Should().Be(SatelliteIllumination.Umbra);
        // Just past the terminator the solar disc is half hidden (it is 0.53° wide).
        var edge = Math.Asin(6378.137 / r);
        SatelliteCalculator.Illumination(new Vector3d(-r * Math.Cos(edge), r * Math.Sin(edge), 0), sun)
            .Should().Be(SatelliteIllumination.Penumbra);
    }

    [Theory]
    [InlineData(1000, 0, -2.5)]
    [InlineData(1000, 90, -1.747)]
    [InlineData(500, 0, -4.005)]
    [InlineData(2000, 120, 0.510)]
    public void Magnitude_FollowsRangeAndPhase(double rangeKm, double phase, double expected) =>
        SatelliteCalculator.Magnitude(-2.5, rangeKm, phase).Should().BeApproximately(expected, 0.001);

    [Fact]
    public void Magnitude_IsUnknownWithoutAStandardMagnitude() =>
        SatelliteCalculator.Magnitude(null, 800, 40).Should().BeNull();

    [Fact]
    public void FieldCrossings_FindTheIssThroughTheCentre_AndNothingBeside()
    {
        var iss = new TrackedSatellite(Iss);
        // The middle of the longest visible stretch of the recorded day, as found by the pass search.
        var start = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);
        var visible = SatelliteCalculator.FindPasses(iss, BuenosAires, start, start.AddHours(36)).Where(p => p.IsVisible)
            .MaxBy(p => p.VisibleEndUtc!.Value - p.VisibleStartUtc!.Value)!;
        var middle = visible.VisibleStartUtc!.Value + ((visible.VisibleEndUtc!.Value - visible.VisibleStartUtc.Value) / 2);
        var centre = ToJ2000(SatelliteCalculator.Observe(iss, BuenosAires, middle).Equatorial, middle);
        var pass = new { MaxUtc = middle };

        var through = FieldCrossings.Find([iss], BuenosAires, new CameraField(centre, 2.0, 1.3), pass.MaxUtc.AddMinutes(-20), pass.MaxUtc.AddMinutes(20));
        var telescope = FieldCrossings.Find([iss], BuenosAires, new CameraField(centre, 0.3, 0.2, 35), pass.MaxUtc.AddMinutes(-20), pass.MaxUtc.AddMinutes(20));
        var beside = FieldCrossings.Find([iss], BuenosAires, new CameraField(Offset(centre, 15), 2.0, 1.3), pass.MaxUtc.AddMinutes(-20), pass.MaxUtc.AddMinutes(20));

        through.Should().ContainSingle();
        through[0].EnterUtc.Should().BeBefore(pass.MaxUtc);
        through[0].ExitUtc.Should().BeAfter(pass.MaxUtc);
        (through[0].ExitUtc - through[0].EnterUtc).Should().BeLessThan(TimeSpan.FromSeconds(10), "the ISS crosses 2° in a few seconds");
        through[0].Magnitude.Should().NotBeNull();
        telescope.Should().ContainSingle("a 0.3° field is crossed in a fraction of a second, but still caught");
        beside.Should().BeEmpty();
    }

    [Fact]
    public void FieldCrossings_IgnoreSatellitesInTheShadow()
    {
        var iss = new TrackedSatellite(Iss);
        var start = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);
        var dark = SatelliteCalculator.FindPasses(iss, BuenosAires, start, start.AddHours(36))
            .First(p => SatelliteCalculator.Observe(iss, BuenosAires, p.MaxUtc).Illumination == SatelliteIllumination.Umbra);
        var atMax = SatelliteCalculator.Observe(iss, BuenosAires, dark.MaxUtc);

        FieldCrossings.Find([iss], BuenosAires, new CameraField(ToJ2000(atMax.Equatorial, dark.MaxUtc), 2, 2), dark.MaxUtc.AddMinutes(-5), dark.MaxUtc.AddMinutes(5))
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 1.0, 0.0, true)]
    [InlineData(0, 0.0, 0.7, false)]
    [InlineData(90, 0.0, 0.9, true)]
    [InlineData(90, 1.0, 0.0, false)]
    public void Field_IsARotatedRectangle(double rotation, double eastDegrees, double northDegrees, bool inside)
    {
        var centre = new EquatorialCoordinates(5.5, 10);
        var position = new EquatorialCoordinates(5.5 + (eastDegrees / 15 / Math.Cos(10 * Math.PI / 180)), 10 + northDegrees);

        FieldCrossings.Inside(position, centre, new CameraField(centre, 2.2, 1.2, rotation)).Should().Be(inside);
    }

    private static EquatorialCoordinates ToJ2000(EquatorialCoordinates ofDate, DateTime utc) =>
        Precession.Precess(ofDate, AstronomyMath.ToJulianDate(utc), 2451545.0);

    private static EquatorialCoordinates Offset(EquatorialCoordinates c, double degrees) =>
        new(c.RightAscensionHours, Math.Clamp(c.DeclinationDegrees + degrees, -89, 89));
}
