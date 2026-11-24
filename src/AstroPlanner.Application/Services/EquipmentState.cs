using CommunityToolkit.Mvvm.ComponentModel;
using System.Data.Common;

namespace AstroPlanner.Application.Services;

/// <summary>
/// The equipment inventory and the active rig, shared by every view model (spec §4.5, §10 "active
/// rig selector"). Loaded once from <see cref="IEquipmentRepository"/>; every change is saved at
/// once and raises <see cref="Changed"/>.
/// </summary>
public sealed partial class EquipmentState : ObservableObject
{
    private readonly IEquipmentRepository _repository;
    private readonly IAppLogger _logger;

    public EquipmentState(IEquipmentRepository repository, IAppLogger logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        try
        {
            Inventory = _repository.Load();
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("Could not load the equipment inventory.", ex);
            Inventory = EquipmentInventory.Empty;
        }

        Rigs = Resolve(Inventory);
    }

    /// <summary>Raised after the inventory or the active rig changed.</summary>
    public event EventHandler? Changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveRig), nameof(HasActiveRig))]
    public partial EquipmentInventory Inventory { get; private set; }

    /// <summary>Every rig whose parts exist, in inventory order (for the rig selector).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<RigSetup> Rigs { get; private set; }

    public RigSetup? ActiveRig => Inventory.ActiveRig;

    public bool HasActiveRig => ActiveRig is not null;

    /// <summary>Saves a new inventory. Returns false (and keeps the old one) if it could not be saved.</summary>
    public bool Save(EquipmentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        try
        {
            _repository.Save(inventory);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError("Could not save the equipment inventory.", ex);
            return false;
        }

        Inventory = inventory;
        Rigs = Resolve(inventory);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Makes a rig the active one (null = none).</summary>
    public bool SetActiveRig(string? rigId) =>
        rigId == Inventory.ActiveRigId || Save(Inventory with { ActiveRigId = Inventory.Rigs.Any(r => r.Id == rigId) ? rigId : null });

    private static IReadOnlyList<RigSetup> Resolve(EquipmentInventory inventory) =>
        [.. inventory.Rigs.Select(r => RigSetup.Resolve(r, inventory.Cameras, inventory.Optics, inventory.Mounts)).OfType<RigSetup>()];
}
