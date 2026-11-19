using System.Globalization;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Sanity checks of a built <c>catalogs.sqlite</c>: row counts and spot checks of well-known
/// objects (spec §7.2). Returns the list of failures (empty when everything is right).
/// </summary>
internal static class CatalogVerifier
{
    public sealed record Expectations(int MinimumObjects, int MinimumStars, int MessierCount, int CaldwellCount, bool SpotChecks);

    /// <summary>What a full build from the real sources must contain.</summary>
    public static readonly Expectations FullBuild = new(13_000, 80_000, 110, 109, SpotChecks: true);

    public static IReadOnlyList<string> Verify(string path, Expectations expectations)
    {
        ArgumentNullException.ThrowIfNull(expectations);

        var failures = new List<string>();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        long Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        void Expect(bool condition, string message)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }

        var objects = Scalar("SELECT COUNT(*) FROM objects");
        var stars = Scalar("SELECT COUNT(*) FROM stars");
        var messier = Scalar(
            "SELECT COUNT(DISTINCT v) FROM (SELECT id AS v FROM objects WHERE id GLOB 'M[0-9]*' " +
            "UNION SELECT designation FROM designations WHERE catalog = 'M')");
        var caldwell = Scalar(
            "SELECT COUNT(DISTINCT v) FROM (SELECT id AS v FROM objects WHERE id GLOB 'C[0-9]*' " +
            "UNION SELECT designation FROM designations WHERE catalog = 'C')");

        Expect(objects >= expectations.MinimumObjects, $"objects: {objects} < {expectations.MinimumObjects}");
        Expect(stars >= expectations.MinimumStars, $"stars: {stars} < {expectations.MinimumStars}");
        Expect(messier == expectations.MessierCount, $"Messier objects: {messier}, expected {expectations.MessierCount}");
        Expect(caldwell == expectations.CaldwellCount, $"Caldwell objects: {caldwell}, expected {expectations.CaldwellCount}");
        Expect(Scalar("SELECT COUNT(*) FROM meta WHERE key = 'schema_version'") == 1, "meta.schema_version is missing");

        if (!expectations.SpotChecks)
        {
            return failures;
        }

        void ExpectObject(string id, string type, string constellation, string? designation = null)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT type, constellation FROM objects WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                failures.Add($"{id} is missing");
                return;
            }

            Expect(reader.GetString(0) == type, $"{id}: type {reader.GetString(0)}, expected {type}");
            Expect(reader.GetString(1) == constellation, $"{id}: constellation {reader.GetString(1)}, expected {constellation}");

            if (designation is not null)
            {
                using var cross = connection.CreateCommand();
                cross.CommandText = "SELECT COUNT(*) FROM designations WHERE object_id = $id AND designation = $d";
                cross.Parameters.AddWithValue("$id", id);
                cross.Parameters.AddWithValue("$d", designation);
                Expect(Convert.ToInt64(cross.ExecuteScalar(), CultureInfo.InvariantCulture) == 1, $"{id} is not cross-identified as {designation}");
            }
        }

        ExpectObject("M31", "Galaxy", "And", "NGC 224");
        ExpectObject("M42", "ClusterWithNebula", "Ori", "NGC 1976");
        ExpectObject("M45", "OpenCluster", "Tau", "Mel 22");
        ExpectObject("M40", "DoubleStar", "UMa");
        ExpectObject("M102", "Galaxy", "Dra", "NGC 5866");
        ExpectObject("M1", "SupernovaRemnant", "Tau", "NGC 1952");
        ExpectObject("C14", "StellarAssociation", "Per");
        ExpectObject("NGC 7000", "HiiRegion", "Cyg", "C20");
        ExpectObject("IC 434", "HiiRegion", "Ori");

        void ExpectStar(string name, string constellation, double magnitude)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT constellation, magnitude FROM stars WHERE proper_name = $name";
            command.Parameters.AddWithValue("$name", name);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                failures.Add($"star {name} is missing");
                return;
            }

            Expect(reader.GetString(0) == constellation, $"{name}: constellation {reader.GetString(0)}, expected {constellation}");
            Expect(Math.Abs(reader.GetDouble(1) - magnitude) < 0.1, $"{name}: magnitude {reader.GetDouble(1)}, expected {magnitude}");
        }

        ExpectStar("Polaris", "UMi", 1.97);
        ExpectStar("Sirius", "CMa", -1.44);
        ExpectStar("Vega", "Lyr", 0.03);

        return failures;
    }
}
