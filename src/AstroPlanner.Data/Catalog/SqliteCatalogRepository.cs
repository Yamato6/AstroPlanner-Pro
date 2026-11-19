using System.Globalization;
using System.Security.Cryptography;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Catalog;

/// <summary>
/// Deep-sky catalog read from <c>catalogs.sqlite</c> (OpenNGC + Caldwell + curated Messier
/// names). The database is embedded in this assembly; SQLite needs a file, so on first use it
/// is copied to <c>&lt;cacheDirectory&gt;/catalogs-&lt;hash&gt;.sqlite</c> (copies of older
/// versions are deleted) and opened read-only. The whole catalog (~13k objects) is loaded into
/// memory once: filtering and search never touch the database.
/// </summary>
public sealed class SqliteCatalogRepository : ICelestialRepository
{
    private const string ResourceName = "Catalogs/catalogs.sqlite";

    private readonly string? _databasePath;
    private readonly string? _cacheDirectory;
    private readonly Lazy<Task<IReadOnlyList<CelestialObject>>> _objects;

    private SqliteCatalogRepository(string? databasePath, string? cacheDirectory)
    {
        _databasePath = databasePath;
        _cacheDirectory = cacheDirectory;
        _objects = new(LoadOnceAsync);
    }

    /// <summary>Uses the embedded catalog, extracted into <paramref name="cacheDirectory"/>.</summary>
    public static SqliteCatalogRepository FromEmbedded(string cacheDirectory) =>
        new(null, cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory)));

    /// <summary>Uses an existing database file (tests, tools).</summary>
    public static SqliteCatalogRepository FromFile(string databasePath) =>
        new(databasePath ?? throw new ArgumentNullException(nameof(databasePath)), null);

    /// <summary>
    /// The catalog, loaded on the first call (later calls share the same list). Cancelling
    /// only stops this caller from waiting: the load itself completes for the next one.
    /// </summary>
    public Task<IReadOnlyList<CelestialObject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _objects.Value.WaitAsync(cancellationToken);

    private async Task<IReadOnlyList<CelestialObject>> LoadOnceAsync()
    {
        var path = _databasePath ?? await ExtractAsync(_cacheDirectory!, CancellationToken.None).ConfigureAwait(false);
        return await Task.Run(() => Load(path, CancellationToken.None)).ConfigureAwait(false);
    }

    /// <summary>Metadata of the catalog (versions and checksums of the sources).</summary>
    public static IReadOnlyDictionary<string, string> ReadMetadata(string databasePath)
    {
        using var connection = OpenReadOnly(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";
        using var reader = command.ExecuteReader();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            metadata[reader.GetString(0)] = reader.GetString(1);
        }

        return metadata;
    }

    /// <summary>Copies the embedded database to the cache directory unless an identical copy exists.</summary>
    internal static async Task<string> ExtractAsync(string cacheDirectory, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await using (var resource = OpenResource())
        {
            await resource.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)))[..16];
        var path = Path.Combine(cacheDirectory, $"catalogs-{hash}.sqlite");
        if (!File.Exists(path) || new FileInfo(path).Length != buffer.Length)
        {
            Directory.CreateDirectory(cacheDirectory);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, buffer.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }

        // Copies left by older versions of the app.
        foreach (var old in Directory.EnumerateFiles(cacheDirectory, "catalogs-*.sqlite"))
        {
            if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(old);
            }
        }

        return path;
    }

    private static Stream OpenResource() =>
        typeof(SqliteCatalogRepository).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"Missing embedded resource '{ResourceName}'.");

    private static List<CelestialObject> Load(string path, CancellationToken cancellationToken)
    {
        try
        {
            return LoadObjects(path, cancellationToken);
        }
        catch (SqliteException ex)
        {
            // Callers see a data problem, not a database-engine type.
            throw new InvalidDataException($"The catalog database '{path}' could not be read: {ex.Message}", ex);
        }
    }

    private static List<CelestialObject> LoadObjects(string path, CancellationToken cancellationToken)
    {
        using var connection = OpenReadOnly(path);
        CheckSchema(connection, path);

        var designations = ReadGrouped(connection, "SELECT object_id, designation FROM designations ORDER BY object_id, designation");
        var names = ReadGrouped(connection, "SELECT object_id, name FROM names ORDER BY object_id, name");

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, ra_hours, dec_degrees, constellation, magnitude, surface_brightness,
                   major_axis_arcmin, minor_axis_arcmin, position_angle_degrees, common_name
            FROM objects ORDER BY id
            """;
        using var reader = command.ExecuteReader();

        var objects = new List<CelestialObject>(14_000);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = reader.GetString(0);
            var constellation = ConstellationLookup.FindByAbbreviation(reader.GetString(4))?.Name ?? reader.GetString(4);
            objects.Add(new CelestialObject(
                id,
                reader.GetString(10),
                constellation,
                Enum.Parse<CelestialObjectType>(reader.GetString(1)),
                Nullable(reader, 5) ?? double.NaN,
                new EquatorialCoordinates(reader.GetDouble(2), reader.GetDouble(3)))
            {
                SurfaceBrightness = Nullable(reader, 6),
                MajorAxisArcmin = Nullable(reader, 7),
                MinorAxisArcmin = Nullable(reader, 8),
                PositionAngleDegrees = Nullable(reader, 9),
                Designations = designations.GetValueOrDefault(id) ?? [],
                AlternateNames = names.GetValueOrDefault(id) ?? []
            });
        }

        return objects;
    }

    private static void CheckSchema(SqliteConnection connection, string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        var version = Convert.ToInt32(command.ExecuteScalar() ?? -1, CultureInfo.InvariantCulture);
        if (version != CatalogSchema.Version)
        {
            throw new InvalidDataException($"{path}: catalog schema {version}, expected {CatalogSchema.Version}.");
        }
    }

    private static Dictionary<string, List<string>> ReadGrouped(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
            }

            list.Add(reader.GetString(1));
        }

        return groups;
    }

    private static double? Nullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // In use by another instance: it will be removed next time.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
