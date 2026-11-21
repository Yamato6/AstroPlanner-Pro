using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Interfaces;

/// <summary>HiPS survey tiles for the framing preview (spec §4.4).</summary>
public interface ISurveyTileSource
{
    /// <summary>The surveys offered, the first being the default.</summary>
    IReadOnlyList<HipsSurvey> Surveys { get; }

    /// <summary>The tile, from the cache or from the first mirror that answers.</summary>
    /// <exception cref="OnlineServiceException">No mirror could serve it and it is not cached.</exception>
    Task<HipsTile> GetTileAsync(HipsSurvey survey, HipsTileKey key, CancellationToken cancellationToken = default);

    /// <summary>Credit line to show under images of the survey.</summary>
    string CreditFor(HipsSurvey survey);
}

/// <summary>Descriptions of objects (Wikipedia) and their images (Wikimedia Commons).</summary>
public interface IObjectInfoService
{
    /// <summary>The object's article, or null when Wikipedia has none about it (missing, disambiguation, not an astronomical object).</summary>
    /// <exception cref="OnlineServiceException">The service could not be reached and nothing is cached.</exception>
    Task<ObjectSummary?> GetSummaryAsync(CelestialObject target, CancellationToken cancellationToken = default);

    /// <summary>The bytes of an image of a summary (cached).</summary>
    /// <exception cref="OnlineServiceException">It could not be fetched.</exception>
    Task<byte[]> GetImageAsync(ObjectImage image, CancellationToken cancellationToken = default);
}

/// <summary>Resolves object names that are not in the local catalog (CDS Sesame).</summary>
public interface INameResolver
{
    /// <summary>The object's position, or null when no service knows the name.</summary>
    /// <exception cref="OnlineServiceException">No mirror could be reached and nothing is cached.</exception>
    Task<ResolvedName?> ResolveAsync(string name, CancellationToken cancellationToken = default);
}
