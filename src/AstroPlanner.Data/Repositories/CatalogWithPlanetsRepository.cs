using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Data.Repositories;

/// <summary>
/// Adds the planets (<see cref="SolarSystemCatalog"/>) and the Milky Way core
/// (<see cref="GalacticCenter"/>) to the deep-sky catalog of <paramref name="inner"/>. Comets and
/// asteroids come later, from the online elements (they are not stored).
/// </summary>
public sealed class CatalogWithPlanetsRepository(ICelestialRepository inner) : ICelestialRepository
{
    public async Task<IReadOnlyList<CelestialObject>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var deepSky = await inner.GetAllAsync(cancellationToken);
        return [.. deepSky, .. SolarSystemCatalog.Planets, GalacticCenter.Target];
    }
}
