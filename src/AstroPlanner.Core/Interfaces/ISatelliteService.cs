using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>Element sets of the bright satellites and the space stations (CelesTrak; spec §9.9).</summary>
public interface ISatelliteService
{
    /// <summary>The satellites of the followed groups, one entry per catalog number.</summary>
    /// <exception cref="OnlineServiceException">Not reachable (or paused after an error) and nothing cached.</exception>
    Task<SatelliteCatalog> GetSatellitesAsync(CancellationToken cancellationToken = default);
}
