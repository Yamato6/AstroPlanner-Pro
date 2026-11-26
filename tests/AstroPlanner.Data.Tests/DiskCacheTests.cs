using System.Text;
using AstroPlanner.Data.Cache;

namespace AstroPlanner.Data.Tests;

/// <summary>M4: disk cache of online responses (temporary folders only).</summary>
public sealed class DiskCacheTests : IDisposable
{
    private static readonly DateTime Stored = new(2026, 11, 20, 18, 0, 0, DateTimeKind.Utc);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", "cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Entries_RoundTrip_WithTheirTimestamp()
    {
        var cache = new DiskCache(_root);

        cache.Read("open-meteo", "https://x/?a=1").Should().BeNull();
        cache.Write("open-meteo", "https://x/?a=1", "hello"u8.ToArray(), Stored);

        var entry = new DiskCache(_root).Read("open-meteo", "https://x/?a=1")!;
        Encoding.UTF8.GetString(entry.Content).Should().Be("hello");
        entry.StoredAtUtc.Should().Be(Stored);
        entry.StoredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        cache.Read("open-meteo", "https://x/?a=2").Should().BeNull();
        cache.Read("7timer", "https://x/?a=1").Should().BeNull("services do not share entries");
    }

    [Fact]
    public void Write_ReplacesTheEntry()
    {
        var cache = new DiskCache(_root);
        cache.Write("s", "k", [1, 2, 3], Stored);

        cache.Write("s", "k", [4], Stored.AddHours(1));

        cache.Read("s", "k")!.Content.Should().Equal(4);
        cache.SizeBytes.Should().Be(8 + 1);
    }

    [Fact]
    public void KeysNeverAppearInFileNames()
    {
        var cache = new DiskCache(_root);

        cache.Write("Open Meteo!", "https://api/forecast?latitude=21.88&longitude=-102.29", [1], Stored);

        var file = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Single();
        file.Should().NotContain("21.88");
        Path.GetFileName(Path.GetDirectoryName(file)).Should().Be("open-meteo-");
    }

    [Fact]
    public void OverTheLimit_TheLeastRecentlyUsedEntriesGo()
    {
        var cache = new DiskCache(_root, limitBytes: 3 * (8 + 100));
        cache.Write("s", "a", new byte[100], Stored);
        cache.Write("s", "b", new byte[100], Stored);
        cache.Write("s", "c", new byte[100], Stored);
        File.SetLastAccessTimeUtc(EntryPath("s", "a"), DateTime.UtcNow.AddHours(-2));

        cache.Write("s", "d", new byte[100], Stored);

        cache.Read("s", "a").Should().BeNull();
        cache.Read("s", "d").Should().NotBeNull();
        cache.SizeBytes.Should().BeLessThanOrEqualTo(cache.LimitBytes);
    }

    [Fact]
    public void Size_IsKeptUpToDate_AcrossWritesAndReplacements()
    {
        var cache = new DiskCache(_root);
        cache.Write("s", "a", new byte[100], Stored);
        cache.Write("s", "b", new byte[50], Stored);
        cache.Write("s", "a", new byte[10], Stored);

        cache.SizeBytes.Should().Be((8 + 50) + (8 + 10));
        new DiskCache(_root).SizeBytes.Should().Be(cache.SizeBytes, "a new instance counts the files once");
    }

    [Fact]
    public void LoweringTheLimit_TrimsAtOnce_ToBelowIt()
    {
        var cache = new DiskCache(_root);
        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            cache.Write("s", key, new byte[100], Stored);
        }

        cache.LimitBytes = 2 * (8 + 100);

        cache.SizeBytes.Should().BeLessThanOrEqualTo((long)(cache.LimitBytes * DiskCache.TrimTarget));
        cache.Read("s", "d").Should().NotBeNull();
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var cache = new DiskCache(_root);
        cache.Write("a", "1", [1], Stored);
        cache.Write("b", "2", [2], Stored);

        cache.Clear();

        cache.SizeBytes.Should().Be(0);
        cache.Read("a", "1").Should().BeNull();
        new DiskCache(Path.Combine(_root, "missing")).SizeBytes.Should().Be(0);
    }

    [Fact]
    public void DamagedEntries_AreIgnored()
    {
        var cache = new DiskCache(_root);
        cache.Write("s", "k", [1, 2], Stored);
        File.WriteAllBytes(Directory.EnumerateFiles(_root, "*.bin", SearchOption.AllDirectories).Single(), [1, 2, 3]);

        cache.Read("s", "k").Should().BeNull();
    }

    private string EntryPath(string service, string key) =>
        Path.Combine(_root, service, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".bin");
}
