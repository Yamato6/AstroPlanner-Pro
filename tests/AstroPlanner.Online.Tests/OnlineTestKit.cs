using AstroPlanner.Core.Interfaces;

namespace AstroPlanner.Online.Tests;

/// <summary>The recorded answers in tests/fixtures/online (no network in tests).</summary>
internal static class OnlineFixtures
{
    private static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", "online", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));
}

/// <summary>A cache kept in memory.</summary>
internal sealed class MemoryCache : IOnlineCache
{
    public Dictionary<(string, string), CachedResponse> Entries { get; } = [];

    public CachedResponse? Read(string service, string key) => Entries.GetValueOrDefault((service, key));

    public void Write(string service, string key, byte[] content, DateTime storedAtUtc) =>
        Entries[(service, key)] = new CachedResponse(content, storedAtUtc);

    public long SizeBytes => Entries.Values.Sum(e => (long)e.Content.Length);

    public void Clear() => Entries.Clear();
}
