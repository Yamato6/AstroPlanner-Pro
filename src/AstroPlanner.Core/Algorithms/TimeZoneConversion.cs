namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Conversion between the observer's wall-clock time and UTC, which is what the
/// astronomical calculations need.
/// </summary>
public static class TimeZoneConversion
{
    /// <summary>
    /// Finds a system time zone by id; if it does not exist or is invalid, returns the local
    /// zone.
    /// </summary>
    public static TimeZoneInfo FindOrLocal(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>
    /// Converts a wall-clock date/time in <paramref name="timeZone"/> to UTC.
    /// </summary>
    public static DateTime ToUtc(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var wallClock = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);

        // A non-existent time happens when daylight saving time starts. It is moved to the
        // first valid time to keep the calculation usable.
        if (timeZone.IsInvalidTime(wallClock))
        {
            wallClock = wallClock.AddHours(1);
        }

        // For an ambiguous time (end of daylight saving time), ConvertTimeToUtc
        // deterministically applies the zone's standard offset.
        return TimeZoneInfo.ConvertTimeToUtc(wallClock, timeZone);
    }
}
