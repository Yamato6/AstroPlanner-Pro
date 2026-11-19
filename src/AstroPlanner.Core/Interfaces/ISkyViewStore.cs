using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>Persistence of the sky view settings.</summary>
public interface ISkyViewStore
{
    /// <summary>The saved settings; defaults if there are none or they are damaged.</summary>
    SkyViewSettings Load();

    void Save(SkyViewSettings settings);
}
