using AstroPlanner.Core.Models;
using AstroPlanner.Data.Storage;

namespace AstroPlanner.Data.Tests;

public sealed class EquipmentRepositoryTests : TempFolderTest
{
    [Fact]
    public void FirstLoad_SeedsTheDemoProfile()
    {
        var repository = new SqliteEquipmentRepository(new AppDatabase(DatabasePath));

        var inventory = repository.Load();

        inventory.Should().BeEquivalentTo(EquipmentInventory.Demo);
        inventory.ActiveRig!.Name.Should().Contain("Rokinon");
    }

    [Fact]
    public void Save_RoundTripsEveryField_AndKeepsTheOrder()
    {
        var repository = new SqliteEquipmentRepository(new AppDatabase(DatabasePath));
        repository.Load();
        var astro = new Camera("asi", "ZWO ASI2600MC Pro", CameraKind.AstroCamera, SensorColor.Color, 23.5, 15.7, 6248, 4176,
            pixelSizeUm: null, quantumEfficiency: 0.8, readNoiseElectrons: 1.5, darkCurrentElectronsPerSecond: 0.002,
            recommendedGain: 100, notes: "cooled");
        var refractor = new Optic("apo", "72 mm APO", OpticKind.Refractor, 432, 72, 0.95);
        var tracker = new Mount("sa", "Star Adventurer", MountKind.StarTracker, maxUnguidedExposureSeconds: 90, payloadKg: 5);
        var demo = EquipmentInventory.Demo;
        var inventory = demo with
        {
            Cameras = [astro, .. demo.Cameras],
            Optics = [.. demo.Optics, refractor],
            Mounts = [.. demo.Mounts, tracker],
            Rigs = [.. demo.Rigs, new Rig("apo-rig", "APO on tracker", "asi", "apo", "sa", 0.8)],
            ActiveRigId = "apo-rig"
        };

        repository.Save(inventory);
        var loaded = new SqliteEquipmentRepository(new AppDatabase(DatabasePath)).Load();

        loaded.Should().BeEquivalentTo(inventory, options => options.WithStrictOrdering());
        loaded.ActiveRig!.FocalLengthMm.Should().BeApproximately(345.6, 1e-9);
    }

    [Fact]
    public void DeletingEverything_DoesNotSeedAgain()
    {
        var repository = new SqliteEquipmentRepository(new AppDatabase(DatabasePath));
        repository.Load();

        repository.Save(EquipmentInventory.Empty);

        repository.Load().Should().BeEquivalentTo(EquipmentInventory.Empty);
    }

    [Fact]
    public void Save_RejectsRigsWithMissingParts()
    {
        var repository = new SqliteEquipmentRepository(new AppDatabase(DatabasePath));
        var broken = EquipmentInventory.Demo with { Mounts = [] };

        var act = () => repository.Save(broken);

        act.Should().Throw<ArgumentException>().WithMessage("*not in the inventory*");
    }

    [Fact]
    public void UnknownActiveRig_LoadsAsNone()
    {
        var repository = new SqliteEquipmentRepository(new AppDatabase(DatabasePath));
        repository.Load();
        repository.Save(EquipmentInventory.Demo with { ActiveRigId = "gone" });

        repository.Load().ActiveRigId.Should().BeNull();
    }
}
