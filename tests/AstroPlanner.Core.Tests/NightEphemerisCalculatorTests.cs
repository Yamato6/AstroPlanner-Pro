using System.Diagnostics;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.Services;

namespace AstroPlanner.Core.Tests;

public sealed class NightEphemerisCalculatorTests
{
    private static readonly TimeZoneInfo MadridZone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
    private static readonly ObserverLocation Madrid = new(40.42, -3.70);

    private readonly AstronomyService _astronomy = new();
    private readonly NightEphemerisCalculator _calculator;
    private readonly NightInfo _night;

    public NightEphemerisCalculatorTests()
    {
        _calculator = new NightEphemerisCalculator(_astronomy);
        _night = _astronomy.GetNightInfo(Madrid, new DateTime(2023, 12, 15, 22, 0, 0), MadridZone);
    }

    private NightEphemerisRequest Request(double minimumAltitude = 20, HorizonMask? mask = null) =>
        new(Madrid, MadridZone, _night, minimumAltitude, mask);

    [Theory]
    [InlineData("M42")]
    [InlineData("M31")]
    [InlineData("M13")]
    public void FixedObject_AgreesWithTheRefinedEventSearch(string id)
    {
        var target = new[] { TestCatalog.M42, TestCatalog.M31, TestCatalog.M13 }.Single(t => t.Id == id);

        var ephemeris = _calculator.Calculate(target, Request());
        var events = _astronomy.GetNightEvents(target, Madrid, _night.WindowStartLocal, _night.WindowEndLocal, MadridZone, 20);

        ephemeris.MaximumAltitudeDegrees.Should().BeApproximately(events.MaximumAltitudeDegrees, 0.02);
        if (ephemeris.TransitLocal is { } transit)
        {
            transit.Should().BeCloseTo(events.CulminationLocal, TimeSpan.FromMinutes(1));
        }
    }

    [Fact]
    public void M42InDecember_BestWindowStartsWhenItClimbsAbove20Degrees()
    {
        var ephemeris = _calculator.Calculate(TestCatalog.M42, Request());
        var events = _astronomy.GetNightEvents(TestCatalog.M42, Madrid, _night.WindowStartLocal, _night.WindowEndLocal, MadridZone, 20);

        // Above 20° from ~21:20 to ~05:05, inside the dark night (18:47 → 06:53): the window
        // starts and ends where it crosses 20°.
        ephemeris.TransitLocal.Should().NotBeNull();
        ephemeris.BestWindowStartLocal.Should().BeCloseTo(events.ThresholdCrossingLocal!.Value, TimeSpan.FromMinutes(1));
        ephemeris.BestWindowEndLocal.Should().BeBefore(_night.AstronomicalDawnLocal!.Value);
        AltitudeAt(ephemeris.BestWindowEndLocal!.Value).Should().BeApproximately(20, 0.05);
        ephemeris.TimeAboveMinimumInDarkness.Should().Be(ephemeris.BestWindowEndLocal!.Value - ephemeris.BestWindowStartLocal!.Value);
        ephemeris.IsObservableInDarkness.Should().BeTrue();
    }

    private double AltitudeAt(DateTime local) =>
        _astronomy.GetHorizontal(TestCatalog.M42, Madrid, TimeZoneConversion.ToUtc(local, MadridZone)).AltitudeDegrees;

    [Fact]
    public void ObjectThatNeverRises_HasNoWindow()
    {
        var target = new CelestialObject("S", "South", "Oct", CelestialObjectType.Unknown, 5, new EquatorialCoordinates(3, -80));

        var ephemeris = _calculator.Calculate(target, Request());

        ephemeris.MaximumAltitudeDegrees.Should().BeLessThan(0);
        ephemeris.TimeAboveMinimumInDarkness.Should().Be(TimeSpan.Zero);
        ephemeris.BestWindowStartLocal.Should().BeNull();
        ephemeris.IsObservableInDarkness.Should().BeFalse();
    }

    [Fact]
    public void HorizonMask_ShortensTheWindow()
    {
        // A wall 35° high towards the south-east hides M42 while it climbs.
        var wall = new HorizonMask([new HorizonPoint(90, 35), new HorizonPoint(179, 35), new HorizonPoint(181, 0), new HorizonPoint(89, 0)]);

        var open = _calculator.Calculate(TestCatalog.M42, Request());
        var masked = _calculator.Calculate(TestCatalog.M42, Request(mask: wall));

        masked.TimeAboveMinimumInDarkness.Should().BeLessThan(open.TimeAboveMinimumInDarkness);
        masked.BestWindowStartLocal.Should().BeAfter(open.BestWindowStartLocal!.Value);
        masked.MaximumAltitudeDegrees.Should().Be(open.MaximumAltitudeDegrees);
    }

