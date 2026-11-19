namespace AstroPlanner.Core.Models;

/// <summary>An object of the observation plan with the user's note.</summary>
public sealed record ObservationPlanEntry(string Id, string Note = "");

/// <summary>
/// An observation recorded in the logbook. Seeing, transparency and rating go from 1 (poor)
/// to 5 (excellent).
/// </summary>
public sealed record ObservationLogEntry(
    Guid Id,
    string ObjectId,
    string ObjectName,
    DateTime ObservedAtLocal,
    string Location,
    string Equipment,
    int Seeing,
    int Transparency,
    int Rating,
    string Notes)
{
    public string RatingStars => new string('★', Math.Clamp(Rating, 0, 5)) + new string('☆', 5 - Math.Clamp(Rating, 0, 5));
}
