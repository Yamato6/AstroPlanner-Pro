using System.Diagnostics;
using AstroPlanner.Data.Storage;
using AstroPlanner.UI.Services;
using AstroPlanner.Application.ViewModels;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.UI.Tests;

public sealed class V1ImportServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static string V1Folder => Path.Combine(AppContext.BaseDirectory, "fixtures", "v1", "current");

    private V1ImportService Create(string v1Folder, out AppDatabase database)
    {
        database = new AppDatabase(Path.Combine(_folder, "app.db"));
        return new V1ImportService(
            database,
            new FakeCelestialRepository([TestCatalog.M42, TestCatalog.M31, .. SolarSystemCatalog.Planets]),
            new SqliteSettingsService(database), new SqlitePlanService(database), new SqliteLogbookService(database),
            new SqliteLocationRepository(database), v1Folder);
    }

    [Fact]
    public async Task Import_AppliesTheDataAndIsOfferedOnlyOnce()
    {
        var service = Create(V1Folder, out var database);
        service.ShouldOffer.Should().BeTrue();

        var result = await service.ImportAsync();

        result.PlanItemsAdded.Should().Be(3);
        result.LogEntriesAdded.Should().Be(3);
        new SqliteSettingsService(database).Load().MinimumAltitude.Should().Be(25);
        service.ShouldOffer.Should().BeFalse();
        V1ImportService.Describe(result).Should().Contain("3").And.Contain("X99");
    }

    [Fact]
    public void Declining_IsRemembered()
    {
        var service = Create(V1Folder, out _);

        service.MarkOffered();

        service.ShouldOffer.Should().BeFalse();
    }

    [Fact]
    public void WithoutV1Data_NothingIsOffered()
    {
        Create(Path.Combine(_folder, "no-v1-here"), out _).ShouldOffer.Should().BeFalse();
    }
}
