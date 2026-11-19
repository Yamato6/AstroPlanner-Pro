using System.Security.Cryptography;
using AstroPlanner.Core.Models;
using AstroPlanner.Data.Import;
using AstroPlanner.Data.Storage;

namespace AstroPlanner.Data.Tests;

public sealed class V1ImporterTests : TempFolderTest
{
    private static readonly IReadOnlyList<CelestialObject> Catalog =
    [
        new("M42", "Orion Nebula", "Orion", CelestialObjectType.ClusterWithNebula, 4, new EquatorialCoordinates(5.59, -5.39)),
        new("M31", "Andromeda Galaxy", "Andromeda", CelestialObjectType.Galaxy, 3.4, new EquatorialCoordinates(0.71, 41.27)),
        .. SolarSystemCatalog.Planets
    ];

    private static string FixtureFolder(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "v1", name);

    [Fact]
    public void Read_CurrentFormat_MapsIdsAndNames()
    {
        var data = V1Importer.Read(FixtureFolder("current"), Catalog);

        data.Settings!.LatitudeDegrees.Should().Be(21.8853);
        data.Settings.MinimumAltitude.Should().Be(25);
        data.Settings.WeatherForecastEnabled.Should().BeFalse();
        data.Plan.Should().Equal(
            new ObservationPlanEntry("M42", "Probar 16 mm desde la azotea"), new("Jupiter"), new("M31", "Campo amplio"));
        data.Logbook.Select(e => (e.ObjectId, e.ObjectName))
            .Should().Equal(("M42", "Orion Nebula"), ("Saturn", "Saturn"), ("X99", "Objeto desconocido"));
        data.Logbook[0].Should().BeEquivalentTo(new
        {
            Id = Guid.Parse("6f1c2a9e-1d2b-4c8e-9a51-3b7e2f0d4a11"),
            ObservedAtLocal = new DateTime(2026, 1, 15, 22, 30, 0),
            Equipment = "Canon T7 + Rokinon 16mm",
            Seeing = 3, Transparency = 4, Rating = 5, Notes = "Primera foto"
        });
        data.Warnings.Should().ContainSingle(w => w.Contains("X99", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_LegacyFormats_AreUnderstood()
    {
        var data = V1Importer.Read(FixtureFolder("legacy"), Catalog);

        data.Settings!.TimeZoneId.Should().Be("Romance Standard Time");
        data.Settings.MaximumMagnitude.Should().Be(12, "missing fields take the defaults");
        data.Plan.Select(p => p.Id).Should().Equal("M31", "Mars");
        data.Logbook.Should().BeEmpty();
    }

    [Fact]
    public void Read_CorruptFiles_AreSkippedWithWarnings()
    {
        var data = V1Importer.Read(FixtureFolder("corrupt"), Catalog);

        data.Settings.Should().BeNull();
        data.Logbook.Should().BeEmpty();
        data.Plan.Select(p => p.Id).Should().Equal("M13");
        data.Warnings.Should().Contain(w => w.StartsWith("settings.json", StringComparison.Ordinal))
            .And.Contain(w => w.StartsWith("logbook.json", StringComparison.Ordinal))
            .And.Contain(w => w.Contains("M13", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_NeverModifiesTheV1Files()
    {
        var folder = FixtureFolder("current");
        var before = Hashes(folder);

        _ = V1Importer.Read(folder, Catalog);

        Hashes(folder).Should().Equal(before);
    }

    [Fact]
    public void HasData_DetectsAV1Folder()
    {
        V1Importer.HasData(FixtureFolder("current")).Should().BeTrue();
        V1Importer.HasData(Folder).Should().BeFalse();
        V1Importer.DefaultDirectory(@"C:\Users\x\AppData\Local").Should().Be(@"C:\Users\x\AppData\Local\AstroPlanner");
    }

    [Fact]
    public void Apply_ImportsIntoTheStores_AndAddsOnlyWhatIsMissing()
    {
        var database = new AppDatabase(DatabasePath);
        var settings = new SqliteSettingsService(database);
        var plan = new SqlitePlanService(database);
        var logbook = new SqliteLogbookService(database);
        var locations = new SqliteLocationRepository(database);
        plan.Save([new ObservationPlanEntry("M31", "already here")]);
        var data = V1Importer.Read(FixtureFolder("current"), Catalog);

        var result = V1Importer.Apply(data, settings, plan, logbook, locations);
        var again = V1Importer.Apply(data, settings, plan, logbook, locations);

        result.SettingsImported.Should().BeTrue();
        result.PlanItemsAdded.Should().Be(2);
        result.LogEntriesAdded.Should().Be(3);
        again.PlanItemsAdded.Should().Be(0);
        again.LogEntriesAdded.Should().Be(0);

        plan.Load().Select(p => p.Id).Should().Equal("M31", "M42", "Jupiter");
        plan.Load()[0].Note.Should().Be("already here");
        logbook.Load().Should().HaveCount(3);

        var location = locations.GetAll().Should().ContainSingle().Subject;
        location.Name.Should().Be(V1Importer.ImportedLocationName);
        location.IanaTimeZoneId.Should().Be("America/Mexico_City");
        location.WindowsTimeZoneId.Should().Be("Central Standard Time (Mexico)");
        location.ElevationMeters.Should().Be(1888);
        settings.Load().ActiveLocationId.Should().Be(location.Id);
        settings.Load().MinimumAltitude.Should().Be(25);
    }

    [Theory]
    [InlineData("Marte", "Mars")]
    [InlineData("Júpiter", "Jupiter")]
    [InlineData("Neptuno", "Neptune")]
    [InlineData("M42", "M42")]
    [InlineData(" Saturno ", "Saturn")]
    public void MapObjectId_TranslatesPlanetIds(string v1, string expected)
    {
        V1Importer.MapObjectId(v1).Should().Be(expected);
    }

    private static List<string> Hashes(string folder) =>
        [.. Directory.EnumerateFiles(folder).Order().Select(f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + File.GetLastWriteTimeUtc(f).Ticks)];
}
