using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.Data.Storage;

/// <summary>Cameras, optics, mounts and rigs in app.db (migration 0003); active rig in settings.</summary>
public sealed class SqliteEquipmentRepository(AppDatabase database) : IEquipmentRepository
{
    private const string ActiveRigKey = "active_rig";
    private const string SeededKey = "equipment_seeded";

    private readonly AppDatabase _database = database ?? throw new ArgumentNullException(nameof(database));

    public EquipmentInventory Load()
    {
        if (SettingsTable.Read(_database, SeededKey) is null)
        {
            if (IsEmpty())
            {
                Save(EquipmentInventory.Demo);
            }

            SettingsTable.Write(_database, SeededKey, "true");
        }

        using var connection = _database.Open();
        var cameras = Read(connection, """
            SELECT id, name, kind, color, sensor_width_mm, sensor_height_mm, resolution_width_px, resolution_height_px,
                   pixel_size_um, quantum_efficiency, read_noise_e, dark_current_e_per_s, recommended_iso, recommended_gain, notes
            FROM cameras ORDER BY position
            """, r => new Camera(
            r.GetString(0), r.GetString(1), Parse<CameraKind>(r.GetString(2)), Parse<SensorColor>(r.GetString(3)),
            r.GetDouble(4), r.GetDouble(5), r.GetInt32(6), r.GetInt32(7), NullableDouble(r, 8),
            r.GetDouble(9), r.GetDouble(10), r.GetDouble(11), NullableInt(r, 12), NullableInt(r, 13), r.GetString(14)));
        var optics = Read(connection,
            "SELECT id, name, kind, focal_length_mm, aperture_mm, transmission, notes FROM optics ORDER BY position",
            r => new Optic(r.GetString(0), r.GetString(1), Parse<OpticKind>(r.GetString(2)), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetString(6)));
        var mounts = Read(connection,
            "SELECT id, name, kind, max_unguided_exposure_s, payload_kg, notes FROM mounts ORDER BY position",
            r => new Mount(r.GetString(0), r.GetString(1), Parse<MountKind>(r.GetString(2)), NullableDouble(r, 3), NullableDouble(r, 4), r.GetString(5)));
        var rigs = Read(connection,
            "SELECT id, name, camera_id, optic_id, mount_id, focal_multiplier, notes FROM rigs ORDER BY position",
            r => new Rig(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDouble(5), r.GetString(6)));

        var active = SettingsTable.Read(_database, ActiveRigKey);
        return new EquipmentInventory(cameras, optics, mounts, rigs, rigs.Any(r => r.Id == active) ? active : null);
    }

    public void Save(EquipmentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        foreach (var rig in inventory.Rigs)
        {
            if (inventory.Cameras.All(c => c.Id != rig.CameraId) || inventory.Optics.All(o => o.Id != rig.OpticId) ||
                inventory.Mounts.All(m => m.Id != rig.MountId))
            {
                throw new ArgumentException($"Rig '{rig.Name}' refers to equipment that is not in the inventory.", nameof(inventory));
            }
        }

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var table in new[] { "rigs", "cameras", "optics", "mounts" })
        {
            Sql.Execute(connection, transaction, $"DELETE FROM {table}");
        }

        using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO cameras (id, position, name, kind, color, sensor_width_mm, sensor_height_mm, resolution_width_px,
                                 resolution_height_px, pixel_size_um, quantum_efficiency, read_noise_e, dark_current_e_per_s,
                                 recommended_iso, recommended_gain, notes)
            VALUES ($id, $p, $name, $kind, $color, $w, $h, $rw, $rh, $pixel, $qe, $rn, $dark, $iso, $gain, $notes)
            """, "$id", "$p", "$name", "$kind", "$color", "$w", "$h", "$rw", "$rh", "$pixel", "$qe", "$rn", "$dark", "$iso", "$gain", "$notes"))
        {
            var position = 0;
            foreach (var c in inventory.Cameras.DistinctBy(c => c.Id))
            {
                Sql.Run(insert, c.Id, position++, c.Name, c.Kind.ToString(), c.Color.ToString(), c.SensorWidthMm, c.SensorHeightMm,
                    c.ResolutionWidthPx, c.ResolutionHeightPx, Db(c.PixelSizeUm), c.QuantumEfficiency, c.ReadNoiseElectrons,
                    c.DarkCurrentElectronsPerSecond, Db(c.RecommendedIso), Db(c.RecommendedGain), c.Notes);
            }
        }

        using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO optics (id, position, name, kind, focal_length_mm, aperture_mm, transmission, notes)
            VALUES ($id, $p, $name, $kind, $f, $d, $t, $notes)
            """, "$id", "$p", "$name", "$kind", "$f", "$d", "$t", "$notes"))
        {
            var position = 0;
            foreach (var o in inventory.Optics.DistinctBy(o => o.Id))
            {
                Sql.Run(insert, o.Id, position++, o.Name, o.Kind.ToString(), o.FocalLengthMm, o.ApertureMm, o.Transmission, o.Notes);
            }
        }

        using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO mounts (id, position, name, kind, max_unguided_exposure_s, payload_kg, notes)
            VALUES ($id, $p, $name, $kind, $max, $kg, $notes)
            """, "$id", "$p", "$name", "$kind", "$max", "$kg", "$notes"))
        {
            var position = 0;
            foreach (var m in inventory.Mounts.DistinctBy(m => m.Id))
            {
                Sql.Run(insert, m.Id, position++, m.Name, m.Kind.ToString(), Db(m.MaxUnguidedExposureSeconds), Db(m.PayloadKg), m.Notes);
            }
        }

        using (var insert = Sql.Command(connection, transaction, """
            INSERT INTO rigs (id, position, name, camera_id, optic_id, mount_id, focal_multiplier, notes)
            VALUES ($id, $p, $name, $camera, $optic, $mount, $k, $notes)
            """, "$id", "$p", "$name", "$camera", "$optic", "$mount", "$k", "$notes"))
        {
            var position = 0;
            foreach (var r in inventory.Rigs.DistinctBy(r => r.Id))
            {
                Sql.Run(insert, r.Id, position++, r.Name, r.CameraId, r.OpticId, r.MountId, r.FocalMultiplier, r.Notes);
            }
        }

        using (var active = Sql.Command(connection, transaction,
            "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT (key) DO UPDATE SET value = excluded.value", "$key", "$value"))
        {
            Sql.Run(active, ActiveRigKey, inventory.ActiveRigId ?? string.Empty);
        }

        transaction.Commit();
    }

    private bool IsEmpty()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM cameras) + (SELECT COUNT(*) FROM optics) + (SELECT COUNT(*) FROM mounts) + (SELECT COUNT(*) FROM rigs)";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) == 0;
    }

    private static List<T> Read<T>(SqliteConnection connection, string sql, Func<SqliteDataReader, T> map)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var items = new List<T>();
        while (reader.Read())
        {
            items.Add(map(reader));
        }

        return items;
    }

    private static T Parse<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : default;

    private static double? NullableDouble(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    private static int? NullableInt(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static object Db(double? value) => value is { } v ? v : DBNull.Value;

    private static object Db(int? value) => value is { } v ? v : DBNull.Value;
}
