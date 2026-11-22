using System.Buffers.Binary;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Sky;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Builds the static data of the sky renderer (spec §5.3, §7.2) from <c>catalogs.sqlite</c> and the
/// d3-celestial GeoJSON: binary buffers of J2000 unit vectors plus small JSON label files. Every
/// binary file starts with a 16-byte header: magic "APSB", format version, record count and record
/// size (uint32, little-endian). The layouts are documented in docs/sky-renderer.md and decoded by
/// src/web/packages/sky-engine/src/data/buffers.ts.
/// </summary>
internal static class SkyDataBuilder
{
    public const uint FormatVersion = 1;
    public const int HeaderBytes = 16;
    public static ReadOnlySpan<byte> Magic => "APSB"u8;

    /// <summary>x, y, z, magnitude, B−V (float32) + HYG id, HIP or 0 (uint32).</summary>
    public const int StarRecordBytes = 28;

    /// <summary>x, y, z, magnitude (99 = unknown), major/minor axis arcmin (0 = unknown), PA degrees (−1 = unknown) (float32) + type index (uint32).</summary>
    public const int DsoRecordBytes = 32;

    /// <summary>Segment end points x1, y1, z1, x2, y2, z2 (float32) + group index (uint32).</summary>
    public const int SegmentRecordBytes = 28;

    /// <summary>Color index assumed when HYG has none (a Sun-like star).</summary>
    public const float DefaultColorIndex = 0.65f;

    /// <summary>Boundary and Milky Way edges longer than this are split so they follow the sphere.</summary>
    private const double MaximumSegmentDegrees = 2.0;

