using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>Persistence of the observer settings.</summary>
public interface ISettingsService
{
    /// <summary>Reads the saved settings; defaults if there are none.</summary>
    ObserverSettings Load();

    /// <summary>Saves the settings atomically.</summary>
    void Save(ObserverSettings settings);
}
