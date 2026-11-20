using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>The equipment inventory and the active rig (spec §4.5).</summary>
public interface IEquipmentRepository
{
    /// <summary>
    /// The saved inventory. On first use the demo profile (<see cref="EquipmentInventory.Demo"/>)
    /// is saved and returned; once the user has deleted everything it stays empty.
    /// </summary>
    EquipmentInventory Load();

    /// <summary>Replaces the whole inventory.</summary>
    /// <exception cref="ArgumentException">A rig refers to a camera, optic or mount that is not in the inventory.</exception>
    void Save(EquipmentInventory inventory);
}
