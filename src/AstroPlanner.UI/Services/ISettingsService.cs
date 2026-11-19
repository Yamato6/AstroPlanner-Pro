namespace AstroPlanner.UI.Services;

/// <summary>
/// Persistence of the observer settings. The interface lives in UI/Services because where and
/// how settings are stored is a presentation concern, not a domain one.
/// </summary>
public interface ISettingsService
{
    /// <summary>Reads the saved settings; defaults if there are none.</summary>
    ObserverSettings Load();

    /// <summary>Saves the settings atomically (writes a temporary file and renames it).</summary>
    void Save(ObserverSettings settings);
}
