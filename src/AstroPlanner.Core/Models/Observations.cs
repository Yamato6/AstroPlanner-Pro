using AstroPlanner.Core.Imaging.Feasibility;

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
/// to 5 (excellent). A photographed entry also has its <see cref="Capture"/> and, when the
/// model had a verdict at logging time, its <see cref="Prediction"/> (spec §4.7, §8.7).
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

    public LogEntryKind Kind { get; init; } = LogEntryKind.Observed;

    /// <summary>How it was photographed; null for a visual observation.</summary>
    public CaptureDetails? Capture { get; init; }

    /// <summary>What the feasibility model predicted when the entry was logged; null if it had no verdict.</summary>
    public PredictionSummary? Prediction { get; init; }

    public bool IsPhotographed => Kind == LogEntryKind.Photographed;
}

/// <summary>What kind of session a logbook entry records. New values are appended so that stored names stay valid.</summary>
public enum LogEntryKind
{
    /// <summary>Seen at the eyepiece (or in a live view).</summary>
    Observed,

    /// <summary>Imaged: the entry has its capture details.</summary>
    Photographed
}

/// <summary>
/// How a target was photographed. Everything is optional: the user fills what they know.
/// <see cref="ImagePath"/> refers to a file the user chose; the logbook never copies it.
/// </summary>
public sealed record CaptureDetails
{
    /// <summary>Rig of the equipment inventory, when one was active; null otherwise.</summary>
    public string? RigId { get; init; }

    /// <summary>Rig name at logging time (kept even if the rig is later renamed or deleted).</summary>
    public string RigName { get; init; } = "";

    public double? SubSeconds { get; init; }
    public int? Iso { get; init; }
    public int? Gain { get; init; }
    public int? SubCount { get; init; }

    /// <summary>The stacked or best image, as a full path; null if none.</summary>
    public string? ImagePath { get; init; }

    /// <summary>Sub length × count, when both are known.</summary>
    public TimeSpan? TotalIntegration =>
        SubSeconds is { } sub && SubCount is { } count ? TimeSpan.FromSeconds(sub * count) : null;
}

/// <summary>The feasibility model's prediction kept with a photographed entry (spec §8.7).</summary>
/// <param name="SignalToNoise">Predicted SNR of the whole stack.</param>
public sealed record PredictionSummary(FeasibilityVerdict Verdict, int Score, double SignalToNoise)
{
    /// <summary>Sub length the model recommended, seconds; null when it recommended none.</summary>
    public double? SubSeconds { get; init; }

    /// <summary>Number of subs the model recommended; null when it recommended none.</summary>
    public int? SubCount { get; init; }

    public static PredictionSummary From(FeasibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new PredictionSummary(report.Verdict, report.Score, report.SignalToNoise)
        {
            SubSeconds = report.Recommended?.SubSeconds,
            SubCount = report.Recommended?.SubCount
        };
    }
}
