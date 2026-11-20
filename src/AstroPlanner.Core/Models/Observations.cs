namespace AstroPlanner.Core.Models;

/// <summary>An object of the observation plan with the user's note.</summary>
public sealed record ObservationPlanEntry(string Id, string Note = "")
{
    /// <summary>The user pinned its block in the session schedule (spec §4.6).</summary>
    public bool Locked { get; init; }

    /// <summary>Start of the locked block (local wall-clock time).</summary>
    public DateTime? StartLocal { get; init; }

    /// <summary>End of the locked block (local wall-clock time).</summary>
    public DateTime? EndLocal { get; init; }
}

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
