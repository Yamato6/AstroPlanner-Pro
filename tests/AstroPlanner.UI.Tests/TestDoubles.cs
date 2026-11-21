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

    public List<string> Checklist { get; set; } = [];

    public IReadOnlyList<string> LoadChecklist() => Checklist;

    public void SaveChecklist(IEnumerable<string> doneKeys) => Checklist = doneKeys.ToList();
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
        ISkyViewStore? skyStore = null,
        IEquipmentRepository? equipment = null,
        ILightPollutionAtlas? atlas = null,
        IAstroForecastService? astro = null,
        ISurveyTileSource? tiles = null,
        IObjectInfoService? info = null,
        INameResolver? resolver = null,
        IImageDecoder? decoder = null,
        ISmallBodyService? smallBodies = null,
        ISatelliteService? satellites = null)
    {
        var logger = new NullLogger();
        var astronomy = new AstronomyService();
        settings ??= new FakeSettingsService(new ObserverSettings());
        logbook ??= new LogbookViewModel(new FakeLogService(), logger);
        var context = new ObserverContext(astronomy, time, atlas);
        // No equipment unless a test asks for it: the existing tests keep their manual camera frame.
        var equipmentState = new EquipmentState(equipment ?? new FakeEquipmentRepository(EquipmentInventory.Empty), logger);
        var weatherState = new WeatherState(context, logger, time, weather, astro);
        var theme = new ThemeState();
        var settingsViewModel = new SettingsViewModel(settings, new FakeCityRepository(), logger, theme: theme);
        var targets = new TargetsViewModel(
            new FakeCelestialRepository(catalog ?? [TestCatalog.M42, TestCatalog.M31, TestCatalog.M13]),
            astronomy, context, settings, logbook, logger, equipmentState, weatherState);
        var online = new OnlineInfoState();
        var satelliteState = new SatelliteState(context, online, logger, time, satellites);
        var planViewModel = new SessionPlanViewModel(plan ?? new FakePlanService(), astronomy, context, weatherState, targets, logger,
            equipmentState, time, satelliteState);
        var framing = new FramingViewModel(tiles, decoder ?? new WpfImageDecoder(), equipmentState, online, logger, time);
        var about = new ObjectInfoViewModel(info, online, logger, time);
        var detail = new ObjectDetailViewModel(astronomy, context, weatherState, targets, planViewModel, logbook, framing, about, equipmentState);
        var sky = new SkyViewModel(context, targets, planViewModel, skyStore ?? new FakeSkyViewStore(), logger, time, equipmentState,
            resolver, online, satelliteState, theme);
        var tonight = new TonightViewModel(context, weatherState, logbook, targets, equipmentState, planViewModel, astronomy, satelliteState);
        var locationsViewModel = new LocationsViewModel(locations ?? new FakeLocationRepository(), settingsViewModel, context, logger, atlas);
        return new ShellViewModel(
            context, weatherState, tonight, targets, sky, detail, planViewModel, logbook, locationsViewModel, settingsViewModel,
            logger, time, updates, new EquipmentViewModel(equipmentState), online,
            new SmallBodyState(context, online, logger, time, smallBodies), satelliteState);
    }
}

/// <summary>Equipment kept in memory.</summary>
internal sealed class FakeEquipmentRepository(EquipmentInventory inventory) : IEquipmentRepository
{
    public EquipmentInventory Inventory { get; private set; } = inventory;
    public int SaveCount { get; private set; }

    public EquipmentInventory Load() => Inventory;

    public void Save(EquipmentInventory inventory)
    {
        Inventory = inventory;
        SaveCount++;
    }
}

/// <summary>A light-pollution atlas that returns the same artificial brightness everywhere (or nothing).</summary>
internal sealed class FakeAtlas(double? artificialBrightness) : ILightPollutionAtlas
{
    public double? ArtificialBrightness(double latitudeDegrees, double longitudeDegrees) => artificialBrightness;
}