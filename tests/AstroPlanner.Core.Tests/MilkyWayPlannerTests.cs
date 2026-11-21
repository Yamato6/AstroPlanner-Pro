using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.Services;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>Milky Way core planner: seasons, latitudes and the Moon.</summary>
public sealed class MilkyWayPlannerTests
{
    private static readonly AstronomyService Astronomy = new();
    private static readonly TimeZoneInfo Mexico = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time (Mexico)");
    private static readonly ObserverLocation Aguascalientes = new(21.88, -102.29, 1888);

    [Fact]
    public void Core_IsTheGalacticCentreInSagittarius()
    {
        GalacticCenter.Target.Coordinates.RightAscensionHours.Should().BeApproximately(17.7611, 1e-4);
        GalacticCenter.Target.Coordinates.DeclinationDegrees.Should().BeApproximately(-29.0078, 1e-4);
        ConstellationLookup.FromJ2000(GalacticCenter.Coordinates).Name.Should().Be("Sagittarius");
        GalacticCenter.Target.Type.Should().Be(CelestialObjectType.MilkyWayCore);
    }

    [Fact]
    public void Summer_HasHoursOfCore_Winter_None()
    {
        var june = MilkyWayPlanner.Night(Astronomy, Aguascalientes, Mexico, new DateOnly(2026, 6, 15));
        var december = MilkyWayPlanner.Night(Astronomy, Aguascalientes, Mexico, new DateOnly(2026, 12, 15));

        june.CoreInDarkness.Should().BeGreaterThan(TimeSpan.FromHours(4));
        june.StartLocal.Should().NotBeNull();
        june.EndLocal.Should().BeAfter(june.StartLocal!.Value);
        december.CoreInDarkness.Should().Be(TimeSpan.Zero);
        december.StartLocal.Should().BeNull();
        december.IsGood.Should().BeFalse();
    }

    [Fact]
    public void FullMoon_TakesTheMoonFreeTimeAway()
    {
        // 2026-06-29 is a full Moon, 2026-06-15 a new Moon.
        var newMoon = MilkyWayPlanner.Night(Astronomy, Aguascalientes, Mexico, new DateOnly(2026, 6, 15));
        var fullMoon = MilkyWayPlanner.Night(Astronomy, Aguascalientes, Mexico, new DateOnly(2026, 6, 29));

        newMoon.MoonIlluminatedFraction.Should().BeLessThan(0.05);
        newMoon.CoreMoonFree.Should().BeCloseTo(newMoon.CoreInDarkness, TimeSpan.FromMinutes(20));
        fullMoon.MoonIlluminatedFraction.Should().BeGreaterThan(0.95);
        fullMoon.CoreMoonFree.Should().BeLessThan(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void FarNorth_NeverSeesTheCoreHighEnough()
    {
        var oslo = new ObserverLocation(59.91, 10.75, 0);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");

        var nights = MilkyWayPlanner.Nights(Astronomy, oslo, zone, new DateOnly(2026, 1, 1), days: 365);

        nights.Should().HaveCount(365).And.OnlyContain(n => n.CoreInDarkness == TimeSpan.Zero, "the core culminates ~1° high at 60°N");
        MilkyWayPlanner.NextGood(nights).Should().BeNull();
    }

    [Fact]
    public void Year_FromWinter_FindsTheFirstGoodNightInSpring()
    {
        var nights = MilkyWayPlanner.Nights(Astronomy, Aguascalientes, Mexico, new DateOnly(2026, 12, 1), days: 365);

        var first = MilkyWayPlanner.NextGood(nights);
        first.Should().NotBeNull();
        first!.Date.Should().BeAfter(new DateOnly(2027, 1, 15)).And.BeBefore(new DateOnly(2027, 4, 1), "the core returns before dawn in late winter");
        nights.Count(n => n.IsGood).Should().BeInRange(100, 250);
    }
}
