namespace AstroPlanner.Core.Models;

/// <summary>An online service could not answer (offline, service error, invalid response). The message is short and shown to the user.</summary>
public class OnlineServiceException(string message, Exception? innerException = null) : Exception(message, innerException);

/// <summary>A survey tile as fetched (JPEG bytes) and the host that served it, for the attribution.</summary>
public sealed record HipsTile(byte[] Content, string Source);

/// <summary>A Wikipedia article about an object (spec §4.4): its lead extract and, when free, its image.</summary>
/// <param name="Title">The article's title (after redirects), e.g. "Orion Nebula".</param>
/// <param name="Description">Wikidata's short description, e.g. "Diffuse nebula in the constellation Orion".</param>
public sealed record ObjectSummary(string Title, string Extract, string? Description, Uri ArticleUrl)
{
    /// <summary>The article's image when it is on Wikimedia Commons with a license; null otherwise.</summary>
    public ObjectImage? Image { get; init; }
}

/// <summary>A Commons image with what its license requires to be shown.</summary>
/// <param name="FileName">"Orion Nebula - Hubble 2006 mosaic 18000.jpg".</param>
/// <param name="License">Short license name, e.g. "CC BY-SA 4.0" or "Public domain".</param>
/// <param name="Artist">Author as plain text (HTML removed), or null.</param>
public sealed record ObjectImage(Uri ThumbnailUrl, string FileName, Uri FilePageUrl, string License, Uri? LicenseUrl, string? Artist);

/// <summary>A name resolved by an online service (CDS Sesame).</summary>
/// <param name="Name">Main identifier as the service gives it, e.g. "NAME Horsehead Nebula".</param>
/// <param name="ObjectType">SIMBAD object type code, e.g. "DNe"; null when unknown.</param>
/// <param name="Coordinates">J2000 position.</param>
/// <param name="Source">Who resolved it, for the attribution, e.g. "Sesame (CDS) · SIMBAD".</param>
public sealed record ResolvedName(string Query, string Name, string? ObjectType, EquatorialCoordinates Coordinates, string Source);
