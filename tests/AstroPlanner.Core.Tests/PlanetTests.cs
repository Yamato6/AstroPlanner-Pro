using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;

namespace AstroPlanner.Core.Tests;

/// <summary>
/// Planets: Meeus worked examples (Venus, 33.a and 41.a) and published oppositions and
/// magnitudes for 2025-2026.
/// </summary>
public sealed class PlanetTests
{
    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Venus_MatchesMeeusExample33a()
    {
        // 1992-12-20 0h TD: α = 21h04m41.454s = 316.17273°, δ = -18°53'16.84", Δ = 0.910947 AU.
        var venus = PlanetCalculator.GetPosition(Planet.Venus, Utc(1992, 12, 20));

        (venus.Equatorial.RightAscensionHours * 15.0).Should().BeApproximately(316.17273, 0.05);
        venus.Equatorial.DeclinationDegrees.Should().BeApproximately(-18.88801, 0.05);
        venus.DistanceAu.Should().BeApproximately(0.910947, 0.002);
        venus.SunDistanceAu.Should().BeApproximately(0.724604, 0.002);
    }

    [Fact]
    public void Venus_PhaseMatchesMeeusExample41a()
    {
        // Same instant: illuminated fraction k = 0.647, phase angle ≈ 72.96°.
        var venus = PlanetCalculator.GetPosition(Planet.Venus, Utc(1992, 12, 20));

        venus.IlluminatedFraction.Should().BeApproximately(0.647, 0.01);
        venus.PhaseAngleDegrees.Should().BeApproximately(72.96, 0.5);
        venus.Magnitude.Should().BeInRange(-4.6, -3.8);
    }

    [Theory]
    [InlineData(Planet.Mars, 2025, 1, 16, -1.4, 0.3)]     // Mars opposition
    [InlineData(Planet.Saturn, 2025, 9, 21, 0.6, 0.3)]    // Saturn opposition (rings almost edge-on)
    [InlineData(Planet.Jupiter, 2026, 1, 10, -2.7, 0.3)]  // Jupiter opposition
    public void Oppositions_AreOppositeTheSunWithPublishedMagnitude(
        Planet planet, int year, int month, int day, double magnitude, double tolerance)
    {
        var position = PlanetCalculator.GetPosition(planet, Utc(year, month, day));

        position.ElongationDegrees.Should().BeGreaterThan(175, "at opposition the planet faces the Sun");
        position.Magnitude.Should().BeApproximately(magnitude, tolerance);
        position.IlluminatedFraction.Should().BeGreaterThan(0.99);
    }

    [Fact]
    public void Jupiter_AtOppositionHasAbout46ArcsecondsDiameter()
    {
        var jupiter = PlanetCalculator.GetPosition(Planet.Jupiter, Utc(2026, 1, 10));

        jupiter.ApparentDiameterArcsec.Should().BeApproximately(46.5, 1.5);
    }

    [Fact]
    public void InnerPlanets_NeverStrayFarFromTheSun()
    {
        // Maximum elongation: Mercury ~28°, Venus ~47°, over two years.
        for (var day = 0; day < 730; day += 5)
        {
            var when = Utc(2025, 1, 1).AddDays(day);
            PlanetCalculator.GetPosition(Planet.Mercury, when).ElongationDegrees.Should().BeLessThan(28.5);
            PlanetCalculator.GetPosition(Planet.Venus, when).ElongationDegrees.Should().BeLessThan(47.5);
        }
    }

    [Fact]
    public void AstronomyService_UsesThePlanetPositionOfTheDate()
    {
        var service = new AstronomyService();
        var mars = SolarSystemCatalog.Planets.Single(p => p.Planet == Planet.Mars);
        var observer = new ObserverLocation(19.4326, -99.1332);
        var when = Utc(2025, 1, 16, 6);

        var horizontal = service.GetHorizontal(mars, observer, when);
        var expected = CoordinateTransformer.ToHorizontal(
            PlanetCalculator.GetPosition(Planet.Mars, when).Equatorial, observer, when);

        horizontal.AzimuthDegrees.Should().BeApproximately(expected.AzimuthDegrees, 1e-9);
        horizontal.AltitudeDegrees.Should().BeApproximately(Refraction.ApparentAltitude(expected.AltitudeDegrees), 1e-9);
        service.GetApparentMagnitude(mars, when).Should().BeApproximately(-1.4, 0.3);
        service.GetPlanetPosition(TestCatalog.M42, when).Should().BeNull();
        service.GetApparentMagnitude(TestCatalog.M42, when).Should().Be(4.0);
    }

    [Fact]
    public void SolarSystemCatalog_HasTheSevenPlanetsWithEnglishNames()
    {
        SolarSystemCatalog.Planets.Select(p => p.CommonName)
            .Should().Equal("Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune");
        SolarSystemCatalog.Planets.Should().OnlyContain(p => p.Id == p.CommonName && p.IsPlanet);
    }
}
