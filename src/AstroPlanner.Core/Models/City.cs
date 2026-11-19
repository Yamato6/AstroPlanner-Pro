namespace AstroPlanner.Core.Models;

/// <summary>A city of the bundled list, with its Windows and IANA time zone ids.</summary>
public sealed record City(
    string Id,
    string Name,
    string State,
    string Country,
    double LatitudeDegrees,
    double LongitudeDegrees,
    double ElevationMeters,
    string TimeZoneId,
    string IanaTimeZoneId)
{
    public string DisplayName => $"{Name}, {State}";
}
