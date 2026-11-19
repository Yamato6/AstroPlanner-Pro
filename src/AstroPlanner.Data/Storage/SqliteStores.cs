using System.Globalization;
using System.Text.Json;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Storage;

/// <summary>Observer settings stored as one JSON document (key 'observer') in app.db.</summary>
public sealed class SqliteSettingsService(AppDatabase database) : ISettingsService
{
    private const string Key = "observer";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public ObserverSettings Load()
    {
        try
        {
            return SettingsTable.Read(_database, Key) is { } json
                ? JsonSerializer.Deserialize<ObserverSettings>(json, Options) ?? new ObserverSettings()
                : new ObserverSettings();
        }
        catch (JsonException)
        {
            // A damaged value: start again from the defaults.
            return new ObserverSettings();
        }
    }

    public void Save(ObserverSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsTable.Write(_database, Key, JsonSerializer.Serialize(settings, Options));
    }
}

/// <summary>Sky view layers, limits and camera, stored as JSON under the "sky" key.</summary>
public sealed class SqliteSkyViewStore(AppDatabase database) : ISkyViewStore
{
    private const string Key = "sky";
    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public SkyViewSettings Load()
    {
        try
        {
            return SettingsTable.Read(_database, Key) is { } json
                ? JsonSerializer.Deserialize<SkyViewSettings>(json) ?? new SkyViewSettings()
                : new SkyViewSettings();
        }
        catch (JsonException)
        {
            return new SkyViewSettings();
        }
    }

    public void Save(SkyViewSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsTable.Write(_database, Key, JsonSerializer.Serialize(settings));
    }
}

/// <summary>Small key/value access to the settings table, also used for app flags.</summary>
public static class SettingsTable
{
    public static string? Read(AppDatabase database, string key)
    {
        ArgumentNullException.ThrowIfNull(database);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public static void Write(AppDatabase database, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(database);
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}

/// <summary>"My plan" in app.db.</summary>
public sealed class SqlitePlanService(AppDatabase database) : IObservationPlanService
{
    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<ObservationPlanEntry> Load()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT object_id, note FROM plan_items ORDER BY position";
        using var reader = command.ExecuteReader();
        var entries = new List<ObservationPlanEntry>();
        while (reader.Read())
        {
            entries.Add(new ObservationPlanEntry(reader.GetString(0), reader.GetString(1)));
        }

        return entries;
    }

    public void Save(IEnumerable<ObservationPlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        Sql.Execute(connection, transaction, "DELETE FROM plan_items");
        using var insert = Sql.Command(connection, transaction,
            "INSERT INTO plan_items (position, object_id, note) VALUES ($p, $id, $note)", "$p", "$id", "$note");
        var position = 0;
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.Id)))
        {
            Sql.Run(insert, position++, entry.Id, entry.Note ?? string.Empty);
        }

        transaction.Commit();
    }
}

