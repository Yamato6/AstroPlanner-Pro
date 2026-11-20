using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.SkyQuality;
using Xunit.Abstractions;

namespace AstroPlanner.Core.Tests;

/// <summary>Spec §8.6: sanity scenarios, monotonicity and edge cases of the feasibility model.</summary>
public sealed class FeasibilityTests(ITestOutputHelper output)
{
    private static readonly TimeZoneInfo MexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
    private static readonly ObserverLocation Aguascalientes = new(21.88, -102.29, 1888);

    /// <summary>Bortle 7 (spec §1.3: the owner's urban sky).</summary>
    private const double Bortle7Sqm = 18.6;

    private const double DarkSiteSqm = 21.6;

    private static readonly CelestialObject M57 = new(
        "M57", "Ring Nebula", "Lyra", CelestialObjectType.PlanetaryNebula, 8.8, new EquatorialCoordinates(18.8932, 33.0292))
    {
        MajorAxisArcmin = 1.4,
        MinorAxisArcmin = 1.0
    };

    private readonly AstronomyService _astronomy = new();
    private FeasibilityCalculator Calculator => new(_astronomy);

    private static RigSetup Rokinon16 => EquipmentInventory.Demo.ActiveRig!;

    /// <summary>T7 behind a 130 mm f/5 Newtonian on an unguided equatorial mount.</summary>
    private static RigSetup Newtonian650OnEq(double apertureMm = 130)
    {
        var demo = EquipmentInventory.Demo.Resolve("t7-starsense-130")!;
        return demo with
        {
            Optic = demo.Optic with { ApertureMm = apertureMm },
            Mount = new Mount("eq", "EQ mount", MountKind.Equatorial)
        };
    }

    private FeasibilityRequest Request(DateTime localEvening, RigSetup rig, double sqm, double minimumAltitude = 20,
        ObserverLocation? observer = null, WeatherForecast? weather = null, TimeZoneInfo? zone = null)
    {
        var site = observer ?? Aguascalientes;
        var tz = zone ?? MexicoZone;
        var night = _astronomy.GetNightInfo(site, localEvening, tz);
        return new FeasibilityRequest(site, tz, night, minimumAltitude, rig, sqm, SkyQualitySource.Manual, Weather: weather);
    }

    private FeasibilityReport Evaluate(CelestialObject target, FeasibilityRequest request)
    {
        var report = Calculator.Evaluate(target, request);
        output.WriteLine($"{target.Id}: {report.Verdict} {report.Score} SNR {report.SignalToNoise:0.0} sub {report.SubSeconds}s ×{report.SubCount} " +
                         $"usable {report.UsableHours:0.00} h sky {report.MeanSkySqm:0.00} framing {report.Framing.Class} {report.Framing.MajorPixels:0}px");
        foreach (var reason in report.Reasons)
        {
            output.WriteLine($"  {reason.Impact} {reason.Factor} {reason.Value:0.###} {reason.Value2:0.###} {reason.Value3:0.###}");
        }

        return report;
    }

    // --- Sanity scenarios (spec §8.6) -----------------------------------------------------------

    [Fact]
    public void M31_FromBortle7_With16mmOnAFixedTripod_IsFeasibleAsAWideField()
    {
        // New Moon on 2026-10-10.
        var report = Evaluate(TestCatalog.M31, Request(new DateTime(2026, 10, 10, 21, 0, 0), Rokinon16, Bortle7Sqm));

        report.Verdict.Should().BeOneOf(FeasibilityVerdict.Marginal, FeasibilityVerdict.Good, FeasibilityVerdict.Excellent);
        report.Framing.Class.Should().Be(FramingClass.Small);
        report.SubLimit.Should().BeOneOf(ExposureLimitSource.Npf, ExposureLimitSource.SkyLimited);
        report.SubSeconds.Should().BeLessThanOrEqualTo(ExposureLimits.NpfAtDeclination(ExposureLimits.Npf(2, 3.72, 16), 41.3));
        report.Recommended!.Iso.Should().Be(800);
        report.Recommended.SubCount.Should().BeGreaterThan(100);
    }

