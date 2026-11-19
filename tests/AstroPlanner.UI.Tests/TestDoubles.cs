using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

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
            throw new IOException("disco lleno");
        }

        Entries = entries.ToList();
        SaveCount++;
    }
}

internal sealed class NullLogger : IAppLogger
{
    public string LogLocation => "";

    public void LogInfo(string message) { }

    public void LogError(string message, Exception? exception = null) { }
}
