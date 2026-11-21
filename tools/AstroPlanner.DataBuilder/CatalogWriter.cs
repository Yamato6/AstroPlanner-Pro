using System.Globalization;
using AstroPlanner.Data.Catalog;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Writes <c>catalogs.sqlite</c>. The output is deterministic: rows are inserted in a fixed
/// order, no timestamps are stored and the file is vacuumed, so the same inputs (and the same
/// SQLite version) always give the same bytes.
/// </summary>
internal static class CatalogWriter
{
    public static void Write(
        string path,
        IReadOnlyList<CatalogEntry> objects,
        IReadOnlyList<StarEntry> stars,
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyList<DoubleStarEntry>? doubles = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(stars);
        ArgumentNullException.ThrowIfNull(metadata);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            Execute(connection, "PRAGMA page_size = 4096; PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF;");
            Execute(connection, CatalogSchema.CreateTables);

            using (var transaction = connection.BeginTransaction())
            {
                WriteMeta(connection, transaction, metadata);
                WriteObjects(connection, transaction, objects);
                WriteStars(connection, transaction, stars);
                WriteDoubles(connection, transaction, doubles ?? []);
                transaction.Commit();
            }

            Execute(connection, "VACUUM;");
        }
    }

    private static void WriteMeta(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyDictionary<string, string> metadata)
    {
        using var command = Command(connection, transaction, "INSERT INTO meta (key, value) VALUES ($key, $value)", "$key", "$value");
        foreach (var (key, value) in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Run(command, key, value);
        }
    }

    private static void WriteObjects(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<CatalogEntry> objects)
    {
        using var insertObject = Command(connection, transaction,
            """
            INSERT INTO objects (id, type, ra_hours, dec_degrees, constellation, magnitude, magnitude_band,
                surface_brightness, major_axis_arcmin, minor_axis_arcmin, position_angle_degrees, common_name, source)
            VALUES ($id, $type, $ra, $dec, $con, $mag, $band, $sb, $maj, $min, $pa, $name, $source)
            """,
            "$id", "$type", "$ra", "$dec", "$con", "$mag", "$band", "$sb", "$maj", "$min", "$pa", "$name", "$source");
        using var insertDesignation = Command(connection, transaction,
            "INSERT INTO designations (object_id, designation, catalog) VALUES ($id, $designation, $catalog)",
            "$id", "$designation", "$catalog");
        using var insertName = Command(connection, transaction,
            "INSERT INTO names (object_id, name) VALUES ($id, $name)", "$id", "$name");

        foreach (var entry in objects.OrderBy(o => o.Id, StringComparer.Ordinal))
        {
            Run(insertObject, entry.Id, entry.Type.ToString(), Round(entry.RightAscensionHours, 7), Round(entry.DeclinationDegrees, 6),
                entry.Constellation, entry.Magnitude, entry.MagnitudeBand, entry.SurfaceBrightness, entry.MajorAxisArcmin,
                entry.MinorAxisArcmin, entry.PositionAngleDegrees, entry.CommonName, entry.Source);

            foreach (var designation in entry.Designations.OrderBy(d => d.Value, StringComparer.Ordinal))
            {
                Run(insertDesignation, entry.Id, designation.Value, designation.Catalog);
            }

            foreach (var name in entry.AlternateNames.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                Run(insertName, entry.Id, name);
            }
        }
    }

    private static void WriteStars(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<StarEntry> stars)
    {
        using var insert = Command(connection, transaction,
            """
            INSERT INTO stars (hyg_id, hip, hd, hr, proper_name, bayer, flamsteed, constellation, ra_hours, dec_degrees,
                magnitude, color_index, pm_ra_mas, pm_dec_mas, spectral_type, distance_pc, variable, var_min, var_max,
                component, comp_primary, system)
            VALUES ($id, $hip, $hd, $hr, $proper, $bayer, $flam, $con, $ra, $dec, $mag, $ci, $pmra, $pmdec, $spect, $dist,
                $var, $varmin, $varmax, $comp, $primary, $system)
            """,
            "$id", "$hip", "$hd", "$hr", "$proper", "$bayer", "$flam", "$con", "$ra", "$dec", "$mag", "$ci", "$pmra", "$pmdec",
            "$spect", "$dist", "$var", "$varmin", "$varmax", "$comp", "$primary", "$system");

        foreach (var star in stars.OrderBy(s => s.HygId))
        {
            Run(insert, star.HygId, star.Hip, star.Hd, star.Hr, star.ProperName, star.Bayer, star.Flamsteed, star.Constellation,
                Round(star.RightAscensionHours, 7), Round(star.DeclinationDegrees, 6), star.Magnitude, star.ColorIndex,
                star.ProperMotionRaMas, star.ProperMotionDecMas, star.SpectralType,
                star.DistanceParsecs is { } distance ? Round(distance, 4) : null, star.VariableName,
                star.VariableMinimumMagnitude, star.VariableMaximumMagnitude, star.Component, star.ComponentPrimary, star.MultipleSystem);
        }
    }

    private static void WriteDoubles(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<DoubleStarEntry> doubles)
    {
        using var insert = Command(connection, transaction,
            """
            INSERT INTO double_stars (hyg_id, wds_id, discoverer, components, first_year, last_year, pa_degrees,
                separation_arcsec, magnitude1, magnitude2, spectral_type)
            VALUES ($hyg, $wds, $disc, $comp, $first, $last, $pa, $sep, $m1, $m2, $spect)
            """,
            "$hyg", "$wds", "$disc", "$comp", "$first", "$last", "$pa", "$sep", "$m1", "$m2", "$spect");

        foreach (var pair in doubles)
        {
            Run(insert, pair.HygId, pair.WdsId, pair.Discoverer, pair.Components, pair.FirstYear, pair.LastYear,
                pair.PositionAngleDegrees, pair.SeparationArcsec, pair.PrimaryMagnitude, pair.SecondaryMagnitude, pair.SpectralType);
        }
    }

    /// <summary>~0.4 mas: far below the catalog precision, and it keeps the file smaller.</summary>
    private static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.ToEven);

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql, params string[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new SqliteParameter { ParameterName = parameter });
        }

        command.Prepare();
        return command;
    }

    private static void Run(SqliteCommand command, params object?[] values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            command.Parameters[index].Value = values[index] switch
            {
                null => DBNull.Value,
                double d when double.IsNaN(d) => DBNull.Value,
                IFormattable formattable when formattable is not double and not int => formattable.ToString(null, CultureInfo.InvariantCulture),
                var value => value
            };
        }

        command.ExecuteNonQuery();
    }
}
