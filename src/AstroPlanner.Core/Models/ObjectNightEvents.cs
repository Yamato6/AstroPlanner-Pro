namespace AstroPlanner.Core.Models;

/// <summary>
/// Events of an object during an observing night, in local time. Each time is null if the
/// event does not happen inside the night window (e.g. a circumpolar object neither rises nor
/// sets).
/// </summary>
public sealed record ObjectNightEvents(
    DateTime? RiseLocal,
    DateTime? ThresholdCrossingLocal,
    DateTime CulminationLocal,
    DateTime? SetLocal,
    double MaximumAltitudeDegrees);
