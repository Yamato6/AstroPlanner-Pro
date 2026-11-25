using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Tests;

/// <summary>Camera field of view (spec ﾂｧ8.2) and the sky-brightness conversions and heuristic (spec ﾂｧ8.4).</summary>
public sealed class SkyQualityTests
{
    [Fact]
    public void FieldOfView_IsTwiceTheArcTangentOfHalfTheSensorOverTheFocalLength()
    {
        // Full frame behind a 50 mm lens: the textbook 39.6ﾂｰ ﾃ・27.0ﾂｰ (46.8ﾂｰ diagonal).
        var fov = OpticsMath.FieldOfView(36, 24, 50);
        fov.WidthDegrees.Should().BeApproximately(39.5978, 1e-4);
        fov.HeightDegrees.Should().BeApproximately(26.9915, 1e-4);
        OpticsMath.FieldOfViewDegrees(Math.Sqrt(36 * 36 + 24 * 24), 50).Should().BeApproximately(46.7930, 1e-4);

        // APS-C astro camera on a 400 mm refractor: 竕・3.37ﾂｰ ﾃ・2.25ﾂｰ.
        OpticsMath.FieldOfViewDegrees(23.5, 400).Should().BeApproximately(3.3652, 1e-4);

        var act = () => OpticsMath.FieldOfViewDegrees(36, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ImageScale_Is206265TimesThePixelOverTheFocalLength()
    {
        OpticsMath.ImageScaleArcsecPerPixel(3.76, 400).Should().BeApproximately(1.9389, 1e-4);
        OpticsMath.ImageScaleArcsecPerPixel(4.3, 1000).Should().BeApproximately(0.8869, 1e-4);
    }

    [Fact]
    public void Sqm_AndLuminance_FollowTheSpecZeroPoint()
    {
        // Spec ﾂｧ8.4: natural sky 竕・0.171 mcd/mﾂｲ 竕・22.0 mag/arcsecﾂｲ.
        SkyBrightness.LuminanceFromSqm(22.0).Should().BeApproximately(0.171, 0.001);
        SkyBrightness.SqmFromLuminance(0.171).Should().BeApproximately(22.0, 0.01);
        SkyBrightness.SqmFromLuminance(SkyBrightness.LuminanceFromSqm(18.37)).Should().BeApproximately(18.37, 1e-9);
    }

    [Fact]
    public void Combine_AddsInLinearUnits()
    {
        // Two equal skies are 2.5ﾂｷlog10(2) 竕・0.753 mag brighter.
        SkyBrightness.Combine(21, 21).Should().BeApproximately(21 - 0.7526, 1e-4);
        SkyBrightness.Combine(21, double.PositiveInfinity).Should().BeApproximately(21, 1e-12);
        SkyBrightness.Combine(double.PositiveInfinity).Should().Be(double.PositiveInfinity);
        SkyBrightness.Combine(10, 22).Should().BeApproximately(10, 1e-4);
    }

    [Theory]
    [InlineData(22.0, 6.62)]
    [InlineData(21.5, 6.38)]
    [InlineData(20.0, 5.49)]
    [InlineData(18.5, 4.38)]
    public void LimitingMagnitude_FollowsTheSchaeferConversion(double sqm, double expected) =>
        SkyBrightness.NakedEyeLimitingMagnitude(sqm).Should().BeApproximately(expected, 0.01);

    [Theory]
    [InlineData(22.1, 1)]
    [InlineData(21.95, 2)]
    [InlineData(21.75, 3)]
    [InlineData(21.0, 4)]
    [InlineData(20.0, 5)]
    [InlineData(19.2, 6)]
    [InlineData(18.6, 7)]
    [InlineData(18.0, 8)]
    [InlineData(17.0, 9)]
    public void Bortle_IsADisplayFromTheSqm(double sqm, int expected) =>
        SkyBrightness.BortleFromSqm(sqm).Should().Be(expected);

    [Fact]
    public void Twilight_BrightensTheSkyAsTheSunRises_AndVanishesBelowMinus18()
    {
        SkyBrightness.Twilight(-20).Should().Be(double.PositiveInfinity);
        SkyBrightness.Twilight(-12).Should().BeApproximately(17.5, 1e-12);
        SkyBrightness.Twilight(-9).Should().BeApproximately(14.75, 1e-12);
        SkyBrightness.Twilight(0).Should().BeApproximately(7.0, 1e-12);
        SkyBrightness.Twilight(40).Should().BeApproximately(4.0, 1e-12);

        var previous = double.PositiveInfinity;
        for (var altitude = -18.0; altitude <= 12; altitude += 0.5)
        {
            var sqm = SkyBrightness.Twilight(altitude);
            sqm.Should().BeLessThanOrEqualTo(previous);
            previous = sqm;
        }
    }

    [Fact]
    public void Appearance_IsTheFeasibilityModelAtTheZenith()
    {
        // Aguascalientes, 2026-03-03 06:00 UTC: Moon full (03-03) and high, Sun far below.
        var observer = new ObserverLocation(21.88, -102.29, 1888);
        var utc = new DateTime(2026, 3, 3, 6, 0, 0, DateTimeKind.Utc);

        var sample = SkyAppearanceCalculator.At(observer, utc, siteSqm: 21.5, extinction: 0.2);

        sample.MoonAltitudeDegrees.Should().BeGreaterThan(40);
        var moon = SolarSystemCalculator.GetMoonPosition(utc);
        var sun = SolarSystemCalculator.GetSunPosition(utc);
        var expected = SkyBackground.At(21.5, 90, sample.SunAltitudeDegrees, true, sample.MoonAltitudeDegrees,
            SolarSystemCalculator.GetMoonPhaseAngleDegrees(moon, sun), 90 - sample.MoonAltitudeDegrees, 0.2);
        sample.ZenithSqm.Should().BeApproximately(expected.TotalSqm, 1e-12);
        sample.MoonlightSqm.Should().BeApproximately(expected.MoonlightSqm, 1e-12);
        sample.MoonlightSqm.Should().BeInRange(17, 19.5, "a high full Moon washes the zenith out to about mag 18");
        sample.ZenithSqm.Should().BeLessThan(sample.MoonlightSqm);
        sample.LimitingMagnitude.Should().BeApproximately(SkyBrightness.NakedEyeLimitingMagnitude(sample.ZenithSqm), 1e-12);
    }

    [Fact]
    public void Appearance_SamplesTheWindow_DarkAtMidnight_BrightAtNoon()
    {
        // Aguascalientes, 2026-03-20 (new Moon on 03-19): local midnight is 06:00 UTC, noon 18:00 UTC.
        var observer = new ObserverLocation(21.88, -102.29, 1888);
        var start = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc);

        var samples = SkyAppearanceCalculator.Sample(observer, start, start.AddHours(24), TimeSpan.FromHours(1), siteSqm: 21.3);

        samples.Should().HaveCount(25);
        samples[0].Utc.Should().Be(start);
        var midnight = samples[6];
        midnight.SunAltitudeDegrees.Should().BeLessThan(-40);
        midnight.ZenithSqm.Should().BeApproximately(21.3, 0.01);
        midnight.LimitingMagnitude.Should().BeApproximately(SkyBrightness.NakedEyeLimitingMagnitude(midnight.ZenithSqm), 1e-9);
        var noon = samples[18];
        noon.SunAltitudeDegrees.Should().BeGreaterThan(60);
        noon.ZenithSqm.Should().BeApproximately(4.0, 0.01);
        noon.LimitingMagnitude.Should().BeLessThan(0);
        midnight.SiteSqm.Should().BeApproximately(21.3, 1e-3, "the Kasten & Young air mass at the zenith is not exactly 1");
        midnight.TwilightSqm.Should().Be(double.PositiveInfinity);
        noon.TwilightSqm.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void Location_AcceptsAPlausibleSqm_Only()
    {
        var site = new Location("a", "Site", 21.88, -102.29, 1888, "America/Mexico_City", "Central Standard Time (Mexico)", zenithSqm: 21.2);
        site.ZenithSqm.Should().Be(21.2);
        new Location("b", "Site", 0, 0, 0, "Etc/UTC", "UTC").ZenithSqm.Should().BeNull();

        var act = () => new Location("c", "Site", 0, 0, 0, "Etc/UTC", "UTC", zenithSqm: 25);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
