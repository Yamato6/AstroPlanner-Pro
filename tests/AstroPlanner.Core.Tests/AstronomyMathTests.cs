using AstroPlanner.Core.Algorithms;

namespace AstroPlanner.Core.Tests;

public class AstronomyMathTests
{
    [Fact]
    public void ToJulianDate_J2000Epoch_Returns2451545()
    {
        // 2000-01-01 12:00:00 UTC is exactly the J2000.0 epoch.
        var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var jd = AstronomyMath.ToJulianDate(j2000);

        jd.Should().BeApproximately(2451545.0, 1e-6);
    }

    [Fact]
    public void ToJulianDate_KnownDate_MatchesReferenceValue()
    {
        // Reference Julian date: 2023-10-15 00:00:00 UTC -> JD 2460232.5
        var date = new DateTime(2023, 10, 15, 0, 0, 0, DateTimeKind.Utc);

        var jd = AstronomyMath.ToJulianDate(date);

        jd.Should().BeApproximately(2460232.5, 1e-6);
    }

    [Fact]
    public void ToJulianDate_LocalTime_IsConvertedToUtc()
    {
        // A DateTime marked as Local must be converted to UTC before the calculation.
        var utc = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var local = utc.ToLocalTime();

        var jdUtc = AstronomyMath.ToJulianDate(utc);
        var jdLocal = AstronomyMath.ToJulianDate(local);

        jdLocal.Should().BeApproximately(jdUtc, 1e-6);
    }

    [Fact]
    public void CalculateGreenwichMeanSiderealTime_J2000_ReturnsExpected()
    {
        // GMST at the J2000.0 epoch must be 280.46061837 degrees (the formula's constant).
        var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var gmst = AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(j2000);

        gmst.Should().BeApproximately(280.46061837, 1e-6);
    }

    [Fact]
    public void CalculateLocalSiderealTime_GreenwichOffset_IsZero()
    {
        // At longitude 0 (Greenwich), LST == GMST.
        var date = new DateTime(2023, 10, 15, 22, 0, 0, DateTimeKind.Utc);

        var gmst = AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(date);
        var lst = AstronomyMath.CalculateLocalSiderealTimeDegrees(date, 0);

        lst.Should().BeApproximately(gmst, 1e-9);
    }

    [Fact]
    public void CalculateLocalSiderealTime_PositiveLongitude_AddsToGmst()
    {
        var date = new DateTime(2023, 10, 15, 22, 0, 0, DateTimeKind.Utc);

        var gmst = AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(date);
        var lst = AstronomyMath.CalculateLocalSiderealTimeDegrees(date, 15);

        // 15° east longitude => LST = GMST + 15, normalized to [0, 360).
        var expected = (gmst + 15.0) % 360.0;
        lst.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void NormalizeDegrees_NegativeInput_ReturnsPositiveEquivalent()
    {
        AstronomyMath.NormalizeDegrees(-10).Should().BeApproximately(350, 1e-9);
        AstronomyMath.NormalizeDegrees(-360).Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void NormalizeDegrees_LargeInput_ReturnsWithinRange()
    {
        AstronomyMath.NormalizeDegrees(725).Should().BeApproximately(5, 1e-9);
        AstronomyMath.NormalizeDegrees(360).Should().BeApproximately(0, 1e-9);
    }
}
