using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Storage;

/// <summary>
/// The user's database (<c>app.db</c>): settings, locations, plan and logbook. On
/// <see cref="Initialize"/> the versioned SQL migrations embedded in this assembly
/// (Storage/Migrations/NNNN_name.sql) are applied in order, each inside a transaction; before
/// migrating an existing database a backup copy is made. A file that is not a valid database is
/// moved aside (<c>app.corrupt-&lt;date&gt;.db</c>) instead of being overwritten.
/// </summary>
public sealed class AppDatabase
{
    private const string MigrationPrefix = "Migrations/";

    private readonly TimeProvider _time;
    private readonly IReadOnlyList<Migration> _migrations;
    private bool _initialized;

    public AppDatabase(string path, TimeProvider? time = null)
        : this(path, time, null)
    {
    }

    /// <summary>For tests: a custom list of (version, name, SQL) migrations.</summary>
    internal AppDatabase(string path, TimeProvider? time, IEnumerable<(int Version, string Name, string Sql)>? migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _time = time ?? TimeProvider.System;
        _migrations = migrations is null
            ? EmbeddedMigrations.Value
            : [.. migrations.Select(m => new Migration(m.Version, m.Name, m.Sql)).OrderBy(m => m.Version)];
    }

    public string Path { get; }

    /// <summary>True if <see cref="Initialize"/> created a new, empty database (first run).</summary>
    public bool WasCreated { get; private set; }

    /// <summary>Where a damaged database was moved, if that happened.</summary>
    public string? PreservedCorruptFilePath { get; private set; }

    /// <summary>Backup made before the last migration, if any.</summary>
    public string? BackupFilePath { get; private set; }

    /// <summary>Schema version after <see cref="Initialize"/>.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>Latest schema version known by this build.</summary>
    public int LatestVersion => _migrations.Max(m => m.Version);

    private static readonly Lazy<IReadOnlyList<Migration>> EmbeddedMigrations = new(LoadMigrations);

    /// <summary>Creates or migrates the database. Safe to call more than once.</summary>
    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        WasCreated = !File.Exists(Path);

        try
        {
            Migrate();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 26 /* SQLITE_NOTADB */ or 11 /* SQLITE_CORRUPT */)
        {
            SqliteConnection.ClearAllPools();
            PreservedCorruptFilePath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Path)!,
                string.Create(CultureInfo.InvariantCulture,
                    $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.corrupt-{_time.GetLocalNow():yyyyMMdd-HHmmss}.db"));
            File.Move(Path, PreservedCorruptFilePath, overwrite: true);
            WasCreated = true;
            Migrate();
        }

        _initialized = true;
    }

    /// <summary>Opens a connection (the database must be initialized).</summary>
    internal SqliteConnection Open()
    {
        Initialize();
        return OpenConnection();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var connection = OpenConnection();
        Execute(connection, null,
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY NOT NULL, name TEXT NOT NULL, applied_utc TEXT NOT NULL);");

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations";
        var current = Convert.ToInt32(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

        if (current > LatestVersion)
        {
            throw new InvalidDataException(
                $"{Path} was written by a newer version of AstroPlanner Pro (schema {current} > {LatestVersion}).");
        }

        var pending = _migrations.Where(m => m.Version > current).ToList();
        if (pending.Count > 0 && current > 0)
        {
            Backup(connection, current);
        }

        foreach (var migration in pending)
        {
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, migration.Sql);
            using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO schema_migrations (version, name, applied_utc) VALUES ($v, $n, $t)";
            record.Parameters.AddWithValue("$v", migration.Version);
            record.Parameters.AddWithValue("$n", migration.Name);
            record.Parameters.AddWithValue("$t", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            record.ExecuteNonQuery();
            transaction.Commit();
            current = migration.Version;
        }

        SchemaVersion = current;
    }

    /// <summary>Copies the database (consistent snapshot, WAL included) before migrating it.</summary>
    private void Backup(SqliteConnection connection, int version)
    {
        BackupFilePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Path)!,
            string.Create(CultureInfo.InvariantCulture, $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.backup-v{version}.db"));
        if (File.Exists(BackupFilePath))
        {
            File.Delete(BackupFilePath);
        }

        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path";
        command.Parameters.AddWithValue("$path", BackupFilePath);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static List<Migration> LoadMigrations()
    {
        var assembly = typeof(AppDatabase).Assembly;
        return [.. assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(MigrationPrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                var fileName = name[MigrationPrefix.Length..];
                return new Migration(
                    int.Parse(fileName.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture),
                    fileName,
                    reader.ReadToEnd());
            })
            .OrderBy(m => m.Version)];
    }

    private sealed record Migration(int Version, string Name, string Sql);
}
