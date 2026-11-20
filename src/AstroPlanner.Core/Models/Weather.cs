namespace AstroPlanner.Core.Models;

/// <summary>Forecast for one hour. Percentages go from 0 to 100.</summary>
/// <param name="TimeUtc">Start of the hour, in UTC.</param>
public sealed record HourlyWeather(
    DateTime TimeUtc,
    int CloudCoverPercent,
    int? LowCloudPercent = null,
    int? MidCloudPercent = null,
    int? HighCloudPercent = null,
    int? PrecipitationProbabilityPercent = null,
    int? RelativeHumidityPercent = null)
{
    /// <summary>Air temperature at 2 m, °C.</summary>
    public double? TemperatureC { get; init; }

    /// <summary>Dew point at 2 m, °C.</summary>
    public double? DewPointC { get; init; }

    /// <summary>Mean wind at 10 m, km/h.</summary>
    public double? WindSpeedKmh { get; init; }

    /// <summary>Wind gusts at 10 m, km/h.</summary>
    public double? WindGustKmh { get; init; }
}

/// <summary>Cloud cover summary of an interval (usually the dark night).</summary>
/// <param name="AverageCloudCoverPercent">Average cloud cover of the interval.</param>
/// <param name="ClearestHourUtc">Hour with the fewest clouds (the first one on a tie).</param>
public sealed record CloudSummary(
    double AverageCloudCoverPercent,
    int MinimumCloudCoverPercent,
    int MaximumCloudCoverPercent,
    DateTime ClearestHourUtc,
    int ClearestHourCloudCoverPercent);

/// <summary>Hourly forecast for a location.</summary>
public sealed class WeatherForecast
{
    public WeatherForecast(double latitude, double longitude, IReadOnlyList<HourlyWeather> hours, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(hours);
        Latitude = latitude;
        Longitude = longitude;
        Hours = hours.OrderBy(h => h.TimeUtc).ToList();
        FetchedAtUtc = fetchedAtUtc;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public IReadOnlyList<HourlyWeather> Hours { get; }
    public DateTime FetchedAtUtc { get; }

    /// <summary>The forecast hour containing <paramref name="utc"/>, or null if it is outside the forecast.</summary>
    public HourlyWeather? At(DateTime utc)
    {
        foreach (var hour in Hours)
        {
            if (utc >= hour.TimeUtc && utc < hour.TimeUtc.AddHours(1))
            {
                return hour;
            }
        }

        return null;
    }

    /// <summary>Hours overlapping [<paramref name="startUtc"/>, <paramref name="endUtc"/>).</summary>
    public IReadOnlyList<HourlyWeather> Between(DateTime startUtc, DateTime endUtc) =>
        Hours.Where(h => h.TimeUtc.AddHours(1) > startUtc && h.TimeUtc < endUtc).ToList();

    /// <summary>
    /// Cloud cover summary of the interval, or null if the forecast does not cover it (past
    /// dates or beyond the forecast horizon).
    /// </summary>
    public CloudSummary? Summarize(DateTime startUtc, DateTime endUtc)
    {
        var hours = Between(startUtc, endUtc);
        if (hours.Count == 0)
        {
            return null;
        }

        var clearest = hours.MinBy(h => h.CloudCoverPercent)!;
        return new CloudSummary(
            hours.Average(h => h.CloudCoverPercent),
            hours.Min(h => h.CloudCoverPercent),
            hours.Max(h => h.CloudCoverPercent),
            clearest.TimeUtc,
            clearest.CloudCoverPercent);
    }
}

/// <summary>
/// One 3-hour point of the 7Timer! ASTRO forecast (https://www.7timer.info/doc.php): the classes are
/// the service's own scales, converted by <see cref="AstroForecastScales"/>.
/// </summary>
/// <param name="TimeUtc">Centre of the 3-hour period.</param>
/// <param name="CloudCoverClass">1 (0–6 %) … 9 (94–100 %).</param>
/// <param name="SeeingClass">1 (&lt; 0.5″) … 8 (&gt; 2.5″).</param>
/// <param name="TransparencyClass">1 (&lt; 0.3 mag/air mass) … 8 (&gt; 1).</param>
/// <param name="WindSpeedClass">1 (calm) … 8 (hurricane), 10 m.</param>
public sealed record AstroForecastPoint(
    DateTime TimeUtc,
    int CloudCoverClass,
    int SeeingClass,
    int TransparencyClass,
    int WindSpeedClass)
{
    public double SeeingArcsec => AstroForecastScales.SeeingArcsec(SeeingClass);

    public double TransparencyMagnitudesPerAirmass => AstroForecastScales.TransparencyMagnitudesPerAirmass(TransparencyClass);
}

/// <summary>
/// The 7Timer! ASTRO classes as numbers: the middle of each documented range, and for the open-ended
/// extremes a value just past the bound (0.4″ / 2.75″ for seeing, 0.25 / 1.1 mag for transparency).
/// </summary>
public static class AstroForecastScales
{
    private static readonly double[] Seeing = [0.4, 0.625, 0.875, 1.125, 1.375, 1.75, 2.25, 2.75];
    private static readonly double[] Transparency = [0.25, 0.35, 0.45, 0.55, 0.65, 0.775, 0.925, 1.1];

    /// <summary>Seeing FWHM in arcseconds for a class 1–8 (clamped).</summary>
    public static double SeeingArcsec(int seeingClass) => Seeing[Math.Clamp(seeingClass, 1, 8) - 1];

    /// <summary>Extinction-like transparency in magnitudes per air mass for a class 1–8 (clamped).</summary>
    public static double TransparencyMagnitudesPerAirmass(int transparencyClass) =>
        Transparency[Math.Clamp(transparencyClass, 1, 8) - 1];
}

/// <summary>The 7Timer! ASTRO forecast for a location (3-hour steps, about 3 days).</summary>
public sealed class AstroForecast
{
    /// <summary>Each point covers ±1.5 h around its time.</summary>
    public static readonly TimeSpan HalfStep = TimeSpan.FromHours(1.5);

    public AstroForecast(double latitude, double longitude, IReadOnlyList<AstroForecastPoint> points, DateTime fetchedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(points);
        Latitude = latitude;
        Longitude = longitude;
        Points = [.. points.OrderBy(p => p.TimeUtc)];
        FetchedAtUtc = fetchedAtUtc;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public IReadOnlyList<AstroForecastPoint> Points { get; }
    public DateTime FetchedAtUtc { get; }

    /// <summary>The point whose period contains <paramref name="utc"/>, or null outside the forecast.</summary>
    public AstroForecastPoint? At(DateTime utc) =>
        Points.FirstOrDefault(p => utc >= p.TimeUtc - HalfStep && utc < p.TimeUtc + HalfStep);

    /// <summary>Points whose period overlaps [<paramref name="startUtc"/>, <paramref name="endUtc"/>).</summary>
    public IReadOnlyList<AstroForecastPoint> Between(DateTime startUtc, DateTime endUtc) =>
        [.. Points.Where(p => p.TimeUtc + HalfStep > startUtc && p.TimeUtc - HalfStep < endUtc)];
}

/// <summary>The forecast could not be obtained (offline, service error, invalid response).</summary>
public sealed class WeatherForecastException(string message, Exception? innerException = null)
    : Exception(message, innerException);
