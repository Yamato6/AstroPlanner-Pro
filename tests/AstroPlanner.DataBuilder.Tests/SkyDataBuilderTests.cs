using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.DataBuilder.Tests;

public sealed class SkyDataBuilderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "AstroPlannerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static string GeoJson => Path.Combine(AppContext.BaseDirectory, "fixtures", "sky");

    private (SkyDataBuilder.Result Result, string Output) Build(string name = "out")
    {
        var catalog = Path.Combine(_directory, name + ".sqlite");
        CatalogBuild.Run(Fixtures.OpenNgcRows(), Fixtures.HygRows(), Fixtures.Curation, catalog);
        var output = Path.Combine(_directory, name);
        return (SkyDataBuilder.Build(catalog, GeoJson, output), output);
    }

    private static List<byte[]> Records(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var header = SkyDataBuilder.ReadHeader(path)!.Value;
        return [.. Enumerable.Range(0, header.Count).Select(i => bytes.AsSpan(16 + i * header.RecordBytes, header.RecordBytes).ToArray())];
    }

    private static float Float(byte[] record, int offset) => BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(offset));

    private static uint UInt(byte[] record, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(offset));

    [Fact]
    public void Build_WritesEveryFile_AndPassesVerification()
    {
        var (result, output) = Build();

        result.Stars.Should().Be(4);
        result.Dsos.Should().BeGreaterThan(10);
        result.Constellations.Should().Be(4);
        result.LineSegments.Should().Be(4);
        result.MilkyWaySegments.Should().BeGreaterThan(6); // 6 edges, split into ≤ 2° pieces
        result.Report.Should().Contain("4 stars (3 named)");
        SkyDataBuilder.Verify(output, minimumStars: 4, minimumDsos: 10).Should().BeEmpty();
    }

    [Fact]
    public void Stars_AreSortedByMagnitude_WithUnitVectorsAndIds()
    {
        var (_, output) = Build();

        var stars = Records(Path.Combine(output, "stars.bin"));

        stars.Select(r => UInt(r, 20)).Should().Equal(32263u, 90979u, 11734u, 49998u); // Sirius, Vega, Polaris, …
        stars.Select(r => Float(r, 12)).Should().BeInAscendingOrder();
        var sirius = stars[0];
        UInt(sirius, 24).Should().Be(32349u); // HIP
        Float(sirius, 16).Should().BeApproximately(0.009f, 1e-6f);
        var length = Math.Sqrt(Float(sirius, 0) * Float(sirius, 0) + Float(sirius, 4) * Float(sirius, 4) + Float(sirius, 8) * Float(sirius, 8));
        length.Should().BeApproximately(1, 1e-6);
        Math.Asin(Float(sirius, 8)).Should().BeApproximately(-16.716116 * Math.PI / 180, 1e-6);

        using var names = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "star-names.json")));
        names.RootElement.GetProperty("32263").GetString().Should().Be("Sirius");
        names.RootElement.TryGetProperty("49998", out _).Should().BeFalse();
    }

    [Fact]
    public void Dsos_MatchTheirJsonIndex()
    {
        var (_, output) = Build();

        var records = Records(Path.Combine(output, "dsos.bin"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "dsos.json")));
        var objects = json.RootElement.GetProperty("objects").EnumerateArray().ToList();
        var types = json.RootElement.GetProperty("types").EnumerateArray().Select(t => t.GetString()).ToList();

        objects.Should().HaveCount(records.Count);
        var m31 = objects.FindIndex(o => o[0].GetString() == "M31");
        m31.Should().BeGreaterThanOrEqualTo(0);
        objects[m31][1].GetString().Should().Be("Andromeda Galaxy");
        types[(int)UInt(records[m31], 28)].Should().Be("Galaxy");
        Float(records[m31], 16).Should().BeGreaterThan(100); // major axis, arcmin
    }

    [Fact]
    public void Segments_AreDensified_AndKeepTheirGroups()
    {
        var (_, output) = Build();

        var bounds = Records(Path.Combine(output, "constellation-bounds.bin"));
        bounds.Count.Should().BeGreaterThan(4);
        foreach (var segment in bounds)
        {
            var dot = Float(segment, 0) * Float(segment, 12) + Float(segment, 4) * Float(segment, 16) + Float(segment, 8) * Float(segment, 20);
            (Math.Acos(Math.Min(1, dot)) * 180 / Math.PI).Should().BeLessThanOrEqualTo(2.0001);
        }

        using var labels = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "constellation-labels.json")));
        var abbreviations = labels.RootElement.EnumerateArray().Select(l => l.GetProperty("abbr").GetString()).ToList();
        abbreviations.Should().Equal("And", "Ori", "Ser", "Ser");
        bounds.Select(r => UInt(r, 24)).Should().AllBeEquivalentTo(0u);                       // And
        Records(Path.Combine(output, "constellation-lines.bin")).Select(r => UInt(r, 24)).Distinct().Should().Equal(1u, 2u); // Ori, Ser
        Records(Path.Combine(output, "milkyway.bin")).Select(r => UInt(r, 24)).Distinct().Should().Equal(0u, 1u);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var (_, a) = Build("a");
        var (_, b) = Build("b");

        foreach (var file in SkyDataBuilder.OutputFiles)
        {
            Fixtures.Sha256(Path.Combine(a, file)).Should().Be(Fixtures.Sha256(Path.Combine(b, file)), file);
        }
    }

    [Fact]
    public void Verify_ReportsMissingAndCorruptFiles()
    {
        var (_, output) = Build();
        File.Delete(Path.Combine(output, "milkyway.bin"));
        SkyDataBuilder.Verify(output, 1, 1).Should().ContainSingle(f => f.Contains("milkyway.bin is missing"));

        Build("again");
        var again = Path.Combine(_directory, "again");
        File.WriteAllBytes(Path.Combine(again, "stars.bin"), [1, 2, 3]);
        SkyDataBuilder.Verify(again, 1, 1).Should().Contain(f => f.Contains("stars.bin: bad header"));
        SkyDataBuilder.Verify(again, 1, 1_000_000).Should().Contain(f => f.Contains("dsos.bin"));
    }

    [Theory]
    [InlineData("Sirius", "Alp", 9, "CMa", "Sirius")]
    [InlineData(null, "Alp", 9, "CMa", "α CMa")]
    [InlineData(null, "Bet-1", null, "Cyg", "β¹ Cyg")]
    [InlineData(null, "p", null, "Pup", "p Pup")]
    [InlineData(null, null, 61, "Cyg", "61 Cyg")]
    [InlineData(null, null, null, "Cyg", null)]
    public void StarLabel_PrefersProperNames_ThenBayer_ThenFlamsteed(string? proper, string? bayer, int? flamsteed, string constellation, string? expected) =>
        SkyDataBuilder.StarLabel(proper, bayer, flamsteed, constellation).Should().Be(expected);
}
