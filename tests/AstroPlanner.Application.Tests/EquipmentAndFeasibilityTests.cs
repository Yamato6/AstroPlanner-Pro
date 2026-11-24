using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.ViewModels;

namespace AstroPlanner.Application.Tests;

/// <summary>M3a: equipment, active rig, sky quality from the atlas and the feasibility verdicts in the UI.</summary>
public sealed class EquipmentAndFeasibilityTests
{
    // 2026-01-16 04:00 UTC = 2026-01-15 22:00 in Aguascalientes (UTC-6).
    private static readonly DateTimeOffset WinterNightUtc = new(2026, 1, 16, 4, 0, 0, TimeSpan.Zero);

    private static readonly ObserverSettings Aguascalientes = new()
    {
        CityId = null,
        IsManualLocation = true,
        LatitudeDegrees = 21.88,
        LongitudeDegrees = -102.29,
        ElevationMeters = 1888,
        TimeZoneId = "Central Standard Time (Mexico)",
        MinimumAltitude = 20,
        MaximumMagnitude = 20
    };

    private static readonly IReadOnlyList<CelestialObject> Catalog =
        [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13, .. SolarSystemCatalog.Planets];

    private static async Task<(ShellViewModel Shell, FakeEquipmentRepository Equipment)> CreateAsync(
        EquipmentInventory? inventory = null, double? atlasBrightness = 1.84, ObserverSettings? settings = null)
    {
        var equipment = new FakeEquipmentRepository(inventory ?? EquipmentInventory.Demo);
        var shell = TestShell.Create(
            catalog: Catalog,
            settings: new FakeSettingsService(settings ?? Aguascalientes),
            time: new ManualTimeProvider(WinterNightUtc),
            equipment: equipment,
            atlas: new FakeAtlas(atlasBrightness));
        await shell.LoadCommand.ExecuteAsync(null);
        await shell.Targets.NightEphemeridesTask;
        await shell.Targets.FeasibilityTask;
        return (shell, equipment);
    }

    private static CelestialObjectRowViewModel Row(ShellViewModel shell, string id) => shell.Targets.AllObjects.Single(r => r.Id == id);

    [Fact]
    public async Task EveryObject_GetsAVerdict_ForTheActiveRig()
    {
        var (shell, _) = await CreateAsync();

        shell.Targets.AllObjects.Should().OnlyContain(r => r.Feasibility != null);
        var m42 = Row(shell, "M42");
        m42.VerdictText.Should().NotBeEmpty();
        m42.Score.Should().BeInRange(0, 100);
        m42.Feasibility!.ZenithSqmSource.Should().Be(SkyQualitySource.Atlas);
        m42.Feasibility.ZenithSqm.Should().BeApproximately(SkyBrightness.SqmFromArtificial(1.84), 1e-9);
        m42.RecommendedText.Should().Contain("ISO 800");
    }

