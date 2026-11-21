using System.Globalization;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Online.Http;

namespace AstroPlanner.Online.Info;

/// <summary>
/// Resolves object names with CDS Sesame (SIMBAD, NED, VizieR; spec §4.2, §9): XML output from
/// cds.unistra.fr, or from the VizieR mirrors at CfA and IDIA when it cannot be reached. Only the
/// name typed is sent. Answers ("nothing found" included) are cached for <see cref="CacheDuration"/>.
/// </summary>
public sealed class SesameResolver(HttpClient http, IOnlineCache? cache = null, TimeProvider? timeProvider = null) : INameResolver
{
    public const string CacheName = "sesame";
    public const int MaximumNameLength = 100;
    public static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    /// <summary>The same Sesame query (<c>-oxp/SNV</c>: XML, SIMBAD then NED then VizieR) on each mirror.</summary>
    public static readonly IReadOnlyList<string> Mirrors =
    [
        "https://cds.unistra.fr/cgi-bin/nph-sesame/-oxp/SNV?",
        "https://vizier.cfa.harvard.edu/viz-bin/nph-sesame/-oxp/SNV?",
        "https://vizier.idia.ac.za/viz-bin/nph-sesame/-oxp/SNV?",
    ];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    public async Task<ResolvedName?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        var query = Normalize(name);
        if (query.Length == 0 || query.Length > MaximumNameLength)
        {
            return null;
        }

        var escaped = Uri.EscapeDataString(query);
        var fetched = await CachedFetch.GetAsync(http, cache, CacheName, query.ToUpperInvariant(), [.. Mirrors.Select(m => m + escaped)],
            CacheDuration, _time.GetUtcNow().UtcDateTime, "Sesame (CDS)", (message, inner) => new OnlineServiceException(message, inner),
            cancellationToken);
        return Parse(fetched.Content, query);
    }

    /// <summary>"  barnard   33 " → "barnard 33".</summary>
    public static string Normalize(string? name) =>
        string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The first resolver's position, or null when no resolver found the name.</summary>
    /// <exception cref="OnlineServiceException">Not a Sesame XML answer.</exception>
    public static ResolvedName? Parse(byte[] xml, string query)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(xml);
            document = XDocument.Load(stream, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new OnlineServiceException("Invalid response from Sesame (CDS).", ex);
        }

        if (document.Root?.Name.LocalName != "Sesame")
        {
            throw new OnlineServiceException("Invalid response from Sesame (CDS).");
        }

        foreach (var resolver in document.Descendants("Resolver"))
        {
            if (Number(resolver.Element("jradeg")) is not { } ra || Number(resolver.Element("jdedeg")) is not { } dec
                || ra is < 0 or >= 360 || dec is < -90 or > 90)
            {
                continue;
            }

            var name = resolver.Element("oname")?.Value.Trim() is { Length: > 0 } oname ? oname : query;
            var type = resolver.Element("otype")?.Value.Trim() is { Length: > 0 } otype ? otype : null;
            return new ResolvedName(query, name, type, new EquatorialCoordinates(ra / 15, dec), $"Sesame (CDS) · {ResolverName(resolver)}");
        }

        return null;
    }

    /// <summary>"Sca=Simbad (CfA, via client/server)" or "Sim=Simbad, all IDs (via url)" → "Simbad".</summary>
    private static string ResolverName(XElement resolver)
    {
        var name = resolver.Attribute("name")?.Value ?? "";
        var start = name.IndexOf('=', StringComparison.Ordinal) + 1;
        var end = name.IndexOfAny(['(', ','], start);
        var result = (end > start ? name[start..end] : name[start..]).Trim();
        return result.Length > 0 ? result : "SIMBAD";
    }

    private static double? Number(XElement? element) =>
        element is not null && double.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
