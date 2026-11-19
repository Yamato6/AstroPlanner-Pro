using AstroPlanner.Core.Algorithms;

namespace AstroPlanner.Core.Models;

/// <summary>
/// Persisted observer settings (stored in app.db). The location fields hold the active
/// observing site; <see cref="ActiveLocationId"/> links it to a saved <see cref="Location"/>.
/// </summary>
public sealed class ObserverSettings
{
    public string? CityId { get; set; } = "mexico-city";
    public bool IsManualLocation { get; set; }
    public double LatitudeDegrees { get; set; } = 19.4326;
    public double LongitudeDegrees { get; set; } = -99.1332;
    public double ElevationMeters { get; set; } = 2240;

    /// <summary>
    /// System time zone id (Windows, e.g. "Romance Standard Time"; IANA ids are accepted too).
    /// Used to convert the local date and time chosen by the user to UTC before the
    /// astronomical calculations.
    /// </summary>
    public string TimeZoneId { get; set; } = "Central Standard Time (Mexico)";

    /// <summary>Saved location the fields above come from; null for an unsaved site.</summary>
    public string? ActiveLocationId { get; set; }

    /// <summary>Minimum altitude (degrees) for an object to count as visible.</summary>
    public double MinimumAltitude { get; set; } = 20;

    /// <summary>Faintest magnitude shown.</summary>
    public double MaximumMagnitude { get; set; } = 12;

    /// <summary>Fetch the cloud forecast from Open-Meteo (sends the location rounded to ~1 km).</summary>
    public bool WeatherForecastEnabled { get; set; } = true;

    /// <summary>Check GitHub for a new version at startup.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public ObserverLocation ToObserverLocation() =>
        new(LatitudeDegrees, LongitudeDegrees, ElevationMeters);

    /// <summary>The saved time zone; the local one if the id no longer exists (e.g. after an OS change).</summary>
    public TimeZoneInfo GetTimeZone() => TimeZoneConversion.FindOrLocal(TimeZoneId);
}
