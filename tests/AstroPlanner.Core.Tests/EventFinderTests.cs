using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;

namespace AstroPlanner.Core.Tests;

public sealed class EventFinderTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A sine wave with a 24-hour period, peaking (value 1) at 06:00.</summary>
    private static double Wave(DateTime time) => Math.Sin((time - Start).TotalHours / 24.0 * 2 * Math.PI);

    [Fact]
    public void FindCrossing_RisingAndSetting_AreRefinedToTheSecond()
    {
        var samples = EventFinder.Sample(Wave, Start, Start.AddHours(23), EventFinder.DefaultStep);

        // sin(x) = 0.5 going up at x = 30° → 02:00; going down at x = 150° → 10:00.
        EventFinder.FindCrossing(Wave, samples, 0.5, rising: true)
            .Should().BeCloseTo(Start.AddHours(2), TimeSpan.FromSeconds(1));
        EventFinder.FindCrossing(Wave, samples, 0.5, rising: false)
            .Should().BeCloseTo(Start.AddHours(10), TimeSpan.FromSeconds(1));
        EventFinder.FindCrossing(Wave, samples, 2, rising: true).Should().BeNull();
    }

    [Fact]
    public void FindMaximum_IsRefinedBetweenSamples()
    {
        // 07:03 is not a sample: the maximum must be found between samples.
        static double Parabola(DateTime time) => 10 - Math.Pow((time - Start.AddMinutes(7 * 60 + 3)).TotalHours, 2);
        var samples = EventFinder.Sample(Parabola, Start, Start.AddHours(12), EventFinder.DefaultStep);

        var maximum = EventFinder.FindMaximum(Parabola, samples);

        maximum.Time.Should().BeCloseTo(Start.AddMinutes(7 * 60 + 3), TimeSpan.FromSeconds(2));
        maximum.Value.Should().BeApproximately(10, 1e-6);
    }

    [Fact]
    public void FindMaximum_MonotonicFunction_IsAtTheEdge()
    {
        static double Rising(DateTime time) => (time - Start).TotalHours;
        var samples = EventFinder.Sample(Rising, Start, Start.AddHours(3), EventFinder.DefaultStep);

        EventFinder.FindMaximum(Rising, samples).Time.Should().Be(Start.AddHours(3));
    }

    [Fact]
    public void Sample_IncludesBothEnds_AndValidatesArguments()
    {
        var samples = EventFinder.Sample(Wave, Start, Start.AddMinutes(25), EventFinder.DefaultStep);

        samples.Select(s => s.Time).Should().Equal(Start, Start.AddMinutes(10), Start.AddMinutes(20), Start.AddMinutes(25));
        ((Action)(() => EventFinder.Sample(Wave, Start, Start.AddHours(1), TimeSpan.Zero))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => EventFinder.Sample(Wave, Start, Start.AddHours(-1), EventFinder.DefaultStep))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => EventFinder.FindMaximum(Wave, []))).Should().Throw<ArgumentException>();
    }
}

/// <summary>
/// Event times against JPL Horizons (tests/fixtures/horizons): the refined events must be
/// within one minute (spec §6).
/// </summary>
public sealed class HorizonsEventTests
{
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);
    private readonly AstronomyService _service = new();

    public static TheoryData<string, double, double, string, int, int, int> SunCases => new()
    {
        { "sun_aguascalientes_2026-06-20", 21.88, -102.29, "Central Standard Time (Mexico)", 2026, 6, 20 },
        { "sun_madrid_2023-12-15", 40.42, -3.70, "Romance Standard Time", 2023, 12, 15 },
    };

    [Theory]
    [MemberData(nameof(SunCases))]
    public void GetNightInfo_SunsetSunriseAndTwilight_MatchHorizons(
        string fixtureName, double latitude, double longitude, string timeZoneId, int year, int month, int day)
    {
        var fixture = HorizonsFixture.Load(fixtureName);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        var night = _service.GetNightInfo(new ObserverLocation(latitude, longitude), new DateTime(year, month, day, 22, 0, 0), zone);

        Utc(night.SunsetLocal, zone).Should().BeCloseTo(fixture.Crossing(-0.833, rising: false)!.Value, OneMinute);
        Utc(night.SunriseLocal, zone).Should().BeCloseTo(fixture.Crossing(-0.833, rising: true)!.Value, OneMinute);
        Utc(night.AstronomicalDuskLocal, zone).Should().BeCloseTo(fixture.Crossing(-18, rising: false)!.Value, OneMinute);
        Utc(night.AstronomicalDawnLocal, zone).Should().BeCloseTo(fixture.Crossing(-18, rising: true)!.Value, OneMinute);
    }

    [Fact]
    public void GetNightInfo_OsloAtMidsummer_HasNoAstronomicalDarkness_LikeHorizons()
    {
        var fixture = HorizonsFixture.Load("sun_oslo_2026-06-21");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        var night = _service.GetNightInfo(new ObserverLocation(59.91, 10.75), new DateTime(2026, 6, 21, 23, 0, 0), zone);

        fixture.MinimumElevation.Should().BeGreaterThan(-18);
        night.HasAstronomicalDarkness.Should().BeFalse();
        Utc(night.SunsetLocal, zone).Should().BeCloseTo(fixture.Crossing(-0.833, rising: false)!.Value, OneMinute);
        Utc(night.SunriseLocal, zone).Should().BeCloseTo(fixture.Crossing(-0.833, rising: true)!.Value, OneMinute);
    }

    public static TheoryData<string, Planet, double, double, string, int, int, int> PlanetCases => new()
    {
        { "jupiter_aguascalientes_2026-01-10", Planet.Jupiter, 21.88, -102.29, "Central Standard Time (Mexico)", 2026, 1, 10 },
        { "saturn_madrid_2025-09-21", Planet.Saturn, 40.42, -3.70, "Romance Standard Time", 2025, 9, 21 },
    };

    [Theory]
    [MemberData(nameof(PlanetCases))]
    public void GetNightEvents_PlanetRiseCulminationAndSet_MatchHorizons(
        string fixtureName, Planet planet, double latitude, double longitude, string timeZoneId, int year, int month, int day)
    {
        var fixture = HorizonsFixture.Load(fixtureName);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var target = SolarSystemCatalog.Planets.Single(p => p.Planet == planet);
        var start = new DateTime(year, month, day, 12, 0, 0);

        var events = _service.GetNightEvents(target, new ObserverLocation(latitude, longitude), start, start.AddDays(1), zone, 30);
        var culmination = fixture.Maximum();

        Utc(events.RiseLocal, zone).Should().BeCloseTo(fixture.Crossing(0, rising: true)!.Value, OneMinute);
        Utc(events.SetLocal, zone).Should().BeCloseTo(fixture.Crossing(0, rising: false)!.Value, OneMinute);
        Utc(events.ThresholdCrossingLocal, zone).Should().BeCloseTo(fixture.Crossing(30, rising: true)!.Value, OneMinute);
        Utc(events.CulminationLocal, zone).Should().BeCloseTo(culmination.Utc, OneMinute);
        events.MaximumAltitudeDegrees.Should().BeApproximately(culmination.Elevation, 0.05);
    }

    private static DateTime Utc(DateTime? local, TimeZoneInfo zone)
    {
        local.Should().NotBeNull();
        return TimeZoneConversion.ToUtc(local!.Value, zone);
    }
}
