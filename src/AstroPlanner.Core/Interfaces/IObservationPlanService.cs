using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>
/// Persistence of the observation plan ("My plan") between sessions. Only catalog ids and
/// notes are saved; the objects are rebuilt when it is loaded.
/// </summary>
public interface IObservationPlanService
{
    /// <summary>Reads the saved plan, in order; an empty list if there is none.</summary>
    IReadOnlyList<ObservationPlanEntry> Load();

    /// <summary>Replaces the saved plan atomically.</summary>
    void Save(IEnumerable<ObservationPlanEntry> entries);

    /// <summary>Keys of the session checklist items ticked off; empty if none.</summary>
    IReadOnlyList<string> LoadChecklist();

    /// <summary>Replaces the ticked checklist items.</summary>
    void SaveChecklist(IEnumerable<string> doneKeys);
}

/// <summary>Persistence of the observation logbook.</summary>
public interface IObservationLogService
{
    /// <summary>Reads the logbook; an empty list if there is none.</summary>
    IReadOnlyList<ObservationLogEntry> Load();

    /// <summary>Replaces the saved logbook atomically.</summary>
    void Save(IEnumerable<ObservationLogEntry> entries);
}

/// <summary>Saved observing sites.</summary>
public interface ILocationRepository
{
    /// <summary>Every saved location, in the user's order.</summary>
    IReadOnlyList<Location> GetAll();

    /// <summary>Adds the location or replaces the one with the same id.</summary>
    void Save(Location location);

    /// <summary>Deletes the location; nothing happens if it does not exist.</summary>
    void Delete(string id);
}