    [Fact]
    public void M57_With16mm_IsTooSmall()
    {
        // New Moon on 2026-07-14.
        var report = Evaluate(M57, Request(new DateTime(2026, 7, 14, 21, 0, 0), Rokinon16, Bortle7Sqm));

        report.Framing.Class.Should().Be(FramingClass.TooSmall);
        report.Framing.MajorPixels.Should().BeLessThan(3);
        report.Verdict.Should().BeOneOf(FeasibilityVerdict.Unlikely, FeasibilityVerdict.NotPossible);
        report.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Framing && r.Impact == ReasonImpact.Negative);
    }

    [Fact]
    public void OrionNebula_With650mmF5OnAnEquatorialMount_InADarkWinterNight_IsExcellent()
    {
        // New Moon on 2026-12-09, dark site.
        var report = Evaluate(TestCatalog.M42, Request(new DateTime(2026, 12, 9, 21, 0, 0), Newtonian650OnEq(), DarkSiteSqm));

        report.Verdict.Should().Be(FeasibilityVerdict.Excellent);
        report.Framing.Class.Should().Be(FramingClass.Good);
        report.SubSeconds.Should().Be(60);
        report.SubLimit.Should().Be(ExposureLimitSource.Mount);
        report.UsableHours.Should().BeGreaterThan(5);
    }

    // --- Monotonicity ----------------------------------------------------------------------------

    private static readonly DateTime WinterEvening = new(2026, 12, 9, 21, 0, 0);

    [Fact]
    public void BrighterSky_LowersTheScore()
    {
        var dark = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), 21.6));
        var suburban = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), 19.5));
        var city = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), 17.5));

        dark.SignalToNoise.Should().BeGreaterThan(suburban.SignalToNoise);
        suburban.SignalToNoise.Should().BeGreaterThan(city.SignalToNoise);
        dark.Score.Should().BeGreaterThanOrEqualTo(suburban.Score);
        suburban.Score.Should().BeGreaterThan(city.Score);
    }

    [Fact]
    public void LongerUsableTime_RaisesTheScore()
    {
        var low = Evaluate(TestCatalog.M31, Request(new DateTime(2026, 10, 10, 21, 0, 0), Rokinon16, Bortle7Sqm, minimumAltitude: 20));
        var high = Evaluate(TestCatalog.M31, Request(new DateTime(2026, 10, 10, 21, 0, 0), Rokinon16, Bortle7Sqm, minimumAltitude: 55));

        low.UsableHours.Should().BeGreaterThan(high.UsableHours);
        low.SignalToNoise.Should().BeGreaterThan(high.SignalToNoise);
        low.Score.Should().BeGreaterThan(high.Score);
    }

    [Fact]
    public void BiggerAperture_RaisesTheScore()
    {
        var small = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(80), 19));
        var large = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(130), 19));

        large.SignalToNoise.Should().BeGreaterThan(small.SignalToNoise);
        large.Score.Should().BeGreaterThanOrEqualTo(small.Score);
    }

    [Fact]
    public void FullMoonNearTheTarget_BrightensTheSky_AndSaysSo()
    {
        // Full Moon on 2026-12-24, in Gemini: a few tens of degrees from M42.
        var newMoon = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm));
        var fullMoon = Evaluate(TestCatalog.M42, Request(new DateTime(2026, 12, 24, 21, 0, 0), Newtonian650OnEq(), DarkSiteSqm));

        fullMoon.MeanSkySqm.Should().BeLessThan(newMoon.MeanSkySqm - 1.5);
        fullMoon.Score.Should().BeLessThan(newMoon.Score);
        fullMoon.MoonIlluminatedFraction.Should().BeGreaterThan(0.9);
        fullMoon.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Moonlight && r.Impact == ReasonImpact.Negative);
    }

    // --- Edge cases --------------------------------------------------------------------------------

    [Fact]
    public void TargetThatNeverRises_IsNotPossible()
    {
        var southern = new CelestialObject("S", "Far south", "Octans", CelestialObjectType.Galaxy, 8, new EquatorialCoordinates(4, -80))
        {
            MajorAxisArcmin = 10
        };

        var report = Evaluate(southern, Request(WinterEvening, Rokinon16, Bortle7Sqm));

        report.Verdict.Should().Be(FeasibilityVerdict.NotPossible);
        report.Score.Should().Be(0);
        report.Recommended.Should().BeNull();
        report.Reasons.Should().ContainSingle().Which.Factor.Should().Be(FeasibilityFactor.NeverAboveMinimum);
    }

    [Fact]
    public void PolarDay_HasNoDarkness()
    {
        var svalbard = new ObserverLocation(78.2, 15.6);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        var report = Evaluate(TestCatalog.M31, Request(new DateTime(2026, 6, 21, 23, 0, 0), Rokinon16, 21, observer: svalbard, zone: zone));

        report.Verdict.Should().Be(FeasibilityVerdict.NotPossible);
        report.Reasons.Should().ContainSingle().Which.Factor.Should().Be(FeasibilityFactor.NoDarkness);
    }

    [Fact]
    public void ZeroClearHours_IsNotPossible()
    {
        var night = _astronomy.GetNightInfo(Aguascalientes, WinterEvening, MexicoZone);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(night.WindowStartLocal, MexicoZone).AddHours(-2);
        var overcast = new WeatherForecast(21.88, -102.29,
            [.. Enumerable.Range(0, 20).Select(h => new HourlyWeather(startUtc.AddHours(h), 100, 100, 100, 100, 0, 90))],
            startUtc);

        var report = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm, weather: overcast));

        report.Verdict.Should().Be(FeasibilityVerdict.NotPossible);
        report.HasForecast.Should().BeTrue();
        report.GeometricHours.Should().BeGreaterThan(5);
        report.Reasons.Should().ContainSingle().Which.Factor.Should().Be(FeasibilityFactor.NoClearTime);
    }

    [Fact]
    public void PartialClouds_WeighTheUsableTime()
    {
        var night = _astronomy.GetNightInfo(Aguascalientes, WinterEvening, MexicoZone);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(night.WindowStartLocal, MexicoZone).AddHours(-2);
        var halfCloudy = new WeatherForecast(21.88, -102.29,
            [.. Enumerable.Range(0, 20).Select(h => new HourlyWeather(startUtc.AddHours(h), 50, 50, 0, 0, 0, 50))],
            startUtc);

        var clear = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm));
        var cloudy = Evaluate(TestCatalog.M42, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm, weather: halfCloudy));

        cloudy.UsableHours.Should().BeApproximately(clear.UsableHours / 2, 0.05);
        cloudy.MeanCloudPercent.Should().BeApproximately(50, 0.1);
        cloudy.SignalToNoise.Should().BeLessThan(clear.SignalToNoise);
        cloudy.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Clouds);
    }

    [Fact]
    public void HorizonMask_RemovesTimeBehindObstacles()
    {
        var request = Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm);
        var masked = request with { HorizonMask = new HorizonMask([new HorizonPoint(0, 60), new HorizonPoint(180, 60)]) };

        var open = Calculator.Evaluate(TestCatalog.M42, request);
        var blocked = Calculator.Evaluate(TestCatalog.M42, masked);

        blocked.UsableHours.Should().BeLessThan(open.UsableHours);
    }

    [Fact]
    public void NauticalDarkness_AddsTimeButBrightensTheSky()
    {
        // M31 is high at dusk in December: nautical twilight adds imaging time.
        var request = Request(WinterEvening, Rokinon16, DarkSiteSqm);
        var astronomical = Calculator.Evaluate(TestCatalog.M31, request);
        var nautical = Calculator.Evaluate(TestCatalog.M31, request with { Darkness = DarknessDefinition.Nautical });

        nautical.GeometricHours.Should().BeGreaterThan(astronomical.GeometricHours);
        nautical.MeanSkySqm.Should().BeLessThan(astronomical.MeanSkySqm);
    }

    [Fact]
    public void LargeObject_SuggestsAMosaic()
    {
        var huge = new CelestialObject("Big", "Huge", "Orion", CelestialObjectType.DiffuseNebula, 3, new EquatorialCoordinates(5.6, -1))
        {
            MajorAxisArcmin = 300,
            MinorAxisArcmin = 200
        };

        var report = Evaluate(huge, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm));

        report.Framing.Class.Should().Be(FramingClass.TooLarge);
        report.Framing.MosaicPanels.Should().BeGreaterThan(4);
        report.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Mosaic);
    }

    [Fact]
    public void ObjectWithoutPhotometry_AssumesAFaintSurface_AndSaysSo()
    {
        var unknown = new CelestialObject("U", "Unknown", "Orion", CelestialObjectType.DarkNebula, double.NaN, new EquatorialCoordinates(5.5, -2))
        {
            MajorAxisArcmin = 20
        };

        var report = Evaluate(unknown, Request(WinterEvening, Newtonian650OnEq(), DarkSiteSqm));

        report.BrightnessAssumed.Should().BeTrue();
        report.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.AssumedBrightness);
    }

    [Fact]
    public void Planets_AreFramedByTheirDisc()
    {
        var jupiter = CelestialObject.CreatePlanet("jupiter", "Jupiter", Planet.Jupiter);
        var middle = DateTime.SpecifyKind(new DateTime(2026, 12, 10, 6, 0, 0), DateTimeKind.Utc);
        var diameterArcsec = _astronomy.GetPlanetPosition(jupiter, middle)!.ApparentDiameterArcsec;

        var wide = Evaluate(jupiter, Request(WinterEvening, Rokinon16, Bortle7Sqm, minimumAltitude: 0));
        var telescope = Evaluate(jupiter, Request(WinterEvening, Newtonian650OnEq(), Bortle7Sqm, minimumAltitude: 0));

        wide.IsPointLike.Should().BeFalse();
        wide.Framing.Class.Should().Be(FramingClass.TooSmall);
        wide.Verdict.Should().BeOneOf(FeasibilityVerdict.Unlikely, FeasibilityVerdict.NotPossible);
        telescope.Framing.MajorPixels.Should().BeApproximately(diameterArcsec / Newtonian650OnEq().ImageScaleArcsecPerPixel, 1);
        telescope.Framing.Class.Should().NotBe(FramingClass.TooSmall);
    }

    [Fact]
    public void Stars_AreAPointOfLight_AndNeverBetterThanUnlikely()
    {
        var vega = new CelestialObject("Vega", "Vega", "Lyra", CelestialObjectType.Star, 0.03, new EquatorialCoordinates(18.6156, 38.7837));

        var report = Evaluate(vega, Request(new DateTime(2026, 7, 14, 21, 0, 0), Rokinon16, Bortle7Sqm));

        report.IsPointLike.Should().BeTrue();
        report.Framing.Class.Should().Be(FramingClass.PointLike);
        report.Verdict.Should().Be(FeasibilityVerdict.Unlikely);
        report.Reasons.Should().Contain(r => r.Factor == FeasibilityFactor.Framing && r.Impact == ReasonImpact.Negative);
    }

    [Fact]
    public void Reasons_ListBlockingAndNegativeFactorsFirst()
    {
        var report = Evaluate(M57, Request(new DateTime(2026, 7, 14, 21, 0, 0), Rokinon16, Bortle7Sqm));

        var order = report.Reasons.Select(r => r.Impact switch
        {
            ReasonImpact.Blocking => 0,
            ReasonImpact.Negative => 1,
            ReasonImpact.Positive => 2,
            _ => 3
        }).ToList();
        order.Should().BeInAscendingOrder();
    }

    [Fact]
    public void WholeCatalog_IsEvaluatedQuickly_InTheOrderGiven()
    {
        var random = new Random(42);
        var targets = Enumerable.Range(0, 13_000).Select(i => new CelestialObject(
            $"T{i}", "Test", "Test", CelestialObjectType.Galaxy, 8 + random.NextDouble() * 6,
            new EquatorialCoordinates(random.NextDouble() * 24, random.NextDouble() * 150 - 70))
        {
            MajorAxisArcmin = 0.5 + random.NextDouble() * 30
        }).ToList();
        var request = Request(WinterEvening, Rokinon16, Bortle7Sqm);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var reports = Calculator.EvaluateAll(targets, request);
        watch.Stop();

        reports.Should().HaveCount(targets.Count);
        reports[17].Should().BeEquivalentTo(Calculator.Evaluate(targets[17], request));
        output.WriteLine($"13 000 targets in {watch.ElapsedMilliseconds} ms");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
