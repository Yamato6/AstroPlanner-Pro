using AstroPlanner.Data.Repositories;

namespace AstroPlanner.Data.Tests;

public class JsonCelestialRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsAll110MessierObjects()
    {
        var repository = new JsonCelestialRepository();

        var objects = await repository.GetAllAsync();

        objects.Should().HaveCount(110);
        objects.Select(o => o.Id).Should().OnlyHaveUniqueItems();
        objects.Should().Contain(o => o.Id == "M1");
        objects.Should().Contain(o => o.Id == "M110");
    }

    [Fact]
    public async Task GetAllAsync_ParsesTypeAndConstellation()
    {
        var repository = new JsonCelestialRepository();

        var objects = await repository.GetAllAsync();

        var m42 = objects.Single(o => o.Id == "M42");
        m42.Constellation.Should().Be("Orion");
        m42.Type.Should().Be(Core.Models.CelestialObjectType.DiffuseNebula);
        m42.Coordinates.RightAscensionHours.Should().BeApproximately(5.59, 1e-6);

        // M40 and M73 are odd cases catalogued as Unknown.
        objects.Single(o => o.Id == "M40").Type.Should().Be(Core.Models.CelestialObjectType.Unknown);
        objects.Single(o => o.Id == "M73").Type.Should().Be(Core.Models.CelestialObjectType.Unknown);
    }

    [Fact]
    public async Task GetAllAsync_CoordinatesAreValid()
    {
        var repository = new JsonCelestialRepository();

        var objects = await repository.GetAllAsync();

        foreach (var o in objects)
        {
            o.Coordinates.RightAscensionHours.Should().BeInRange(0, 24);
            o.Coordinates.DeclinationDegrees.Should().BeInRange(-90, 90);
        }
    }

    [Fact]
    public async Task GetAllAsync_MissingFile_Throws()
    {
        var repository = new JsonCelestialRepository("does/not/exist/messier_objects.json");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => repository.GetAllAsync());
        ex.Should().BeAssignableTo<IOException>();
    }
}