    [Fact]
    public void NoAstronomicalDarkness_MeansNoWindow()
    {
        var oslo = new ObserverLocation(59.91, 10.75);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var night = _astronomy.GetNightInfo(oslo, new DateTime(2026, 6, 21, 23, 0, 0), zone);

        var ephemeris = _calculator.Calculate(TestCatalog.M13, new NightEphemerisRequest(oslo, zone, night, 20));

        ephemeris.MaximumAltitudeDegrees.Should().BeGreaterThan(20);
        ephemeris.TimeAboveMinimumInDarkness.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Planet_UsesTheMovingBodyPath()
    {
        var jupiter = SolarSystemCatalog.Planets.Single(p => p.Planet == Planet.Jupiter);

        var ephemeris = _calculator.Calculate(jupiter, Request());
        var events = _astronomy.GetNightEvents(jupiter, Madrid, _night.WindowStartLocal, _night.WindowEndLocal, MadridZone, 20);

        ephemeris.MaximumAltitudeDegrees.Should().BeApproximately(events.MaximumAltitudeDegrees, 0.01);
        ephemeris.TransitLocal.Should().BeCloseTo(events.CulminationLocal, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Calculate_KeepsTheOrderOfTheTargets()
    {
        var targets = new[] { TestCatalog.M13, TestCatalog.M42, TestCatalog.M31 };

        var all = _calculator.Calculate(targets, Request());

        all.Should().HaveCount(3);
        all[1].Should().Be(_calculator.Calculate(TestCatalog.M42, Request()));
    }

    [Fact]
    public void Calculate_CanBeCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => _calculator.Calculate([TestCatalog.M42], Request(), cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void TwentyFiveThousandObjects_TakeLessThanOneSecond()
    {
        // DoD of M1: the night ephemerides of the whole catalog in < 1 s (~22.5k objects with the stars of M6a).
        var random = new Random(42);
        var catalog = Enumerable.Range(0, 25_000)
            .Select(i => new CelestialObject($"T{i}", "Test", "Test", CelestialObjectType.Galaxy, 10,
                new EquatorialCoordinates(random.NextDouble() * 24, random.NextDouble() * 180 - 90)))
            .ToList();
        var mask = new HorizonMask([new HorizonPoint(0, 10), new HorizonPoint(180, 25)]);
        _calculator.Calculate(catalog.Take(100).ToList(), Request(mask: mask));

        // Best of three runs, to leave out JIT and scheduling noise. CI runs the Performance
        // tests in a step of their own, without coverage instrumentation.
        var best = TimeSpan.MaxValue;
        IReadOnlyList<NightEphemeris> all = [];
        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            all = _calculator.Calculate(catalog, Request(mask: mask));
            stopwatch.Stop();
            best = stopwatch.Elapsed < best ? stopwatch.Elapsed : best;
        }

        all.Should().HaveCount(25_000);
        best.Should().BeLessThan(PerformanceBudget.Of(TimeSpan.FromSeconds(1)));
    }
}

public sealed class PrecessionBetweenEpochsTests
{
    [Fact]
    public void Precess_ThereAndBack_ReturnsTheOriginalPosition()
    {
        var j2000 = new EquatorialCoordinates(2.5302, 89.2641);

        var b1875 = Precession.Precess(j2000, Precession.J2000JulianDate, Precession.B1875JulianDate);
        var back = Precession.Precess(b1875, Precession.B1875JulianDate, Precession.J2000JulianDate);

        back.RightAscensionHours.Should().BeApproximately(j2000.RightAscensionHours, 1e-5);
        back.DeclinationDegrees.Should().BeApproximately(j2000.DeclinationDegrees, 1e-6);
        b1875.RightAscensionHours.Should().NotBeApproximately(j2000.RightAscensionHours, 0.1);
    }

    [Fact]
    public void Precess_ToTheSameEquinox_IsTheIdentity()
    {
        var position = new EquatorialCoordinates(5.59, -5.39);

        var same = Precession.Precess(position, Precession.B1950JulianDate, Precession.B1950JulianDate);

        same.RightAscensionHours.Should().BeApproximately(5.59, 1e-12);
        same.DeclinationDegrees.Should().BeApproximately(-5.39, 1e-12);
    }

    [Fact]
    public void Precess_B1950ToJ2000_MatchesAStandardReduction()
    {
        // Meeus example 21.b runs from J2000; here B1950 → J2000 for Polaris must agree with the
        // FK4 → FK5 values of the catalogue to within the size of the omitted E-terms (< 1').
        // B1950: 1h48m48.786s +89°01'43.74"  →  J2000: 2h31m49.09s +89°15'50.8"
        var b1950 = new EquatorialCoordinates(1 + 48 / 60.0 + 48.786 / 3600.0, 89 + 1 / 60.0 + 43.74 / 3600.0);

        var j2000 = Precession.Precess(b1950, Precession.B1950JulianDate, Precession.J2000JulianDate);

        AstronomyMath.AngularSeparationDegrees(j2000,
            new EquatorialCoordinates(2 + 31 / 60.0 + 49.09 / 3600.0, 89 + 15 / 60.0 + 50.8 / 3600.0))
            .Should().BeLessThan(1 / 60.0);
    }
}

public sealed class TimeZoneIdTests
{
    [Fact]
    public void WindowsAndIanaIds_ConvertBothWays()
    {
        TimeZoneConversion.ToIanaId("Central Standard Time (Mexico)").Should().Be("America/Mexico_City");
        TimeZoneConversion.ToWindowsId("America/Mexico_City").Should().Be("Central Standard Time (Mexico)");
        TimeZoneConversion.ToIanaId("America/Mexico_City").Should().Be("America/Mexico_City");
        TimeZoneConversion.ToWindowsId("Romance Standard Time").Should().Be("Romance Standard Time");
        TimeZoneConversion.ToIanaId("Not a zone").Should().BeNull();
        TimeZoneConversion.ToWindowsId(null).Should().BeNull();
    }

    [Fact]
    public void FindOrLocal_AcceptsIanaIds()
    {
        TimeZoneConversion.FindOrLocal("America/Mexico_City").BaseUtcOffset.Should().Be(TimeSpan.FromHours(-6));
    }

    [Fact]
    public void ToLocal_IsTheInverseOfToUtc()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        var local = new DateTime(2026, 7, 1, 23, 30, 0);

        var roundTrip = TimeZoneConversion.ToLocal(TimeZoneConversion.ToUtc(local, zone), zone);

        roundTrip.Should().Be(local);
        roundTrip.Kind.Should().Be(DateTimeKind.Unspecified);
    }
}
