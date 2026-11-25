using System.Globalization;
using System.Security.Cryptography;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Catalog;

/// <summary>
/// Catalog read from <c>catalogs.sqlite</c>: the deep-sky objects (OpenNGC + Caldwell + curated
/// Messier names) and the stars to <see cref="StarCatalogLimitMagnitude"/> (HYG) with their
/// double-star pairs (WDS). The database is embedded in this assembly; SQLite needs a file, so on
/// first use it is copied to <c>&lt;cacheDirectory&gt;/catalogs-&lt;hash&gt;.sqlite</c> (copies of
/// older versions are deleted) and opened read-only. The whole catalog (~22k objects) is loaded
/// into memory once: filtering and search never touch the database.
/// </summary>
public sealed class SqliteCatalogRepository : ICelestialRepository
{
    /// <summary>Faintest star of the catalog rows (the naked-eye limit; owner decision 2026-11-21). The sky draws fainter ones.</summary>
    public const double StarCatalogLimitMagnitude = 6.5;

    private const string ResourceName = "Catalogs/catalogs.sqlite";

    private readonly string _cacheDirectory;
    private readonly Lazy<Task<IReadOnlyList<CelestialObject>>> _objects;

    private SqliteCatalogRepository(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
        _objects = new(LoadOnceAsync);
    }

    /// <summary>Uses the embedded catalog, extracted into <paramref name="cacheDirectory"/>.</summary>
    public static SqliteCatalogRepository FromEmbedded(string cacheDirectory) =>
        new(cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory)));

    /// <summary>
    /// The catalog, loaded on the first call (later calls share the same list). Cancelling
    /// only stops this caller from waiting: the load itself completes for the next one.
    /// </summary>
    public Task<IReadOnlyList<CelestialObject>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _objects.Value.WaitAsync(cancellationToken);

    private async Task<IReadOnlyList<CelestialObject>> LoadOnceAsync()
    {
        var path = await ExtractAsync(_cacheDirectory, CancellationToken.None).ConfigureAwait(false);
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

        var objects = new List<CelestialObject>(23_000);
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

        objects.AddRange(LoadStars(connection, cancellationToken));
        return objects;
    }

    /// <summary>The stars to <see cref="StarCatalogLimitMagnitude"/>, as catalog rows ("HIP 32349 · Sirius").</summary>
    private static List<CelestialObject> LoadStars(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var pairs = new Dictionary<int, List<DoubleStarPair>>();
        using (var pairCommand = connection.CreateCommand())
        {
            pairCommand.CommandText = """
                SELECT d.hyg_id, d.wds_id, d.discoverer, d.components, d.last_year, d.pa_degrees, d.separation_arcsec,
                       d.magnitude1, d.magnitude2
                FROM double_stars d JOIN stars s ON s.hyg_id = d.hyg_id
                WHERE s.magnitude <= $limit
                ORDER BY d.hyg_id, d.separation_arcsec, d.wds_id, d.discoverer, d.components
                """;
            pairCommand.Parameters.AddWithValue("$limit", StarCatalogLimitMagnitude);
            using var pairReader = pairCommand.ExecuteReader();
            while (pairReader.Read())
            {
                var hygId = pairReader.GetInt32(0);
                if (!pairs.TryGetValue(hygId, out var list))
                {
                    pairs[hygId] = list = [];
                }

                list.Add(new DoubleStarPair(pairReader.GetString(1), pairReader.GetString(2), pairReader.GetString(3), pairReader.GetInt32(4),
                    Nullable(pairReader, 5), pairReader.GetDouble(6), pairReader.GetDouble(7), pairReader.GetDouble(8)));
            }
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT hyg_id, hip, hd, hr, proper_name, bayer, flamsteed, constellation, ra_hours, dec_degrees, magnitude,
                   color_index, spectral_type, distance_pc, variable, var_min, var_max
            FROM stars WHERE magnitude <= $limit ORDER BY hyg_id
            """;
        command.Parameters.AddWithValue("$limit", StarCatalogLimitMagnitude);
        using var reader = command.ExecuteReader();

        var stars = new List<CelestialObject>(9_000);
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hygId = reader.GetInt32(0);
            int? hip = reader.IsDBNull(1) ? null : reader.GetInt32(1);
            int? hd = reader.IsDBNull(2) ? null : reader.GetInt32(2);
            int? hr = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            var proper = reader.IsDBNull(4) ? null : reader.GetString(4);
            var bayer = reader.IsDBNull(5) ? null : reader.GetString(5);
            int? flamsteed = reader.IsDBNull(6) ? null : reader.GetInt32(6);
            var abbreviation = reader.GetString(7);
            var starPairs = pairs.GetValueOrDefault(hygId) ?? [];
            var info = new StarInfo(hygId, hip, hd, reader.IsDBNull(12) ? null : reader.GetString(12), Nullable(reader, 11),
                Nullable(reader, 13), reader.IsDBNull(14) ? null : reader.GetString(14), Nullable(reader, 15), Nullable(reader, 16))
            {
                Pairs = starPairs
            };

            var id = hip is { } h ? Id("HIP", h) : hd is { } d ? Id("HD", d) : Id("HYG", hygId);
            var designations = new List<string>();
            AddIfNew(designations, id, hip is { } hip2 ? Id("HIP", hip2) : null);
            AddIfNew(designations, id, hd is { } hd2 ? Id("HD", hd2) : null);
            AddIfNew(designations, id, hr is { } hr2 ? Id("HR", hr2) : null);
            AddIfNew(designations, id, bayer is null ? null : $"{bayer.Replace("-", "", StringComparison.Ordinal)} {abbreviation}");
            AddIfNew(designations, id, StarNames.Flamsteed(flamsteed, abbreviation));
            foreach (var pair in starPairs)
            {
                AddIfNew(designations, id, "WDS " + pair.WdsId);
                AddIfNew(designations, id, pair.DiscovererName);
            }

            var alternate = new List<string>();
            if (proper is not null)
            {
                AddIfNew(alternate, proper, StarNames.GreekBayer(bayer, abbreviation));
            }

            AddIfNew(alternate, proper ?? "", StarNames.SpelledBayer(bayer, abbreviation));
            // "β Cyg" and "Beta Cygni" also find β¹ Cyg (Albireo), the way people search.
            AddIfNew(alternate, proper ?? "", StarNames.GreekBayer(bayer, abbreviation, withIndex: false));
            AddIfNew(alternate, proper ?? "", StarNames.SpelledBayer(bayer, abbreviation, withIndex: false));
            AddIfNew(alternate, proper ?? "", StarNames.SpelledFlamsteed(flamsteed, abbreviation));

            stars.Add(new CelestialObject(
                id,
                StarNames.Label(proper, bayer, flamsteed, abbreviation) ?? "",
                ConstellationLookup.FindByAbbreviation(abbreviation)?.Name ?? abbreviation,
                starPairs.Count > 0 ? CelestialObjectType.DoubleStar : CelestialObjectType.Star,
                reader.GetDouble(10),
                new EquatorialCoordinates(reader.GetDouble(8), reader.GetDouble(9)))
            {
                Star = info,
                Designations = designations,
                AlternateNames = alternate
            });
        }

        return stars;

        static string Id(string catalog, int number) => string.Create(CultureInfo.InvariantCulture, $"{catalog} {number}");

        static void AddIfNew(List<string> list, string excluded, string? value)
        {
            if (value is not null && value != excluded && !list.Contains(value))
            {
                list.Add(value);
            }
        }
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
