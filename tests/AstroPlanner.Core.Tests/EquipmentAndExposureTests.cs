using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Tests;

public sealed class EquipmentAndExposureTests
{
    private static RigSetup Demo16 => EquipmentInventory.Demo.ActiveRig!;

    [Fact]
    public void DemoProfile_IsTheOwnersGear_WithTheTripodRigActive()
    {
        var demo = EquipmentInventory.Demo;

        demo.ActiveRig!.Name.Should().Contain("16 mm");
        demo.Resolve("t7-starsense-130")!.FocalLengthMm.Should().Be(650);
        demo.Resolve("missing").Should().BeNull();
        demo.Cameras.Should().ContainSingle().Which.Name.Should().Contain("T7");
    }

    [Fact]
    public void Rig_DerivesFieldScaleAndFocalRatio_FromItsParts()
    {
        // Canon T7 (22.3 × 14.9 mm, 3.72 µm) behind 16 mm f/2: 69.75° × 49.94°, 48″/px.
        var rig = Demo16;

        rig.FocalRatio.Should().BeApproximately(2.0, 1e-12);
        rig.FieldOfView.WidthDegrees.Should().BeApproximately(69.75, 0.01);
        rig.FieldOfView.HeightDegrees.Should().BeApproximately(49.94, 0.01);
        rig.ImageScaleArcsecPerPixel.Should().BeApproximately(47.96, 0.01);
        rig.Camera.CropFactor.Should().BeApproximately(1.61, 0.01);
    }

    [Fact]
    public void Reducer_ShortensTheFocalLength_AndSpeedsUpTheRatio()
    {
        var demo = EquipmentInventory.Demo;
        var rig = demo.Rigs[1] with { FocalMultiplier = 0.8 };
        var setup = RigSetup.Resolve(rig, demo.Cameras, demo.Optics, demo.Mounts)!;

        setup.FocalLengthMm.Should().BeApproximately(520, 1e-9);
        setup.FocalRatio.Should().BeApproximately(4.0, 1e-9);
    }

    [Fact]
    public void Camera_DerivesThePixelPitch_WhenNotGiven()
    {
        var camera = new Camera("c", "Test", CameraKind.AstroCamera, SensorColor.Mono, 23.5, 15.7, 6248, 4176);

        camera.EffectivePixelSizeUm.Should().BeApproximately(3.761, 0.001);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(20, 1.5)]
    [InlineData(150, 0.5)]
    public void Camera_RejectsImpossibleValues(double sensorWidth, double qe)
    {
        var act = () => new Camera("c", "Bad", CameraKind.Dslr, SensorColor.Color, sensorWidth, 10, 100, 100, quantumEfficiency: qe);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void MountAndRig_RejectImpossibleValues()
    {
        var mount = () => new Mount("m", "Bad", MountKind.StarTracker, maxUnguidedExposureSeconds: -5);
        var rig = () => new Rig("r", "Bad", "c", "o", "m", focalMultiplier: 9);

        mount.Should().Throw<ArgumentOutOfRangeException>();
        rig.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Optic_RejectsAFocalRatioFasterThanHalf()
    {
        var act = () => new Optic("o", "Bad", OpticKind.CameraLens, 10, 30);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Npf_MatchesTheRuleAndItsDeclinationCorrection()
    {
        // (35 · 2 + 30 · 3.72) / 16 = 181.6 / 16 = 11.35 s.
        var npf = ExposureLimits.Npf(2.0, 3.72, 16);

        npf.Should().BeApproximately(11.35, 1e-9);
        ExposureLimits.NpfAtDeclination(npf, 60).Should().BeApproximately(22.7, 1e-6);
        ExposureLimits.NpfAtDeclination(npf, -89).Should().BeApproximately(npf / Math.Cos(double.DegreesToRadians(75)), 1e-9);
        ExposureLimits.Rule500(16, 1.61).Should().BeApproximately(19.41, 0.01);
    }

    [Theory]
    [InlineData(MountKind.StarTracker, 24, 120)]
    [InlineData(MountKind.StarTracker, 85, 60)]
    [InlineData(MountKind.StarTracker, 200, 30)]
    [InlineData(MountKind.StarTracker, 400, 15)]
    [InlineData(MountKind.AltAz, 650, 30)]
    [InlineData(MountKind.Equatorial, 200, 120)]
    [InlineData(MountKind.Equatorial, 650, 60)]
    [InlineData(MountKind.EquatorialGuided, 650, 300)]
    public void MountDefaults_FollowTheSpec(MountKind kind, double focal, double expected)
    {
        ExposureLimits.DefaultMountLimitSeconds(kind, focal).Should().Be(expected);
    }

    [Fact]
    public void MountLimit_UsesNpfOnATripod_AndTheUserValueOnATracker()
    {
        var (seconds, source) = ExposureLimits.MountLimit(Demo16, 0);
        seconds.Should().BeApproximately(11.35, 1e-9);
        source.Should().Be(ExposureLimitSource.Npf);

        var tracked = Demo16 with { Mount = new Mount("t", "Tracker", MountKind.StarTracker, maxUnguidedExposureSeconds: 90) };
        ExposureLimits.MountLimit(tracked, 0).Should().Be((90.0, ExposureLimitSource.Mount));
        var defaults = Demo16 with { Mount = new Mount("t", "Tracker", MountKind.StarTracker) };
        ExposureLimits.MountLimit(defaults, 0).Seconds.Should().Be(120);
        var fixedTripod = () => ExposureLimits.DefaultMountLimitSeconds(MountKind.FixedTripod, 16);
        fixedTripod.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SkyLimitedSub_SwampsTheReadNoiseTenTimes()
    {
        // 10 · 3² / 0.5 e⁻/s = 180 s.
        ExposureLimits.SkyLimitedSeconds(0.5, 3).Should().BeApproximately(180, 1e-9);
        ExposureLimits.SkyLimitedSeconds(0, 3).Should().Be(double.PositiveInfinity);
    }

    [Theory]
    [InlineData(11.35, 10)]
    [InlineData(14.9, 13)]
    [InlineData(60, 60)]
    [InlineData(700, 600)]
    [InlineData(0.44, 0.4)]
    [InlineData(0.04, 0.1)]
    [InlineData(0, 0)]
    public void RoundDownToNice_PicksCameraValues(double seconds, double expected)
    {
        ExposureLimits.RoundDownToNice(seconds).Should().Be(expected);
    }

    [Fact]
    public void Sampling_ComparesSeeingWithThePixelScale()
    {
        OpticsMath.SamplingFor(2.5, 47.96).Verdict.Should().Be(SamplingVerdict.Undersampled);
        OpticsMath.SamplingFor(2.5, 1.0).Verdict.Should().Be(SamplingVerdict.Good);
        var oversampled = OpticsMath.SamplingFor(3, 0.5);
        oversampled.Verdict.Should().Be(SamplingVerdict.Oversampled);
        oversampled.FwhmPixels.Should().Be(6);
        OpticsMath.Pixels(1, 1.2).Should().BeApproximately(50, 1e-9);
    }
}
