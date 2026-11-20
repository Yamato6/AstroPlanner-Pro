using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Tests;

/// <summary>Spec §4.6: greedy session scheduler (overlaps, priorities, locks, edges).</summary>
public sealed class SessionSchedulerTests
{
    private static readonly TimeZoneInfo MexicoZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
    private static readonly ObserverLocation Aguascalientes = new(21.88, -102.29, 1888);
    private static readonly DateTime WinterEvening = new(2026, 12, 9, 21, 0, 0);

    private readonly AstronomyService _astronomy = new();

    private static RigSetup Rokinon16 => EquipmentInventory.Demo.ActiveRig!;

    private static RigSetup OnEquatorial(RigSetup rig) => rig with { Mount = new Mount("eq", "EQ mount", MountKind.Equatorial) };

    private FeasibilityRequest Request(RigSetup? rig = null, double minimumAltitude = 20, DateTime? evening = null) =>
        new(Aguascalientes, MexicoZone, _astronomy.GetNightInfo(Aguascalientes, evening ?? WinterEvening, MexicoZone),
            minimumAltitude, rig ?? Rokinon16, 18.6, SkyQualitySource.Manual);

    private static FeasibilityReport Report(FeasibilityVerdict verdict = FeasibilityVerdict.Good, double sub = 30,
        double usableHours = 4, double snr = 20) => new()
    {
        Verdict = verdict,
        Score = 60,
        Reasons = [],
        Framing = new FramingAssessment(FramingClass.Good, 500, 300, 0.5, 1),
        SubSeconds = sub,
        UsableHours = usableHours,
        SignalToNoise = snr
    };

    /// <summary>A target usable on dark samples in [from, to), quality peaking at <paramref name="peak"/>, transit at <paramref name="peak"/>.</summary>
    private static TargetNightProfile Synthetic(NightSkySamples night, int from, int to, int peak)
    {
        var n = night.Count;
        var usable = new bool[n];
        var quality = new double[n];
        var hourAngle = new double[n];
        var altitude = new double[n];
        for (var k = 0; k < n; k++)
        {
            hourAngle[k] = (k - peak) * 1.25;   // 5-minute samples
            altitude[k] = 60 - Math.Abs(k - peak) * 0.5;
            usable[k] = k >= from && k < to && night.IsDark[k];
            quality[k] = usable[k] ? 1.0 / (1 + Math.Abs(k - peak)) : 0;
        }

        return new TargetNightProfile(altitude, hourAngle, usable, quality);
    }

    private static (int First, int Last) DarkRange(NightSkySamples night)
    {
        var first = Array.IndexOf(night.IsDark, true);
        var last = Array.LastIndexOf(night.IsDark, true);
        return (first, last);
    }

    private static int Index(NightSkySamples night, DateTime local) =>
        Array.FindIndex(night.TimesUtc, t => Algorithms.TimeZoneConversion.ToLocal(t, MexicoZone) >= local);

