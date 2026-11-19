namespace AstroPlanner.UI.Services;

/// <summary>An object of the observation plan with the user's note.</summary>
public sealed record ObservationPlanEntry(string Id, string Note = "");

/// <summary>
/// Persistence of the observation plan ("My plan") between sessions. Only catalog ids and
/// notes are saved; the objects are rebuilt when it is loaded.
/// </summary>
public interface IObservationPlanService
{
    /// <summary>Reads the saved plan; an empty list if there is none or it is invalid.</summary>
    IReadOnlyList<ObservationPlanEntry> Load();

    /// <summary>Saves the plan atomically (writes a temporary file and renames it).</summary>
    void Save(IEnumerable<ObservationPlanEntry> entries);
}
