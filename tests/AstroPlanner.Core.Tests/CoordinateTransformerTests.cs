using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

public class CoordinateTransformerTests
{
    [Fact]
    public void ToHorizontal_ObjectAtZenith_ReturnsAltitude90()
    {
        // At J2000.0 (2000-01-01 12:00 UTC) GMST is 280.46061837°. At longitude 0, LST = GMST.
        // With the object's RA exactly at the LST and its Dec equal to the latitude, the hour
        // angle H = 0 and the object passes through the zenith (altitude 90°).
        var instant = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var latitude = 40.0;
        var raDegrees = AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(instant);
        var raHours = raDegrees / 15.0;

        var equatorial = new EquatorialCoordinates(raHours, latitude);
        var observer = new ObserverLocation(latitude, 0);

        var horizontal = CoordinateTransformer.ToHorizontal(equatorial, observer, instant);

        horizontal.AltitudeDegrees.Should().BeApproximately(90.0, 1e-4);
    }

    [Fact]
    public void ToHorizontal_ObjectOnMeridianSouth_ReturnsAzimuth180()
    {
        // An object with Dec = 0 crossing the meridian (H = 0) for an observer at a positive
        // (northern) latitude is exactly due south (azimuth 180°).
        var instant = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var latitude = 40.0;
        var raHours = AstronomyMath.CalculateGreenwichMeanSiderealTimeDegrees(instant) / 15.0;

        var equatorial = new EquatorialCoordinates(raHours, 0.0);
        var observer = new ObserverLocation(latitude, 0);

        var horizontal = CoordinateTransformer.ToHorizontal(equatorial, observer, instant);

        horizontal.AzimuthDegrees.Should().BeApproximately(180.0, 1e-4);
        // Altitude at transit = 90 - lat + dec = 90 - 40 + 0 = 50°.
        horizontal.AltitudeDegrees.Should().BeApproximately(50.0, 1e-4);
    }

    [Fact]
    public void ToHorizontal_ObjectAtSixHoursHourAngle_IsWest_Azimuth270()
    {
        // With H = 90° (6h hour angle), lat = 0 and Dec = 0, the object is on the western
        // horizon (azimuth 270°, altitude 0°). RA is derived from the instant's LST at
        // Greenwich so that H = LST - RA = 90°.
        var latitude = 0.0;
        var declination = 0.0;
        var instant = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var lst = AstronomyMath.CalculateLocalSiderealTimeDegrees(instant, 0);
        var raHours = AstronomyMath.NormalizeDegrees(lst - 90.0) / 15.0;

        var equatorial = new EquatorialCoordinates(raHours, declination);
        var observer = new ObserverLocation(latitude, 0);

        var horizontal = CoordinateTransformer.ToHorizontal(equatorial, observer, instant);

        // With lat = 0 and dec = 0, H = 90° => altitude 0°, azimuth 270° (west).
        horizontal.AltitudeDegrees.Should().BeApproximately(0.0, 1e-4);
        horizontal.AzimuthDegrees.Should().BeApproximately(270.0, 1e-4);
    }

    [Fact]
    public void ToHorizontal_AzimuthStaysWithinRange_ForManyInstants()
    {
        // 24 h sweep: the azimuth must always stay in [0, 360).
        var equatorial = new EquatorialCoordinates(5.59, -5.39); // M42, Orion
        var observer = new ObserverLocation(40.4, -3.7); // Madrid

        var start = new DateTime(2023, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 96; i++) // 24 h every 15 min
        {
            var t = start.AddMinutes(15 * i);
            var h = CoordinateTransformer.ToHorizontal(equatorial, observer, t);

            h.AltitudeDegrees.Should().BeInRange(-90.0, 90.0);
            h.AzimuthDegrees.Should().BeInRange(0.0, 360.0);
        }
    }
}
