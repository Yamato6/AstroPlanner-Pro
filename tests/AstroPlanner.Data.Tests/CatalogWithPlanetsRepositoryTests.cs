using AstroPlanner.Core.Models;
using AstroPlanner.Data.Repositories;

namespace AstroPlanner.Data.Tests;

public sealed class CatalogWithPlanetsRepositoryTests
{
    [Fact]
    public async Task CatalogWithPlanets_AddsTheSevenPlanetsAfterTheMessierObjects()
    {
        var repository = new CatalogWithPlanetsRepository(new JsonCelestialRepository());

        var objects = await repository.GetAllAsync();

        objects.Should().HaveCount(117);
        objects.Where(o => o.IsPlanet).Select(o => o.CommonName)
            .Should().Equal("Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune");
        objects.Where(o => o.IsPlanet).Should().OnlyContain(o =>
            o.Type == CelestialObjectType.Planet && o.Constellation == CelestialObject.SolarSystemConstellation);
    }
}
