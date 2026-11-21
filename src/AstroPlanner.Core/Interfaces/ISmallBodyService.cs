using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>Orbital elements of comets and bright asteroids (Minor Planet Center; spec §9.8).</summary>
public interface ISmallBodyService
{
    /// <summary>Every comet with current elements.</summary>
    /// <exception cref="OnlineServiceException">Not reachable and nothing cached.</exception>
    Task<SmallBodyCatalog> GetCometsAsync(CancellationToken cancellationToken = default);

    /// <summary>The asteroids that get brighter than about mag 11.5 this year.</summary>
    /// <exception cref="OnlineServiceException">Not reachable and nothing cached.</exception>
    Task<SmallBodyCatalog> GetBrightAsteroidsAsync(CancellationToken cancellationToken = default);
}
