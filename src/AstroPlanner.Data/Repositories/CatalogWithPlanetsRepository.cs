using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Data.Repositories;

/// <summary>
/// Adds the planets (<see cref="SolarSystemCatalog"/>) to the deep-sky catalog of
/// <paramref name="inner"/>.
/// </summary>
public sealed class CatalogWithPlanetsRepository(ICelestialRepository inner) : ICelestialRepository
{
    public async Task<IReadOnlyList<CelestialObject>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var deepSky = await inner.GetAllAsync(cancellationToken);
        return [.. deepSky, .. SolarSystemCatalog.Planets];
    }
}