    [Fact]
    public void SingleTarget_TakesAllItsUsableTime()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, last) = DarkRange(night);
        var target = new SessionTarget("A", Synthetic(night, first + 10, last - 10, (first + last) / 2), Report());

        var result = SessionScheduler.Schedule([target], night, request).Targets.Single();

        result.Status.Should().Be(ScheduleStatus.Scheduled);
        Index(night, result.StartLocal!.Value).Should().Be(first + 10);
        Index(night, result.EndLocal!.Value).Should().Be(last - 10);
        result.UsableHours.Should().BeApproximately((last - 10 - (first + 10)) * 5 / 60.0, 1e-6);
    }

    [Fact]
    public void OverlappingTargets_NeverShareTime_AndFillTheNight()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, last) = DarkRange(night);
        var middle = (first + last) / 2;
        var targets = new[]
        {
            new SessionTarget("East", Synthetic(night, first, last + 1, last), Report()),
            new SessionTarget("West", Synthetic(night, first, last + 1, first), Report()),
            new SessionTarget("Zenith", Synthetic(night, first, last + 1, middle), Report())
        };

        var schedule = SessionScheduler.Schedule(targets, night, request);

        var blocks = schedule.Targets.Select(t => (t.Id, Start: t.StartLocal!.Value, End: t.EndLocal!.Value)).OrderBy(b => b.Start).ToList();
        blocks.Select(b => b.Id).Should().Equal("West", "Zenith", "East");
        for (var i = 1; i < blocks.Count; i++)
        {
            blocks[i].Start.Should().BeOnOrAfter(blocks[i - 1].End);
        }

        schedule.IdleDarkHours.Should().BeApproximately(0, 1e-9);
        schedule.DarkHours.Should().BeGreaterThan(9);
    }

    [Fact]
    public void HigherPriority_GetsTheBestStretch()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, last) = DarkRange(night);
        var peak = (first + last) / 2;
        var profile = Synthetic(night, first, last + 1, peak);

        var schedule = SessionScheduler.Schedule(
            [new SessionTarget("First", profile, Report()), new SessionTarget("Second", profile, Report())], night, request);

        var firstBlock = schedule.Targets[0];
        var peakTime = Algorithms.TimeZoneConversion.ToLocal(night.TimesUtc[peak], MexicoZone);
        firstBlock.StartLocal.Should().BeOnOrBefore(peakTime);
        firstBlock.EndLocal.Should().BeAfter(peakTime);
        schedule.Targets[1].Status.Should().Be(ScheduleStatus.Scheduled);
    }

    [Fact]
    public void LockedBlock_IsKept_AndOthersWorkAroundIt()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, last) = DarkRange(night);
        var peak = (first + last) / 2;
        var lockedStart = Algorithms.TimeZoneConversion.ToLocal(night.TimesUtc[peak - 6], MexicoZone);
        var lockedEnd = Algorithms.TimeZoneConversion.ToLocal(night.TimesUtc[peak + 6], MexicoZone);
        var profile = Synthetic(night, first, last + 1, peak);
        var targets = new[]
        {
            new SessionTarget("Free", profile, Report()),
            new SessionTarget("Pinned", profile, Report()) { Locked = true, LockedStartLocal = lockedStart, LockedEndLocal = lockedEnd }
        };

        var schedule = SessionScheduler.Schedule(targets, night, request);

        var pinned = schedule.Targets[1];
        pinned.Status.Should().Be(ScheduleStatus.Locked);
        pinned.StartLocal.Should().Be(lockedStart);
        pinned.EndLocal.Should().Be(lockedEnd);
        var free = schedule.Targets[0];
        (free.EndLocal <= lockedStart || free.StartLocal >= lockedEnd).Should().BeTrue();
    }

    [Fact]
    public void ImpossibleTarget_AndTargetWithoutFreeTime_AreReported()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, last) = DarkRange(night);
        var narrow = Synthetic(night, first, first + 12, first + 6);
        var targets = new[]
        {
            new SessionTarget("Owner", narrow, Report()),
            new SessionTarget("Late", narrow, Report()),
            new SessionTarget("Never", Synthetic(night, 0, 0, 0), Report(FeasibilityVerdict.NotPossible)),
            new SessionTarget("Model says no", Synthetic(night, first, last, first), Report(FeasibilityVerdict.NotPossible))
        };

        var schedule = SessionScheduler.Schedule(targets, night, request);

        schedule.Targets.Select(t => t.Status).Should().Equal(
            ScheduleStatus.Scheduled, ScheduleStatus.NoFreeTime, ScheduleStatus.NotPossibleTonight, ScheduleStatus.NotPossibleTonight);
        schedule.Targets[1].HasBlock.Should().BeFalse();
    }

    [Fact]
    public void ShortVisibility_IsScheduledEvenUnderTheMinimumBlock()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, _) = DarkRange(night);

        var result = SessionScheduler.Schedule([new SessionTarget("Brief", Synthetic(night, first, first + 2, first), Report())], night, request)
            .Targets.Single();

        result.Status.Should().Be(ScheduleStatus.Scheduled);
        result.UsableHours.Should().BeApproximately(10 / 60.0, 1e-6);
    }

    [Fact]
    public void Subs_FillTheUsableTime_WithOverhead()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var (first, _) = DarkRange(night);

        var result = SessionScheduler.Schedule(
            [new SessionTarget("A", Synthetic(night, first, first + 12, first + 6), Report(sub: 58, usableHours: 4, snr: 20))], night, request)
            .Targets.Single();

        // 60 min usable, 58 s + 2 s overhead per sub.
        result.SubSeconds.Should().Be(58);
        result.SubCount.Should().Be(60);
        result.Integration.Should().Be(TimeSpan.FromSeconds(60 * 58));
        result.Iso.Should().Be(800);
        result.EstimatedSignalToNoise.Should().BeApproximately(20 * Math.Sqrt(1 / 4.0), 1e-9);
    }

    [Fact]
    public void MeridianFlip_IsGivenOnlyForEquatorialMounts()
    {
        var night = FeasibilityCalculator.CreateSamples(Request());
        var (first, last) = DarkRange(night);
        var peak = (first + last) / 2;
        var target = new SessionTarget("A", Synthetic(night, first, last, peak), Report());

        var altAz = SessionScheduler.Schedule([target], night, Request()).Targets.Single();
        var eq = SessionScheduler.Schedule([target], night, Request(OnEquatorial(Rokinon16))).Targets.Single();

        altAz.MeridianFlipLocal.Should().BeNull();
        eq.MeridianFlipLocal.Should().Be(Algorithms.TimeZoneConversion.ToLocal(night.TimesUtc[peak], MexicoZone));
    }

    [Fact]
    public void RealTargets_AreScheduledInTheOrderTheySetAndRise()
    {
        // Winter evening from Aguascalientes: M31 sets in the evening, M42 culminates near midnight.
        var request = Request(OnEquatorial(EquipmentInventory.Demo.Resolve("t7-starsense-130")!));
        var night = FeasibilityCalculator.CreateSamples(request);
        var calculator = new FeasibilityCalculator(_astronomy);
        var targets = new[] { TestCatalog.M42, TestCatalog.M31 }
            .Select(o => new SessionTarget(o.Id, calculator.Profile(o, request, night), calculator.Evaluate(o, request, night)))
            .ToList();

        var schedule = SessionScheduler.Schedule(targets, night, request);
        var again = SessionScheduler.Schedule(targets, night, request);

        schedule.Should().BeEquivalentTo(again);
        var m42 = schedule.Targets[0];
        var m31 = schedule.Targets[1];
        m42.Status.Should().Be(ScheduleStatus.Scheduled);
        m31.Status.Should().Be(ScheduleStatus.Scheduled);
        m31.StartLocal.Should().BeBefore(m42.StartLocal!.Value);
        m31.EndLocal.Should().BeOnOrBefore(m42.StartLocal!.Value);
        m42.MeridianFlipLocal.Should().NotBeNull();
        // LST at local midnight ≈ 4.4 h and M42's RA = 5.59 h: transit ≈ 01:10.
        m42.MeridianFlipLocal!.Value.Should().BeCloseTo(new DateTime(2026, 12, 10, 1, 10, 0), TimeSpan.FromMinutes(10));
        m42.SubCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Profile_MatchesTheReport()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var calculator = new FeasibilityCalculator(_astronomy);

        var profile = calculator.Profile(TestCatalog.M42, request, night);
        var report = calculator.Evaluate(TestCatalog.M42, request, night);

        var usableHours = Enumerable.Range(0, night.Count).Where(k => profile.Usable[k]).Sum(k => night.DurationsSeconds[k]) / 3600;
        usableHours.Should().BeApproximately(report.GeometricHours, 1e-9);
        profile.AltitudeDegrees.Max().Should().BeApproximately(report.MaximumAltitudeDegrees, 1e-9);
        profile.Quality.Where((q, k) => !profile.Usable[k]).Should().OnlyContain(q => q == 0);
        profile.Quality.Where((q, k) => profile.Usable[k]).Should().OnlyContain(q => q > 0);
        profile.HourAngleDegrees.Should().OnlyContain(h => h >= -180 && h <= 180);
    }

    [Fact]
    public void ProfileOfAnotherNight_IsRejected()
    {
        var request = Request();
        var night = FeasibilityCalculator.CreateSamples(request);
        var other = new TargetNightProfile([1.0], [0.0], [true], [1.0]);

        var act = () => SessionScheduler.Schedule([new SessionTarget("X", other, Report())], night, request);

        act.Should().Throw<ArgumentException>();
    }
}