    public static readonly string[] OutputFiles =
    [
        "stars.bin", "star-names.json", "dsos.bin", "dsos.json",
        "constellation-lines.bin", "constellation-bounds.bin", "milkyway.bin", "constellation-labels.json"
    ];

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Indented = false
    };

    public sealed record Result(int Stars, int NamedStars, int Dsos, int LineSegments, int BoundarySegments, int MilkyWaySegments, int Constellations)
    {
        public string Report => string.Create(CultureInfo.InvariantCulture,
            $"sky: {Stars} stars ({NamedStars} named), {Dsos} deep-sky objects, {Constellations} constellations, " +
            $"{LineSegments} line / {BoundarySegments} boundary / {MilkyWaySegments} Milky Way segments");
    }

    /// <summary>Builds every output file into <paramref name="outputDirectory"/>.</summary>
    /// <param name="catalogPath">The built catalogs.sqlite.</param>
    /// <param name="geoJsonDirectory">Folder with the d3-celestial constellations*.json and mw.json.</param>
    public static Result Build(string catalogPath, string geoJsonDirectory, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = catalogPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();

        var (stars, named) = WriteStars(connection, outputDirectory);
        var dsos = WriteDsos(connection, outputDirectory);

        var constellations = ReadConstellations(Path.Combine(geoJsonDirectory, "constellations.json"));
        // Serpens has two label entries (Caput and Cauda); its lines and boundary use the first.
        var index = constellations.Select((c, i) => (c.Abbreviation, i))
            .GroupBy(p => p.Abbreviation, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().i, StringComparer.Ordinal);
        WriteConstellations(constellations, Path.Combine(outputDirectory, "constellation-labels.json"));

        var lines = WriteSegments(
            ReadGeometry(Path.Combine(geoJsonDirectory, "constellations.lines.json"), id => index[id], closeRings: false),
            Path.Combine(outputDirectory, "constellation-lines.bin"), densify: false);
        var bounds = WriteSegments(
            ReadGeometry(Path.Combine(geoJsonDirectory, "constellations.bounds.json"), id => index[id], closeRings: true),
            Path.Combine(outputDirectory, "constellation-bounds.bin"), densify: true);
        var milkyWay = WriteSegments(
            ReadGeometry(Path.Combine(geoJsonDirectory, "mw.json"), MilkyWayLevel, closeRings: true),
            Path.Combine(outputDirectory, "milkyway.bin"), densify: true);

        return new Result(stars, named, dsos, lines, bounds, milkyWay, constellations.Count);
    }

    private static (int Count, int Named) WriteStars(SqliteConnection connection, string outputDirectory)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT hyg_id, hip, proper_name, bayer, flamsteed, constellation, ra_hours, dec_degrees, magnitude, color_index
            FROM stars ORDER BY magnitude, hyg_id
            """;
        using var reader = command.ExecuteReader();

        var records = new List<byte[]>();
        var names = new SortedDictionary<int, string>();
        while (reader.Read())
        {
            var hygId = reader.GetInt32(0);
            var hip = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
            var vector = SkyFrame.FromEquatorial(new(reader.GetDouble(6), reader.GetDouble(7)));
            var record = new byte[StarRecordBytes];
            WriteVector(record, 0, vector);
            WriteFloat(record, 12, (float)reader.GetDouble(8));
            WriteFloat(record, 16, reader.IsDBNull(9) ? DefaultColorIndex : (float)reader.GetDouble(9));
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(20), (uint)hygId);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)hip);
            records.Add(record);

            var label = StarLabel(
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.GetString(5));
            if (label is not null)
            {
                names[hygId] = label;
            }
        }

        WriteBinary(Path.Combine(outputDirectory, "stars.bin"), StarRecordBytes, records);
        WriteJson(Path.Combine(outputDirectory, "star-names.json"), writer =>
        {
            writer.WriteStartObject();
            foreach (var (id, label) in names)
            {
                writer.WriteString(id.ToString(CultureInfo.InvariantCulture), label);
            }

            writer.WriteEndObject();
        });
        return (records.Count, names.Count);
    }

    /// <summary>"Sirius", "α CMa", "β¹ Cyg", "61 Cyg" or null (<see cref="StarNames.Label"/>).</summary>
    internal static string? StarLabel(string? properName, string? bayer, int? flamsteed, string constellation) =>
        StarNames.Label(properName, bayer, flamsteed, constellation);

    private static int WriteDsos(SqliteConnection connection, string outputDirectory)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, ra_hours, dec_degrees, magnitude, major_axis_arcmin, minor_axis_arcmin,
                   position_angle_degrees, common_name, constellation
            FROM objects ORDER BY id
            """;
        using var reader = command.ExecuteReader();

        var rows = new List<(string Id, string Type, string Name, string Constellation, byte[] Record)>();
        while (reader.Read())
        {
            var record = new byte[DsoRecordBytes];
            WriteVector(record, 0, SkyFrame.FromEquatorial(new(reader.GetDouble(2), reader.GetDouble(3))));
            WriteFloat(record, 12, reader.IsDBNull(4) ? 99f : (float)reader.GetDouble(4));
            WriteFloat(record, 16, reader.IsDBNull(5) ? 0f : (float)reader.GetDouble(5));
            WriteFloat(record, 20, reader.IsDBNull(6) ? (reader.IsDBNull(5) ? 0f : (float)reader.GetDouble(5)) : (float)reader.GetDouble(6));
            WriteFloat(record, 24, reader.IsDBNull(7) ? -1f : (float)reader.GetDouble(7));
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(8), reader.GetString(9), record));
        }

        var types = rows.Select(r => r.Type).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        foreach (var row in rows)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(row.Record.AsSpan(28), (uint)types.IndexOf(row.Type));
        }

        WriteBinary(Path.Combine(outputDirectory, "dsos.bin"), DsoRecordBytes, rows.Select(r => r.Record));
        WriteJson(Path.Combine(outputDirectory, "dsos.json"), writer =>
        {
            writer.WriteStartObject();
            writer.WriteStartArray("types");
            types.ForEach(writer.WriteStringValue);
            writer.WriteEndArray();
            writer.WriteStartArray("objects");
            foreach (var row in rows)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(row.Id);
                writer.WriteStringValue(row.Name);
                writer.WriteStringValue(row.Constellation);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
        return rows.Count;
    }

    internal sealed record ConstellationLabel(string Abbreviation, string Name, string Genitive, int Rank, double LongitudeDegrees, double LatitudeDegrees);

    private static List<ConstellationLabel> ReadConstellations(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return
        [
            .. document.RootElement.GetProperty("features").EnumerateArray().Select(feature =>
            {
                var properties = feature.GetProperty("properties");
                var point = feature.GetProperty("geometry").GetProperty("coordinates");
                return new ConstellationLabel(
                    feature.GetProperty("id").GetString()!,
                    properties.GetProperty("name").GetString()!,
                    properties.GetProperty("gen").GetString()!,
                    int.Parse(properties.GetProperty("rank").GetString()!, CultureInfo.InvariantCulture),
                    point[0].GetDouble(),
                    point[1].GetDouble());
            }).OrderBy(c => c.Abbreviation, StringComparer.Ordinal)
        ];
    }

    private static void WriteConstellations(List<ConstellationLabel> constellations, string path) =>
        WriteJson(path, writer =>
        {
            writer.WriteStartArray();
            foreach (var c in constellations)
            {
                var label = Vector3d.FromSpherical(c.LongitudeDegrees, c.LatitudeDegrees);
                writer.WriteStartObject();
                writer.WriteString("abbr", c.Abbreviation);
                writer.WriteString("name", c.Name);
                writer.WriteString("genitive", c.Genitive);
                writer.WriteNumber("rank", c.Rank);
                writer.WriteStartArray("label");
                writer.WriteNumberValue(Math.Round(label.X, 6));
                writer.WriteNumberValue(Math.Round(label.Y, 6));
                writer.WriteNumberValue(Math.Round(label.Z, 6));
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

    /// <summary>d3-celestial Milky Way features "ol1"…"ol5" (outer → inner density levels) → 0…4.</summary>
    private static int MilkyWayLevel(string id) =>
        id.StartsWith("ol", StringComparison.Ordinal) ? int.Parse(id.AsSpan(2), CultureInfo.InvariantCulture) - 1
        : throw new InvalidDataException($"Unexpected Milky Way feature '{id}'.");

    /// <summary>Polylines (lon/lat degree pairs) of every feature with its group index.</summary>
    internal static IEnumerable<(int Group, List<(double Lon, double Lat)> Points)> ReadGeometry(
        string path, Func<string, int> group, bool closeRings)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<(int, List<(double, double)>)>();
        foreach (var feature in document.RootElement.GetProperty("features").EnumerateArray())
        {
            var groupIndex = group(feature.GetProperty("id").GetString()!);
            var geometry = feature.GetProperty("geometry");
            var coordinates = geometry.GetProperty("coordinates");
            var lines = geometry.GetProperty("type").GetString() switch
            {
                "LineString" => [coordinates],
                "MultiLineString" or "Polygon" => coordinates.EnumerateArray().ToList(),
                "MultiPolygon" => coordinates.EnumerateArray().SelectMany(p => p.EnumerateArray()).ToList(),
                var type => throw new InvalidDataException($"Unsupported geometry {type} in {path}.")
            };

            foreach (var line in lines)
            {
                var points = line.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToList();
                if (closeRings && points.Count > 2 && points[0] != points[^1])
                {
                    points.Add(points[0]);
                }

                result.Add((groupIndex, points));
            }
        }

        return result;
    }

    private static int WriteSegments(IEnumerable<(int Group, List<(double Lon, double Lat)> Points)> polylines, string path, bool densify)
    {
        var records = new List<byte[]>();
        foreach (var (group, points) in polylines)
        {
            for (var i = 1; i < points.Count; i++)
            {
                var a = Vector3d.FromSpherical(points[i - 1].Lon, points[i - 1].Lat);
                var b = Vector3d.FromSpherical(points[i].Lon, points[i].Lat);
                var omega = Math.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1, 1));
                var angle = omega * 180 / Math.PI;
                if (angle < 1e-9)
                {
                    continue;
                }

                var pieces = densify ? Math.Max(1, (int)Math.Ceiling(angle / MaximumSegmentDegrees)) : 1;
                var previous = a;
                for (var piece = 1; piece <= pieces; piece++)
                {
                    // Spherical linear interpolation: equal pieces along the great circle.
                    var t = (double)piece / pieces;
                    var next = piece == pieces ? b : (a * Math.Sin((1 - t) * omega) + b * Math.Sin(t * omega)) * (1 / Math.Sin(omega));
                    var record = new byte[SegmentRecordBytes];
                    WriteVector(record, 0, previous);
                    WriteVector(record, 12, next);
                    BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(24), (uint)group);
                    records.Add(record);
                    previous = next;
                }
            }
        }

        WriteBinary(path, SegmentRecordBytes, records);
        return records.Count;
    }

    private static void WriteVector(byte[] record, int offset, Vector3d v)
    {
        WriteFloat(record, offset, (float)v.X);
        WriteFloat(record, offset + 4, (float)v.Y);
        WriteFloat(record, offset + 8, (float)v.Z);
    }

    private static void WriteFloat(byte[] record, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(record.AsSpan(offset), value);

    private static void WriteBinary(string path, int recordBytes, IEnumerable<byte[]> records)
    {
        var list = records.ToList();
        using var stream = File.Create(path);
        Span<byte> header = stackalloc byte[HeaderBytes];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)list.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], (uint)recordBytes);
        stream.Write(header);
        foreach (var record in list)
        {
            stream.Write(record);
        }
    }

    private static void WriteJson(string path, Action<Utf8JsonWriter> write)
    {
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, JsonOptions);
        write(writer);
    }

    /// <summary>Header of a binary sky file: (record count, record size), or null if the header is invalid.</summary>
    public static (int Count, int RecordBytes)? ReadHeader(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < HeaderBytes || !bytes.AsSpan(0, 4).SequenceEqual(Magic)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) != FormatVersion)
        {
            return null;
        }

        var count = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        return bytes.Length == HeaderBytes + (long)count * size ? (count, size) : null;
    }

    /// <summary>Sanity checks of the built sky files; returns the failures.</summary>
    public static IReadOnlyList<string> Verify(string directory, int minimumStars, int minimumDsos)
    {
        var failures = new List<string>();
        foreach (var file in OutputFiles)
        {
            if (!File.Exists(Path.Combine(directory, file)))
            {
                failures.Add($"sky/{file} is missing");
            }
        }

        if (failures.Count > 0)
        {
            return failures;
        }

        void Check(string file, int recordBytes, int minimum)
        {
            var header = ReadHeader(Path.Combine(directory, file));
            if (header is null)
            {
                failures.Add($"sky/{file}: bad header or length");
            }
            else if (header.Value.RecordBytes != recordBytes || header.Value.Count < minimum)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture,
                    $"sky/{file}: {header.Value.Count} records of {header.Value.RecordBytes} bytes, expected ≥ {minimum} of {recordBytes}"));
            }
        }

        Check("stars.bin", StarRecordBytes, minimumStars);
        Check("dsos.bin", DsoRecordBytes, minimumDsos);
        Check("constellation-lines.bin", SegmentRecordBytes, 1);
        Check("constellation-bounds.bin", SegmentRecordBytes, 1);
        Check("milkyway.bin", SegmentRecordBytes, 1);

        using var dsos = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "dsos.json")));
        var dsoHeader = ReadHeader(Path.Combine(directory, "dsos.bin"));
        if (dsoHeader is not null && dsos.RootElement.GetProperty("objects").GetArrayLength() != dsoHeader.Value.Count)
        {
            failures.Add("sky/dsos.json and dsos.bin disagree");
        }

        return failures;
    }
}