/// <summary>Observation logbook in app.db.</summary>
public sealed class SqliteLogbookService(AppDatabase database) : IObservationLogService
{
    private const string DateFormat = "yyyy-MM-ddTHH:mm:ss.FFFFFFF";

    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<ObservationLogEntry> Load()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, object_id, object_name, observed_at_local, location, equipment, seeing, transparency, rating, notes
            FROM logbook ORDER BY observed_at_local, id
            """;
        using var reader = command.ExecuteReader();
        var entries = new List<ObservationLogEntry>();
        while (reader.Read())
        {
            entries.Add(new ObservationLogEntry(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                DateTime.ParseExact(reader.GetString(3), DateFormat, CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetString(9)));
        }

        return entries;
    }

    public void Save(IEnumerable<ObservationLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        Sql.Execute(connection, transaction, "DELETE FROM logbook");
        using var insert = Sql.Command(connection, transaction,
            """
            INSERT INTO logbook (id, object_id, object_name, observed_at_local, location, equipment, seeing, transparency, rating, notes)
            VALUES ($id, $object, $name, $at, $location, $equipment, $seeing, $transparency, $rating, $notes)
            """,
            "$id", "$object", "$name", "$at", "$location", "$equipment", "$seeing", "$transparency", "$rating", "$notes");
        foreach (var entry in entries.Where(e => !string.IsNullOrWhiteSpace(e.ObjectId)).DistinctBy(e => e.Id))
        {
            Sql.Run(insert, entry.Id.ToString("D"), entry.ObjectId, entry.ObjectName ?? string.Empty,
                entry.ObservedAtLocal.ToString(DateFormat, CultureInfo.InvariantCulture), entry.Location ?? string.Empty,
                entry.Equipment ?? string.Empty, entry.Seeing, entry.Transparency, entry.Rating, entry.Notes ?? string.Empty);
        }

        transaction.Commit();
    }
}

/// <summary>Saved locations in app.db.</summary>
public sealed class SqliteLocationRepository(AppDatabase database) : ILocationRepository
{
    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public IReadOnlyList<Location> GetAll()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, latitude_degrees, longitude_degrees, elevation_meters, iana_time_zone_id,
                   windows_time_zone_id, horizon_mask, notes
            FROM locations ORDER BY position, name
            """;
        using var reader = command.ExecuteReader();
        var locations = new List<Location>();
        while (reader.Read())
        {
            locations.Add(new Location(
                reader.GetString(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4),
                reader.GetString(5), reader.GetString(6), HorizonMaskJson.Parse(reader.GetString(7)), reader.GetString(8)));
        }

        return locations;
    }

    public void Save(Location location)
    {
        ArgumentNullException.ThrowIfNull(location);
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO locations (id, position, name, latitude_degrees, longitude_degrees, elevation_meters,
                                   iana_time_zone_id, windows_time_zone_id, horizon_mask, notes)
            VALUES ($id, COALESCE((SELECT position FROM locations WHERE id = $id), (SELECT COALESCE(MAX(position), -1) + 1 FROM locations)),
                    $name, $lat, $lon, $elev, $iana, $windows, $mask, $notes)
            ON CONFLICT (id) DO UPDATE SET
                name = excluded.name, latitude_degrees = excluded.latitude_degrees, longitude_degrees = excluded.longitude_degrees,
                elevation_meters = excluded.elevation_meters, iana_time_zone_id = excluded.iana_time_zone_id,
                windows_time_zone_id = excluded.windows_time_zone_id, horizon_mask = excluded.horizon_mask, notes = excluded.notes
            """;
        command.Parameters.AddWithValue("$id", location.Id);
        command.Parameters.AddWithValue("$name", location.Name);
        command.Parameters.AddWithValue("$lat", location.LatitudeDegrees);
        command.Parameters.AddWithValue("$lon", location.LongitudeDegrees);
        command.Parameters.AddWithValue("$elev", location.ElevationMeters);
        command.Parameters.AddWithValue("$iana", location.IanaTimeZoneId);
        command.Parameters.AddWithValue("$windows", location.WindowsTimeZoneId);
        command.Parameters.AddWithValue("$mask", HorizonMaskJson.Serialize(location.HorizonMask));
        command.Parameters.AddWithValue("$notes", location.Notes);
        command.ExecuteNonQuery();
    }

    public void Delete(string id)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM locations WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}

/// <summary>JSON form of a horizon mask: [{"az":90,"alt":10}, …].</summary>
public static class HorizonMaskJson
{
    private sealed record Point(double Az, double Alt);

    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Serialize(HorizonMask mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        return JsonSerializer.Serialize(mask.Points.Select(p => new Point(p.AzimuthDegrees, p.AltitudeDegrees)), Options);
    }

    public static HorizonMask Parse(string json)
    {
        var points = JsonSerializer.Deserialize<List<Point>>(string.IsNullOrWhiteSpace(json) ? "[]" : json, Options) ?? [];
        return new HorizonMask(points.Select(p => new HorizonPoint(p.Az, p.Alt)));
    }
}

/// <summary>Command helpers shared by the stores.</summary>
internal static class Sql
{
    public static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = parameter });
        }

        return command;
    }

    public static void Run(SqliteCommand command, params object[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters[index].Value = values[index];
        }

        command.ExecuteNonQuery();
    }
}