    [Fact]
    public async Task Detail_ExplainsTheVerdict()
    {
        var (shell, _) = await CreateAsync();

        shell.Targets.SelectById("M42").Should().BeTrue();

        shell.Detail.HasFeasibility.Should().BeTrue();
        shell.Detail.FeasibilityHeadline.Should().Contain("/100");
        shell.Detail.FeasibilityLines.Should().NotBeEmpty();
        shell.Detail.FeasibilityLines.Should().Contain(l => l.Text.Contains("NPF", StringComparison.Ordinal));
        shell.Detail.RecommendedText.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Tonight_ListsTheBestTargets_BestFirst_AndTheSiteSky()
    {
        var (shell, _) = await CreateAsync();

        shell.Tonight.BestTargets.Should().NotBeEmpty();
        shell.Tonight.BestTargets.Select(r => r.Score).Should().BeInDescendingOrder();
        shell.Tonight.BestTargets.Should().OnlyContain(r => r.Verdict >= FeasibilityVerdict.Marginal);
        shell.Tonight.BestTargetsHint.Should().BeNull();
        shell.Tonight.SiteSkyText.Should().Contain(Strings.SkyQualityAtlas).And.Contain("19.3");
        shell.Tonight.ActiveRigText.Should().Contain("Rokinon");

        var first = shell.Tonight.BestTargets[0];
        shell.Tonight.AddToPlanCommand.Execute(first);
        shell.Plan.Contains(first).Should().BeTrue();
    }

    [Fact]
    public async Task ChangingTheActiveRig_RecalculatesAndMovesTheCameraFrame()
    {
        var (shell, equipment) = await CreateAsync();
        var tripod = Row(shell, "M42").Feasibility!;
        shell.Sky.Camera.FocalLengthMm.Should().Be(16);
        shell.Sky.Camera.FovRigName.Should().Contain("Rokinon");

        shell.Equipment!.ActiveRigId = "t7-starsense-130";
        await shell.Targets.FeasibilityTask;

        equipment.Inventory.ActiveRigId.Should().Be("t7-starsense-130");
        Row(shell, "M42").Feasibility.Should().NotBeSameAs(tripod);
        Row(shell, "M42").Feasibility!.SubSeconds.Should().BeLessThan(1, "650 mm on an untracked mount: NPF ≈ 0.4 s");
        shell.Sky.Camera.FocalLengthMm.Should().Be(650);
    }

    [Fact]
    public async Task WithoutARig_ThereAreNoVerdicts_AndTonightSaysWhy()
    {
        var (shell, _) = await CreateAsync(EquipmentInventory.Demo with { ActiveRigId = null });

        shell.Targets.AllObjects.Should().OnlyContain(r => r.Feasibility == null);
        shell.Tonight.BestTargets.Should().BeEmpty();
        shell.Tonight.BestTargetsHint.Should().Be(Strings.BestTargetsNoRig);
        shell.Tonight.ActiveRigText.Should().Be(Strings.NoActiveRig);
        shell.Detail.HasFeasibility.Should().BeFalse();
    }

    [Fact]
    public async Task VerdictAndFieldFilters_DoNotRecalculate()
    {
        var (shell, _) = await CreateAsync();
        var before = Row(shell, "M31").Feasibility;
        var all = shell.Targets.Objects.Count;

        shell.Targets.VerdictFilter = nameof(FeasibilityVerdict.Excellent);
        shell.Targets.Objects.Should().OnlyContain(r => r.Verdict == FeasibilityVerdict.Excellent);

        shell.Targets.VerdictFilter = "";
        shell.Targets.OnlyFitsFov = true;
        shell.Targets.Objects.Should().OnlyContain(r => r.FitsFov);
        shell.Targets.Objects.Count.Should().BeLessThanOrEqualTo(all);

        shell.Targets.ResetFiltersCommand.Execute(null);
        shell.Targets.OnlyFitsFov.Should().BeFalse();
        Row(shell, "M31").Feasibility.Should().BeSameAs(before);
    }

    [Fact]
    public async Task ManualSqm_OverridesTheAtlas()
    {
        var (shell, _) = await CreateAsync();
        var city = Row(shell, "M42").Feasibility!.Score;

        shell.Observer.ZenithSqm = 21.8;
        await shell.Targets.FeasibilityTask;

        shell.Observer.ZenithSqmSource.Should().Be(SkyQualitySource.Manual);
        Row(shell, "M42").Feasibility!.ZenithSqm.Should().Be(21.8);
        Row(shell, "M42").Feasibility!.Score.Should().BeGreaterThanOrEqualTo(city);
    }

    [Fact]
    public async Task OutsideTheGrids_ANaturalSkyIsAssumed()
    {
        var (shell, _) = await CreateAsync(atlasBrightness: null);

        shell.Observer.AtlasSqm.Should().BeNull();
        shell.Observer.ZenithSqmSource.Should().Be(SkyQualitySource.Default);
        shell.Observer.EffectiveZenithSqm.Should().Be(SkyBrightness.NaturalSqm);
    }

    [Fact]
    public async Task NauticalTwilightSetting_ReachesTheModel()
    {
        var (shell, _) = await CreateAsync();

        shell.Settings.AllowNauticalTwilight = true;
        shell.Settings.ExtinctionCoefficient = 0.18;
        shell.Settings.SaveCommand.Execute(null);
        await shell.Targets.FeasibilityTask;

        shell.Observer.Darkness.Should().Be(DarknessDefinition.Nautical);
        shell.Observer.ExtinctionCoefficient.Should().Be(0.18);
        shell.Targets.AllObjects.Should().OnlyContain(r => r.Feasibility != null);
    }

    // ---- Equipment tab ---------------------------------------------------------------------------

    [Fact]
    public async Task Equipment_AddsACompleteRig_AndShowsItsDerivedOptics()
    {
        var (shell, repository) = await CreateAsync(EquipmentInventory.Empty);
        var equipment = shell.Equipment!;

        equipment.NewCameraCommand.Execute(null);
        equipment.CameraName = "ZWO ASI533MC Pro";
        equipment.CameraKindValue = nameof(CameraKind.AstroCamera);
        equipment.SensorWidthText = "11.31";
        equipment.SensorHeightText = "11.31";
        equipment.ResolutionWidthText = "3008";
        equipment.ResolutionHeightText = "3008";
        equipment.PixelSizeText = "3.76";
        equipment.QuantumEfficiencyText = "0.8";
        equipment.ReadNoiseText = "1.0";
        equipment.DarkCurrentText = "0.001";
        equipment.GainText = "100";
        equipment.SaveCameraCommand.Execute(null);

        equipment.NewOpticCommand.Execute(null);
        equipment.OpticName = "Redcat 51";
        equipment.OpticKindValue = nameof(OpticKind.Refractor);
        equipment.FocalLengthText = "250";
        equipment.ApertureText = "";
        equipment.FocalRatioText = "4.9";
        equipment.SaveOpticCommand.Execute(null);

        equipment.NewMountCommand.Execute(null);
        equipment.MountName = "Star Adventurer GTi";
        equipment.MountKindValue = nameof(MountKind.StarTracker);
        equipment.MaxExposureText = "90";
        equipment.SaveMountCommand.Execute(null);

        equipment.NewRigCommand.Execute(null);
        equipment.RigName = "Redcat on GTi";
        equipment.DerivedText.Should().Contain("mm").And.Contain("90 s");
        equipment.SaveRigCommand.Execute(null);

        equipment.HasError.Should().BeFalse(equipment.StatusMessage);
        repository.Inventory.Rigs.Should().ContainSingle();
        repository.Inventory.ActiveRigId.Should().Be(repository.Inventory.Rigs[0].Id, "the first rig becomes the active one");
        repository.Inventory.Optics[0].ApertureMm.Should().BeApproximately(250 / 4.9, 1e-9);
        repository.Inventory.Cameras[0].RecommendedGain.Should().Be(100);

        await shell.Targets.FeasibilityTask;
        Row(shell, "M42").Feasibility.Should().NotBeNull();
        Row(shell, "M42").Feasibility!.Recommended!.Gain.Should().Be(100);
    }

    [Fact]
    public async Task Equipment_RejectsBadInput_AndPartsInUse()
    {
        var (shell, repository) = await CreateAsync();
        var equipment = shell.Equipment!;
        var saves = repository.SaveCount;

        equipment.SelectedCamera = equipment.Cameras[0];
        equipment.SensorWidthText = "abc";
        equipment.SaveCameraCommand.Execute(null);
        equipment.HasError.Should().BeTrue();
        equipment.StatusMessage.Should().Contain(Strings.FieldSensorWidth);

        equipment.SensorWidthText = "500";
        equipment.SaveCameraCommand.Execute(null);
        equipment.HasError.Should().BeTrue();

        equipment.SelectedCamera = equipment.Cameras[0];
        equipment.DeleteCameraCommand.Execute(null);
        equipment.StatusMessage.Should().Be(Strings.EquipmentInUse);
        repository.SaveCount.Should().Be(saves);
        repository.Inventory.Cameras.Should().HaveCount(1);
    }

    [Fact]
    public async Task Equipment_DeletingTheActiveRig_PicksAnother()
    {
        var (shell, repository) = await CreateAsync();
        var equipment = shell.Equipment!;

        equipment.SelectedRig = equipment.Rigs.Single(r => r.Rig.Id == "t7-rokinon-16");
        equipment.DeleteRigCommand.Execute(null);

        repository.Inventory.ActiveRigId.Should().Be("t7-starsense-130");
        equipment.Rigs.Should().ContainSingle();
    }

    [Fact]
    public async Task Equipment_EditingKeepsTheIds_SoRigsStayValid()
    {
        var (shell, repository) = await CreateAsync();
        var equipment = shell.Equipment!;

        equipment.SelectedOptic = equipment.Optics.Single(o => o.Id == "rokinon-16");
        equipment.OpticName = "Rokinon 16 mm (stopped down)";
        equipment.ApertureText = "5.7";
        equipment.SaveOpticCommand.Execute(null);

        repository.Inventory.Optics.Single(o => o.Id == "rokinon-16").FocalRatio.Should().BeApproximately(16 / 5.7, 1e-9);
        shell.Equipment!.Rigs.Should().HaveCount(2);
        await shell.Targets.FeasibilityTask;
        Row(shell, "M42").Feasibility!.MountLimitSeconds.Should().BeGreaterThan(12, "f/2.8 instead of f/2: NPF 13.1 s instead of 11.4 s");
    }

    [Fact]
    public async Task Locations_ShowTheAtlasSqm()
    {
        var locations = new FakeLocationRepository();
        locations.Locations.Add(new Location("ags", "Aguascalientes", 21.88, -102.29, 1888,
            "America/Mexico_City", "Central Standard Time (Mexico)"));
        var shell = TestShell.Create(locations: locations, atlas: new FakeAtlas(1.84));
        shell.Locations.Reload();

        shell.Locations.SelectedLocation = shell.Locations.Locations[0];

        shell.Locations.AtlasSqmText.Should().Contain("19.3").And.Contain("Bortle");
        await Task.CompletedTask;
    }
}
