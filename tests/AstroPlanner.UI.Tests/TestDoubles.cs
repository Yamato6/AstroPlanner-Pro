using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Services;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Tests;

internal sealed class FakeCelestialRepository(IReadOnlyList<CelestialObject> objects) : ICelestialRepository
{
    public Task<IReadOnlyList<CelestialObject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(objects);
}

internal sealed class FakeCityRepository : ICityRepository
{
    public Task<IReadOnlyList<City>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<City>>([]);
}

internal sealed class FakeSettingsService(ObserverSettings saved) : ISettingsService
{
    private ObserverSettings _saved = saved;

    public int SaveCount { get; private set; }

    public ObserverSettings Load() => _saved;

    public void Save(ObserverSettings settings)
    {
        _saved = settings;
        SaveCount++;
    }
}

internal sealed class FakePlanService : IObservationPlanService
{
    public List<ObservationPlanEntry> Entries { get; set; } = [];

    public IReadOnlyList<string> Ids => Entries.Select(entry => entry.Id).ToList();

    public IReadOnlyList<ObservationPlanEntry> Load() => Entries;

    public void Save(IEnumerable<ObservationPlanEntry> entries) => Entries = entries.ToList();
}

internal sealed class FakeLogService : IObservationLogService
{
    public List<ObservationLogEntry> Entries { get; set; } = [];

    public int SaveCount { get; private set; }

    public bool FailOnSave { get; set; }

    public IReadOnlyList<ObservationLogEntry> Load() => Entries;

    public void Save(IEnumerable<ObservationLogEntry> entries)
    {
        if (FailOnSave)
        {
            throw new IOException("disk full");
        }

        Entries = entries.ToList();
        SaveCount++;
    }
}

internal sealed class FakeLocationRepository : ILocationRepository
{
    public List<Location> Locations { get; } = [];

    public IReadOnlyList<Location> GetAll() => [.. Locations];

    public void Save(Location location)
    {
        var index = Locations.FindIndex(l => l.Id == location.Id);
        if (index >= 0)
        {
            Locations[index] = location;
        }
        else
        {
            Locations.Add(location);
        }
    }

    public void Delete(string id) => Locations.RemoveAll(l => l.Id == id);
}

internal sealed class FakeSkyViewStore : ISkyViewStore
{
    public SkyViewSettings Saved { get; private set; } = new();

    public int SaveCount { get; private set; }

    public SkyViewSettings Load() => Saved.Clone();

    public void Save(SkyViewSettings settings)
    {
        Saved = settings.Clone();
        SaveCount++;
    }
}

internal sealed class NullLogger : IAppLogger
{
    public string LogLocation => "";

    public void LogInfo(string message) { }

    public void LogError(string message, Exception? exception = null) { }
}

/// <summary>
/// Builds the whole view-model graph the way the app's DI container does, with in-memory
/// doubles for catalog, settings, plan, logbook and online services.
/// </summary>
internal static class TestShell
{
    public static ShellViewModel Create(
        IReadOnlyList<CelestialObject>? catalog = null,
        FakePlanService? plan = null,
        ISettingsService? settings = null,
        LogbookViewModel? logbook = null,
        TimeProvider? time = null,
        IWeatherForecastService? weather = null,
        IUpdateChecker? updates = null,
        ILocationRepository? locations = null,
        ISkyViewStore? skyStore = null)
    {
        var logger = new NullLogger();
        var astronomy = new AstronomyService();
        settings ??= new FakeSettingsService(new ObserverSettings());
        logbook ??= new LogbookViewModel(new FakeLogService(), logger);
        var context = new ObserverContext(astronomy, time);
        var weatherState = new WeatherState(context, logger, time, weather);
        var settingsViewModel = new SettingsViewModel(settings, new FakeCityRepository(), logger);
        var targets = new TargetsViewModel(
            new FakeCelestialRepository(catalog ?? [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13]),
            astronomy, context, settings, logbook, logger);
        var planViewModel = new SessionPlanViewModel(plan ?? new FakePlanService(), astronomy, context, weatherState, targets, logger);
        var detail = new ObjectDetailViewModel(astronomy, context, weatherState, targets, planViewModel, logbook);
        var sky = new SkyViewModel(context, targets, planViewModel, skyStore ?? new FakeSkyViewStore(), logger, time);
        var tonight = new TonightViewModel(context, weatherState, logbook);
        var locationsViewModel = new LocationsViewModel(locations ?? new FakeLocationRepository(), settingsViewModel, logger);
        return new ShellViewModel(
            context, weatherState, tonight, targets, sky, detail, planViewModel, logbook, locationsViewModel, settingsViewModel,
            logger, time, updates);
    }
}
