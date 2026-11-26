using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using AstroPlanner.Core.Interfaces;

namespace AstroPlanner.Data.Cache;

/// <summary>
/// <see cref="IOnlineCache"/> on disk: <c>{root}/{service}/{sha256(key)}.bin</c>, each file an
/// 8-byte UTC timestamp followed by the response. Writes go through a temporary file and a move, so
/// a crash never leaves half an entry. When the cache grows past <see cref="LimitBytes"/> the least
/// recently used entries are deleted, down to <see cref="TrimTarget"/> of the limit. Its size is
/// counted once and then kept up to date, so a write does not list the folder (the survey tiles
/// are tens of thousands of files). Every IO error is swallowed: the cache is only an optimisation
/// and an offline fallback.
/// </summary>
public sealed class DiskCache(string root, long limitBytes = DiskCache.DefaultLimitBytes) : ISizedCache
{
    /// <summary>A trim leaves the cache at this share of the limit, so that it does not run at every write.</summary>
    public const double TrimTarget = 0.9;

    public const long DefaultLimitBytes = 200L * 1024 * 1024;

    private readonly Lock _gate = new();

    public string Root { get; } = root ?? throw new ArgumentNullException(nameof(root));

    private long? _sizeBytes;
    private long _limitBytes = limitBytes;

    /// <summary>Size above which the least recently used entries are removed (a lower limit trims at once).</summary>
    public long LimitBytes
    {
        get => _limitBytes;
        set
        {
            lock (_gate)
            {
                _limitBytes = value;
                if (Size() > _limitBytes)
                {
                    Trim();
                }
            }
        }
    }

    public CachedResponse? Read(string service, string key)
    {
        var path = PathOf(service, key);
        try
        {
            lock (_gate)
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(path);
                if (bytes.Length < sizeof(long))
                {
                    return null;
                }

                var ticks = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                {
                    return null;
                }

                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                return new CachedResponse(bytes[sizeof(long)..], new DateTime(ticks, DateTimeKind.Utc));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Write(string service, string key, byte[] content, DateTime storedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = PathOf(service, key);
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var size = Size();
                var replaced = File.Exists(path) ? new FileInfo(path).Length : 0;
                var temporary = path + ".tmp";
                using (var file = File.Create(temporary))
                {
                    Span<byte> header = stackalloc byte[sizeof(long)];
                    BinaryPrimitives.WriteInt64LittleEndian(header, storedAtUtc.ToUniversalTime().Ticks);
                    file.Write(header);
                    file.Write(content);
                }

                File.Move(temporary, path, overwrite: true);
                File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
                _sizeBytes = size - replaced + sizeof(long) + content.Length;
                if (_sizeBytes > _limitBytes)
                {
                    Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    public long SizeBytes
    {
        get
        {
            lock (_gate)
            {
                return Size();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var file in Entries())
            {
                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // In use or read-only: left for next time.
                }
            }

            _sizeBytes = null;
        }
    }

    /// <summary>The bytes on disk: counted the first time, then kept up to date (call under the lock).</summary>
    private long Size() => _sizeBytes ??= Entries().Sum(f => f.Length);

    private void Trim()
    {
        var files = Entries().ToList();
        var total = files.Sum(f => f.Length);
        var target = (long)(_limitBytes * TrimTarget);
        foreach (var file in files.OrderBy(f => f.LastAccessTimeUtc))
        {
            if (total <= target)
            {
                break;
            }

            var length = file.Length;
            try
            {
                file.Delete();
                total -= length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use: it stays.
            }
        }

        _sizeBytes = total;
    }

    private IEnumerable<FileInfo> Entries()
    {
        var directory = new DirectoryInfo(Root);
        return directory.Exists ? directory.EnumerateFiles("*.bin", SearchOption.AllDirectories) : [];
    }

    private string PathOf(string service, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentNullException.ThrowIfNull(key);
        var folder = new string([.. service.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? char.ToLowerInvariant(c) : '-')]);
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Root, folder, name + ".bin");
    }
}
