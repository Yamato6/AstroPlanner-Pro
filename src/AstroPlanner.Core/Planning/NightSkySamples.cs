using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Planning;

/// <summary>
/// The night sampled every few minutes, with everything that does not depend on the target:
/// sidereal time, Sun altitude (darkness), the Moon (topocentric altitude, phase, direction) and
/// cloud cover. Shared by every target of a feasibility run (spec §8.5 "usable time").
/// Sample k stands for the interval [Times[k], Times[k+1]); its length is DurationsSeconds[k].
/// </summary>
public sealed class NightSkySamples
{
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Sun altitude limits of the darkness definitions.</summary>
    public const double AstronomicalDarknessDegrees = -18;
    public const double NauticalDarknessDegrees = -12;

    private NightSkySamples()
    {
    }

    public required DateTime[] TimesUtc { get; init; }
    public required double[] DurationsSeconds { get; init; }
    public required double[] LocalSiderealTimeDegrees { get; init; }
    public required double[] SunAltitudeDegrees { get; init; }
    public required bool[] IsDark { get; init; }
    public required double[] MoonAltitudeDegrees { get; init; }
    public required double[] MoonPhaseAngleDegrees { get; init; }
    public required double[] MoonIlluminatedFraction { get; init; }

    /// <summary>Unit vectors of the Moon (equator of date): x, y, z per sample.</summary>
    public required double[] MoonVectors { get; init; }

    /// <summary>Cloud cover 0–1 per sample (0 without forecast).</summary>
    public required double[] CloudFraction { get; init; }

    public required bool HasForecast { get; init; }
    public required double SinLatitude { get; init; }
    public required double CosLatitude { get; init; }
    public required double MiddleJulianDate { get; init; }
    public required double RefractionScale { get; init; }
    public required double DarknessLimitDegrees { get; init; }
    public int Count => TimesUtc.Length;

    /// <summary>Samples the night window of <paramref name="night"/> (sunset → sunrise).</summary>
    /// <param name="darknessLimitDegrees">Sun altitude at or below which a sample counts as dark.</param>
    /// <param name="weather">Hourly forecast; cloud cover weighs the usable time when present.</param>
    public static NightSkySamples Create(
        ObserverLocation observer,
        TimeZoneInfo timeZone,
        NightInfo night,
        TimeSpan step,
        double darknessLimitDegrees = AstronomicalDarknessDegrees,
        WeatherForecast? weather = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(night);
        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The step must be positive.");
        }

        var startUtc = TimeZoneConversion.ToUtc(night.WindowStartLocal, timeZone);
        var endUtc = TimeZoneConversion.ToUtc(night.WindowEndLocal, timeZone);
        if (endUtc <= startUtc)
        {
            endUtc = startUtc.AddMinutes(1);
        }

        var times = new List<DateTime>();
        for (var time = startUtc; time < endUtc; time += step)
        {
            times.Add(time);
        }

        var count = times.Count;
        var latitude = observer.LatitudeDegrees * DegreesToRadians;
        var sinLatitude = Math.Sin(latitude);
        var cosLatitude = Math.Cos(latitude);
        var samples = new NightSkySamples
        {
            TimesUtc = [.. times],
            DurationsSeconds = new double[count],
            LocalSiderealTimeDegrees = new double[count],
            SunAltitudeDegrees = new double[count],
            IsDark = new bool[count],
            MoonAltitudeDegrees = new double[count],
            MoonPhaseAngleDegrees = new double[count],
            MoonIlluminatedFraction = new double[count],
            MoonVectors = new double[count * 3],
            CloudFraction = new double[count],
            HasForecast = weather is not null && weather.Between(startUtc, endUtc).Count > 0,
            SinLatitude = sinLatitude,
            CosLatitude = cosLatitude,
            MiddleJulianDate = AstronomyMath.ToJulianDate(startUtc + (endUtc - startUtc) / 2),
            RefractionScale = Refraction.PressureScale(observer.ElevationMeters),
            DarknessLimitDegrees = darknessLimitDegrees
        };

        for (var k = 0; k < count; k++)
        {
            var t = times[k];
            var next = k + 1 < count ? times[k + 1] : endUtc;
            samples.DurationsSeconds[k] = (next - t).TotalSeconds;

            var lst = AstronomyMath.CalculateLocalSiderealTimeDegrees(t, observer.LongitudeDegrees);
            samples.LocalSiderealTimeDegrees[k] = lst;

            var sun = SolarSystemCalculator.GetSunPosition(t);
            samples.SunAltitudeDegrees[k] = Altitude(sun.Equatorial, lst, sinLatitude, cosLatitude);
            samples.IsDark[k] = samples.SunAltitudeDegrees[k] <= darknessLimitDegrees;

            var moon = SolarSystemCalculator.GetMoonPosition(t);
            var geocentric = Altitude(moon.Equatorial, lst, sinLatitude, cosLatitude);
            samples.MoonAltitudeDegrees[k] = geocentric - moon.HorizontalParallaxDegrees * Math.Cos(geocentric * DegreesToRadians);
            samples.MoonPhaseAngleDegrees[k] = SolarSystemCalculator.GetMoonPhaseAngleDegrees(moon, sun);
            samples.MoonIlluminatedFraction[k] = SolarSystemCalculator.GetMoonIlluminatedFraction(moon, sun);
            var (x, y, z) = UnitVector(moon.Equatorial);
            samples.MoonVectors[3 * k] = x;
            samples.MoonVectors[3 * k + 1] = y;
            samples.MoonVectors[3 * k + 2] = z;

            if (weather?.At(t) is { } hour)
            {
                samples.CloudFraction[k] = Math.Clamp(hour.CloudCoverPercent / 100.0, 0, 1);
            }
        }

        return samples;
    }

    /// <summary>Geometric altitude (degrees) of equatorial coordinates of date at a sidereal time.</summary>
    public static double Altitude(EquatorialCoordinates coordinates, double lstDegrees, double sinLatitude, double cosLatitude)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var hourAngle = (lstDegrees - coordinates.RightAscensionHours * 15.0) * DegreesToRadians;
        var declination = coordinates.DeclinationDegrees * DegreesToRadians;
        var sinAltitude = Math.Clamp(
            sinLatitude * Math.Sin(declination) + cosLatitude * Math.Cos(declination) * Math.Cos(hourAngle), -1.0, 1.0);
        return Math.Asin(sinAltitude) / DegreesToRadians;
    }

    /// <summary>Unit vector of equatorial coordinates.</summary>
    public static (double X, double Y, double Z) UnitVector(EquatorialCoordinates coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var ra = coordinates.RightAscensionHours * 15.0 * DegreesToRadians;
        var dec = coordinates.DeclinationDegrees * DegreesToRadians;
        return (Math.Cos(dec) * Math.Cos(ra), Math.Cos(dec) * Math.Sin(ra), Math.Sin(dec));
    }

    /// <summary>Angle (degrees) between the Moon of sample k and a unit vector.</summary>
    public double MoonSeparationDegrees(int k, double x, double y, double z)
    {
        var dot = MoonVectors[3 * k] * x + MoonVectors[3 * k + 1] * y + MoonVectors[3 * k + 2] * z;
        return Math.Acos(Math.Clamp(dot, -1, 1)) / DegreesToRadians;
    }
}
