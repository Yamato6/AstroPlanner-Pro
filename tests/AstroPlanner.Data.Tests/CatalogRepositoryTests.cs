using AstroPlanner.Core.Models;
using AstroPlanner.Data.Catalog;
using AstroPlanner.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Tests;

/// <summary>The embedded catalogs.sqlite, loaded once for the whole class.</summary>
public sealed class CatalogFixture : IDisposable
{
    public CatalogFixture()
    {
        Repository = SqliteCatalogRepository.FromEmbedded(Directory);
        Objects = Repository.GetAllAsync().GetAwaiter().GetResult();
    }

    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));
    public SqliteCatalogRepository Repository { get; }
    public IReadOnlyList<CelestialObject> Objects { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (System.IO.Directory.Exists(Directory))
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}

public sealed class SqliteCatalogRepositoryTests(CatalogFixture fixture) : IClassFixture<CatalogFixture>
{
    private readonly IReadOnlyList<CelestialObject> _objects = fixture.Objects;

    private CelestialObject Object(string id) => _objects.Single(o => o.Id == id);

    [Fact]
    public void Catalog_HasTheWholeNgcIcCatalog()
    {
        _objects.Should().HaveCountGreaterThan(13_000);
        _objects.Select(o => o.Id).Should().OnlyHaveUniqueItems();
        _objects.Should().OnlyContain(o => !o.IsPlanet);
    }

    [Fact]
    public void Stars_ToTheNakedEyeLimit_AreCatalogRows()
    {
        var stars = _objects.Where(o => o.IsStar).ToList();

        stars.Should().HaveCountGreaterThan(8_500).And.OnlyContain(s => s.ApparentMagnitude <= SqliteCatalogRepository.StarCatalogLimitMagnitude);
        stars.Count(s => s.Type == CelestialObjectType.DoubleStar).Should().BeGreaterThan(1_500);
        _objects.Should().Contain(o => o.Id == "M40" && !o.IsStar, "OpenNGC's star entries stay as they were");

        var sirius = Object("HIP 32349");
        sirius.CommonName.Should().Be("Sirius");
        sirius.Constellation.Should().Be("Canis Major");
        sirius.Designations.Should().Contain(["HD 48915", "HR 2491", "Alp CMa", "9 CMa", "WDS 06451-1643"]);
        sirius.AlternateNames.Should().Contain(["α CMa", "Alpha Canis Majoris", "9 Canis Majoris"]);
        sirius.Star!.SpectralType.Should().StartWith("A0");
        sirius.Star.DistanceLightYears.Should().BeApproximately(8.6, 0.1);
        sirius.Star.MainPair!.Name.Should().Be("AGC 1 AB");
    }

    [Fact]
    public void Albireo_IsADouble_ThatKnowsItsMainPair()
    {
        var albireo = Object("HIP 95947");

        albireo.CommonName.Should().Be("Albireo");
        albireo.Type.Should().Be(CelestialObjectType.DoubleStar);
        albireo.AlternateNames.Should().Contain(["β¹ Cyg", "Beta1 Cygni", "6 Cygni"]);
        albireo.Star!.Pairs.Should().BeInAscendingOrder(p => p.SeparationArcsec);
        albireo.Star.MainPair.Should().BeEquivalentTo(new
        {
            Discoverer = "STFA 43", Components = "AB", SeparationArcsec = 34.3, PrimaryMagnitude = 3.19, CompanionMagnitude = 4.68,
            PositionAngleDegrees = 54.0
        }, o => o.ExcludingMissingMembers());
        albireo.Star.Pairs.Should().Contain(p => p.IsClose, "Aa,Ab is a speckle pair inside A");

        var unnamed = _objects.First(o => o.IsStar && o.CommonName.Length == 0);
        unnamed.DisplayName.Should().Be(unnamed.Id);
    }

    [Fact]
    public void Catalog_HasAll110MessierAnd109CaldwellObjects()
    {
        _objects.Select(o => o.MessierNumber).OfType<int>().Distinct().Order()
            .Should().Equal(Enumerable.Range(1, 110));
        _objects.Select(o => o.CaldwellNumber).OfType<int>().Distinct().Order()
            .Should().Equal(Enumerable.Range(1, 109));
    }

    [Fact]
    public void MessierObjects_KeepTheirV1IdsAndEnglishNames()
    {
        var m42 = Object("M42");

        m42.CommonName.Should().Be("Orion Nebula");
        m42.Constellation.Should().Be("Orion");
        m42.Designations.Should().Contain("NGC 1976");
        m42.Coordinates.RightAscensionHours.Should().BeApproximately(5.588, 0.01);
        Object("M31").Designations.Should().Contain("NGC 224");
        Object("M102").Designations.Should().Contain("NGC 5866");
        Object("M40").Type.Should().Be(CelestialObjectType.DoubleStar);
    }

    [Fact]
    public void Objects_HaveSizesSurfaceBrightnessAndAlternateNames()
    {
        var m31 = Object("M31");

        m31.MajorAxisArcmin.Should().BeApproximately(177.8, 0.1);
        m31.MinorAxisArcmin.Should().BeApproximately(69.7, 0.1);
        m31.PositionAngleDegrees.Should().Be(35);
        m31.SurfaceBrightness.Should().BeApproximately(23.6, 0.1);
        Object("M42").AlternateNames.Should().Contain("Great Orion Nebula");
    }

