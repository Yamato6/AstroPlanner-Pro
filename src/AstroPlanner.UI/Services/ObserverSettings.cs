using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Persisted observer settings, stored as JSON in %LocalAppData%\AstroPlanner\settings.json.
/// </summary>
public sealed class ObserverSettings
{
    public string? CityId { get; set; } = "mexico-city";
    public bool IsManualLocation { get; set; }
    public double LatitudeDegrees { get; set; } = 19.4326;
    public double LongitudeDegrees { get; set; } = -99.1332;
    public double ElevationMeters { get; set; } = 2240;

    /// <summary>
    /// System time zone id (e.g. "Romance Standard Time"). Used to convert the local date and
    /// time chosen by the user to UTC before the astronomical calculations.
    /// </summary>
    public string TimeZoneId { get; set; } = "Central Standard Time (Mexico)";

    /// <summary>Minimum altitude (degrees) for an object to count as visible.</summary>
    public double MinimumAltitude { get; set; } = 20;

    /// <summary>
    /// Faintest magnitude shown. The faintest Messier object is ~10.2, so the default (12)
    /// hides none.
    /// </summary>
    public double MaximumMagnitude { get; set; } = 12;

    /// <summary>Fetch the cloud forecast from Open-Meteo (sends the location rounded to ~1 km).</summary>
    public bool WeatherForecastEnabled { get; set; } = true;

    /// <summary>Check GitHub for a new version at startup.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public ObserverLocation ToObserverLocation() =>
        new(LatitudeDegrees, LongitudeDegrees, ElevationMeters);

    public TimeZoneInfo GetTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // The saved id no longer exists (e.g. after an OS change): use the local zone.
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
