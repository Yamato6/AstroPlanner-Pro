using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>The planetarium UI's own state: its window layout and interface preferences.</summary>
public interface IInterfaceStore
{
    /// <summary>The saved windows; empty when there are none.</summary>
    IReadOnlyList<WindowLayout> LoadLayouts();

    /// <summary>Replaces the saved windows with these (atomically).</summary>
    void SaveLayouts(IReadOnlyList<WindowLayout> layouts);

    /// <summary>The saved preferences; defaults if there are none or they are damaged.</summary>
    InterfacePreferences LoadPreferences();

    void SavePreferences(InterfacePreferences preferences);
}
