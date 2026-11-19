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
    /// IANA id (e.g. "America/Mexico_City") of a time zone given by its Windows or IANA id;
    /// null if it is unknown.
    /// </summary>
    public static string? ToIanaId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId))
        {
            return ianaId;
        }

        return TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out _) ? timeZoneId : null;
    }

    /// <summary>
    /// Windows id (e.g. "Central Standard Time (Mexico)") of a time zone given by its IANA or
    /// Windows id; null if it is unknown.
    /// </summary>
    public static string? ToWindowsId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId))
        {
            return windowsId;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out _) ? timeZoneId : null;
    }

    /// <summary>Converts a UTC instant to the wall-clock time of <paramref name="timeZone"/> (kind Unspecified).</summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
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
