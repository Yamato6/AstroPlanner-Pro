namespace AstroPlanner.Core.Interfaces;

/// <summary>A cached response and when it was stored.</summary>
public sealed record CachedResponse(byte[] Content, DateTime StoredAtUtc);

/// <summary>
/// Disk cache of online responses (spec §9: every service is cached and degradable). Entries are
/// grouped by service and keyed by the request (the URL, already free of exact coordinates). The
/// freshness policy belongs to the caller: an old entry is still returned, so that a service can
/// fall back to it when offline.
/// </summary>
public interface IOnlineCache
{
    /// <summary>The stored response, whatever its age; null if there is none or it is unreadable.</summary>
    CachedResponse? Read(string service, string key);

    /// <summary>Stores (or replaces) a response. Never throws for IO errors: caching is best effort.</summary>
    void Write(string service, string key, byte[] content, DateTime storedAtUtc);

    /// <summary>Bytes used by the cache.</summary>
    long SizeBytes { get; }

    /// <summary>Deletes every entry.</summary>
    void Clear();
}

/// <summary>A cache with a size limit that can change while it is used (the survey tiles).</summary>
public interface ISizedCache : IOnlineCache
{
    /// <summary>Size above which the least recently used entries are removed.</summary>
    long LimitBytes { get; set; }
}