    [Fact]
    public void CaldwellOnlyObjects_UseTheirCaldwellId()
    {
        Object("C14").CommonName.Should().Be("Double Cluster");
        Object("NGC 7000").CaldwellNumber.Should().Be(20);
        Object("NGC 7000").IsInCatalog("NGC").Should().BeTrue();
        Object("NGC 7000").IsInCatalog("IC").Should().BeFalse();
    }

    [Fact]
    public void Constellations_AreFullIauNames()
    {
        var names = ConstellationNames();

        _objects.Select(o => o.Constellation).Distinct().Should().OnlyContain(c => names.Contains(c));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Catalog_LoadsWithinTheStartupBudget()
    {
        // Part of the 2 s budget to the first frame (spec §5): extraction of a fresh copy included.
        var directory = Path.Combine(fixture.Directory, "fresh");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var objects = await SqliteCatalogRepository.FromEmbedded(directory).GetAllAsync();

        watch.Stop();
        objects.Should().HaveCountGreaterThan(22_000);
        watch.Elapsed.Should().BeLessThan(PerformanceBudget.Of(TimeSpan.FromSeconds(1.5)));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsTheSameListEveryTime()
    {
        var again = await fixture.Repository.GetAllAsync();

        again.Should().BeSameAs(_objects);
    }

    [Fact]
    public async Task Extract_ReusesTheCopyAndRemovesOlderVersions()
    {
        var stale = Path.Combine(fixture.Directory, "catalogs-0000000000000000.sqlite");
        await File.WriteAllTextAsync(stale, "old");

        var first = await SqliteCatalogRepository.ExtractAsync(fixture.Directory, CancellationToken.None);
        var writeTime = File.GetLastWriteTimeUtc(first);
        var second = await SqliteCatalogRepository.ExtractAsync(fixture.Directory, CancellationToken.None);

        second.Should().Be(first);
        File.GetLastWriteTimeUtc(second).Should().Be(writeTime);
        File.Exists(stale).Should().BeFalse();
    }

    [Fact]
    public async Task Metadata_RecordsTheSourceVersions()
    {
        var path = await SqliteCatalogRepository.ExtractAsync(fixture.Directory, CancellationToken.None);

        var metadata = SqliteCatalogRepository.ReadMetadata(path);

        metadata["schema_version"].Should().Be(CatalogSchema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        metadata["openngc_version"].Should().Be("v20260501");
        metadata["hyg_version"].Should().Be("v4.4");
    }

    [Fact]
    public async Task WithPlanets_AppendsTheSevenPlanets_AndTheMilkyWayCore()
    {
        var repository = new CatalogWithPlanetsRepository(fixture.Repository);

        var objects = await repository.GetAllAsync();

        objects.Should().HaveCount(_objects.Count + 8);
        objects.Where(o => o.IsPlanet).Select(o => o.CommonName)
            .Should().Equal("Mercury", "Venus", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune");
        objects[^1].Type.Should().Be(CelestialObjectType.MilkyWayCore);
        objects[^1].Constellation.Should().Be("Sagittarius");
    }

    private static HashSet<string> ConstellationNames() =>
        [.. Core.Algorithms.ConstellationLookup.All.Select(c => c.Name)];
}

public sealed class CityRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsMexicanCitiesWithValidCoordinates()
    {
        var cities = await new JsonCityRepository().GetAllAsync();

        cities.Should().HaveCount(11);
        cities.Should().OnlyContain(city => city.Country == "Mexico");
        cities.Should().OnlyContain(city => city.LatitudeDegrees >= -90 && city.LatitudeDegrees <= 90);
        cities.Should().OnlyContain(city => city.LongitudeDegrees >= -180 && city.LongitudeDegrees <= 180);
        cities.Should().OnlyContain(city => city.ElevationMeters >= 0);
    }

    [Fact]
    public async Task GetAllAsync_HasConsistentWindowsAndIanaTimeZones()
    {
        var cities = await new JsonCityRepository().GetAllAsync();

        foreach (var city in cities)
        {
            var windows = TimeZoneInfo.FindSystemTimeZoneById(city.TimeZoneId);
            var iana = TimeZoneInfo.FindSystemTimeZoneById(city.IanaTimeZoneId);
            var instant = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

            iana.GetUtcOffset(instant).Should().Be(windows.GetUtcOffset(instant), city.Id);
        }

        cities.Single(city => city.Id == "mexico-city").IanaTimeZoneId.Should().Be("America/Mexico_City");
        cities.Single(city => city.Id == "cancun").TimeZoneId.Should().Be("Eastern Standard Time (Mexico)");
        cities.Single(city => city.Id == "hermosillo").IanaTimeZoneId.Should().Be("America/Hermosillo");
    }

    [Fact]
    public async Task GetAllAsync_MissingFile_ThrowsIOException()
    {
        var repository = new JsonCityRepository("does not exist/cities.json");

        await Assert.ThrowsAnyAsync<IOException>(() => repository.GetAllAsync());
    }
}
