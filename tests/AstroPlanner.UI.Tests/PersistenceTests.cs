using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.Tests;

/// <summary>JSON persistence of settings, plan and log, in a temporary folder.</summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public PersistenceTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string fileName) => Path.Combine(_directory, fileName);

    [Fact]
    public void SettingsService_MissingFile_ReturnsDefaults()
    {
        var settings = new JsonSettingsService(PathFor("settings.json")).Load();

        settings.CityId.Should().Be("mexico-city");
        settings.MinimumAltitude.Should().Be(20);
    }

    [Fact]
    public void SettingsService_CorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(PathFor("settings.json"), "{ this is not json");

        var settings = new JsonSettingsService(PathFor("settings.json")).Load();

        settings.CityId.Should().Be("mexico-city");
    }

    [Fact]
    public void SettingsService_SaveThenLoad_RoundTrips()
    {
        var service = new JsonSettingsService(PathFor("sub/settings.json"));
        var saved = new ObserverSettings
        {
            CityId = null,
            IsManualLocation = true,
            LatitudeDegrees = 31.8667,
            LongitudeDegrees = -116.5964,
            ElevationMeters = 2800,
            TimeZoneId = "Pacific Standard Time (Mexico)",
            MinimumAltitude = 35
        };

        service.Save(saved);
        var loaded = service.Load();

        loaded.Should().BeEquivalentTo(saved);
        File.Exists(PathFor("sub/settings.json.tmp")).Should().BeFalse("the atomic write renames the temporary file");
    }

    [Fact]
    public void PlanService_MissingOrCorruptFile_ReturnsEmptyPlan()
    {
        var service = new JsonObservationPlanService(PathFor("plan.json"));
        service.Load().Should().BeEmpty();

        File.WriteAllText(PathFor("plan.json"), "[ \"M31\", ");
        service.Load().Should().BeEmpty();
    }

    [Fact]
    public void PlanService_SaveThenLoad_KeepsIdsInOrder()
    {
        var service = new JsonObservationPlanService(PathFor("plan.json"));

        service.Save([new("M42", "UHC filter"), new("M31"), new("M13", "with \"quotes\"")]);

        service.Load().Should().Equal(
            new ObservationPlanEntry("M42", "UHC filter"),
            new ObservationPlanEntry("M31", ""),
            new ObservationPlanEntry("M13", "with \"quotes\""));
    }

    [Fact]
    public void PlanService_ReadsLegacyListOfIds()
    {
        // Format of versions 0.3-0.5: ids only, no notes.
        File.WriteAllText(PathFor("plan.json"), "[ \"M31\", \"M42\" ]");

        var entries = new JsonObservationPlanService(PathFor("plan.json")).Load();

        entries.Should().Equal(new ObservationPlanEntry("M31", ""), new ObservationPlanEntry("M42", ""));
    }

    [Fact]
    public void FileLogger_WritesErrorWithExceptionDetails()
    {
        var logger = new FileAppLogger(PathFor("logs"));

        logger.LogError("Test failure", new InvalidOperationException("internal detail"));

        var text = File.ReadAllText(logger.CurrentFilePath);
        text.Should().Contain("[ERROR] Test failure")
            .And.Contain("InvalidOperationException")
            .And.Contain("internal detail");
    }
}
