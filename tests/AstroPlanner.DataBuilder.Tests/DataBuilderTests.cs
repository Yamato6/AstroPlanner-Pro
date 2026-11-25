using System.Security.Cryptography;
using AstroPlanner.Core.Models;
using Microsoft.Data.Sqlite;

namespace AstroPlanner.DataBuilder.Tests;

public sealed class DesignationTests
{
    [Theory]
    [InlineData("NGC0224", "NGC 224", "NGC")]
    [InlineData("IC0011", "IC 11", "IC")]
    [InlineData("IC0080 NED01", "IC 80 NED01", "IC")]
    [InlineData("IC0186A", "IC 186A", "IC")]
    [InlineData("ESO056-115", "ESO 56-115", "ESO")]
    [InlineData("M040", "M40", "M")]
    [InlineData("C 001", "C1", "C")]
    [InlineData("C009", "C9", "C")]
    [InlineData("Mel022", "Mel 22", "Mel")]
    [InlineData("PGC 000778", "PGC 778", "PGC")]
    [InlineData("SH 2-155", "Sh2-155", "Sh2")]
    [InlineData("PN G138.8+02.8", "PN G138.8+02.8", "PN")]
    public void Format_NormalizesCatalogNames(string raw, string expected, string catalog)
    {
        Designations.Format(raw).Should().Be(new Designation(expected, catalog));
    }

    [Theory]
    [InlineData("2MASX J00424433+4116074")]
    [InlineData("SDSS J001206.08-002454.7")]
    [InlineData("MCG -02-01-031")]
    [InlineData("IRAS 00084-1306")]
    [InlineData("HD")]
    [InlineData("50 Cas")]
    public void SurveyAndMalformedIds_AreNotKept(string raw)
    {
        Designations.IsKept(Designations.Format(raw)).Should().BeFalse();
    }

    [Fact]
    public void Csv_Split_HandlesQuotesAndEscapes()
    {
        Csv.Split("a;\"b;c\";\"d \"\"e\"\"\";", ';').Should().Equal("a", "b;c", "d \"e\"", "");
    }

    [Theory]
    [InlineData("00:42:44.35", 0.7123194)]
    [InlineData("23:59:60.00", 0.0)]
    public void ParseRightAscension(string value, double hours)
    {
        OpenNgcImporter.ParseRightAscension(value).Should().BeApproximately(hours, 1e-6);
    }

    [Theory]
    [InlineData("+41:16:08.6", 41.269056)]
    [InlineData("-05:23:22.8", -5.3896667)]
    [InlineData("-00:30:00", -0.5)]
    public void ParseDeclination(string value, double degrees)
    {
        OpenNgcImporter.ParseDeclination(value).Should().BeApproximately(degrees, 1e-6);
    }
}

public sealed class OpenNgcImporterTests
{
    private static readonly OpenNgcImport Import = OpenNgcImporter.Import(Fixtures.OpenNgcRows(), Fixtures.Curation);

    private static CatalogEntry Object(string id) => Import.Objects.Single(o => o.Id == id);

    private static IEnumerable<string> DesignationsOf(string id) => Object(id).Designations.Select(d => d.Value);

    [Fact]
    public void Messier_IsThePrimaryId_WithTheNgcAsCrossId()
    {
        var m31 = Object("M31");

        m31.Type.Should().Be(CelestialObjectType.Galaxy);
        m31.Constellation.Should().Be("And");
        m31.Magnitude.Should().Be(3.44);
        m31.MagnitudeBand.Should().Be("V");
        m31.MajorAxisArcmin.Should().Be(177.83);
        m31.SurfaceBrightness.Should().Be(23.63);
        m31.CommonName.Should().Be("Andromeda Galaxy");
        DesignationsOf("M31").Should().Contain("NGC 224").And.Contain("PGC 2557").And.NotContain(d => d.StartsWith("2MASX", StringComparison.Ordinal));
    }

    [Fact]
    public void CuratedName_ComesFirst_AndOpenNgcNamesBecomeAlternates()
    {
        var m42 = Object("M42");

        m42.CommonName.Should().Be("Orion Nebula");
        m42.AlternateNames.Should().Contain("Great Orion Nebula");
        m42.Type.Should().Be(CelestialObjectType.ClusterWithNebula);
    }

    [Fact]
    public void Addendum_MessierAndCaldwellOnlyObjects()
    {
        Object("M40").Type.Should().Be(CelestialObjectType.DoubleStar);
        Object("M45").CommonName.Should().Be("Pleiades");
        DesignationsOf("M45").Should().Contain("Mel 22");
        Object("C9").CommonName.Should().Be("Cave Nebula");
        DesignationsOf("C9").Should().Contain("Sh2-155").And.Contain("LBN 529");
        Object("C14").Type.Should().Be(CelestialObjectType.StellarAssociation);
        DesignationsOf("NGC 188").Should().Contain("C1");
    }

    [Fact]
    public void M102_FollowsTheCuratedIdentificationWithNgc5866()
    {
        Object("M102").CommonName.Should().Be("Spindle Galaxy");
        DesignationsOf("M102").Should().Contain("NGC 5866");
        Import.Objects.Should().NotContain(o => o.Id == "M101" && o.Designations.Any(d => d.Value == "M102"));
    }

    [Fact]
    public void DuplicateRows_AreFoldedIntoTheirTarget_AndNonExistentRowsDropped()
    {
        DesignationsOf("NGC 281").Should().Contain("IC 11");
        Import.Objects.Should().NotContain(o => o.Id == "IC 11" || o.Id == "IC 67");
        Import.DuplicateRows.Should().Be(2);
        Import.NonExistentRows.Should().Be(1);
    }

    [Fact]
    public void CrossIdThatIsAnotherObjectsId_StaysWithItsOwner()
    {
        // OpenNGC lists NGC 6839 as a cross-id of M71 (NGC 6838), but NGC 6839 is a row of its own.
        DesignationsOf("M71").Should().Contain("NGC 6838").And.NotContain("NGC 6839");
        Object("NGC 6839").Type.Should().Be(CelestialObjectType.Unknown);
        Import.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void SerpensHalves_AreNotReportedAsConstellationDifferences()
    {
        Object("NGC 5910").Constellation.Should().Be("Ser");
        Import.ConstellationMismatches.Should().BeEmpty();
    }

    [Fact]
    public void UnknownType_Throws()
    {
        var row = Fixtures.OpenNgcRows().First().ToDictionary(p => p.Key, p => p.Value);
        row["Type"] = "Blob";

        var act = () => OpenNgcImporter.Import([row], Fixtures.Curation);

        act.Should().Throw<InvalidDataException>();
    }
}

public sealed class HygImporterTests
{
    [Fact]
    public void Import_SkipsTheSunAndStarsFainterThanTheLimit()
    {
        var stars = HygImporter.Import(Fixtures.HygRows());

        stars.Select(s => s.HygId).Should().Equal(11734, 32263, 49998, 90979);
    }

    [Fact]
    public void Import_KeepsIdentifiersAndComputesTheConstellation()
    {
        var polaris = HygImporter.Import(Fixtures.HygRows()).Single(s => s.ProperName == "Polaris");

        polaris.Hip.Should().Be(11767);
        polaris.Hd.Should().Be(8890);
        polaris.Hr.Should().Be(424);
        polaris.Bayer.Should().Be("Alp");
        polaris.Flamsteed.Should().Be(1);
        polaris.Constellation.Should().Be("UMi");
        polaris.Magnitude.Should().Be(1.97);
        polaris.ColorIndex.Should().Be(0.636);
    }

    [Fact]
    public void Import_KeepsSpectralTypeDistanceVariabilityAndMultiplicity()
    {
        var stars = HygImporter.Import(Fixtures.HygRows());
        var sirius = stars.Single(s => s.ProperName == "Sirius");
        var vega = stars.Single(s => s.ProperName == "Vega");

        sirius.SpectralType.Should().Be("A0m...");
        sirius.DistanceParsecs.Should().Be(2.6371);
        sirius.MultipleSystem.Should().Be("Gl 244");
        sirius.Component.Should().Be(1);
        sirius.ComponentPrimary.Should().Be(sirius.HygId);
        vega.SpectralType.Should().Be("A0Vvar");
        stars.Should().OnlyContain(s => s.DistanceParsecs == null || s.DistanceParsecs < 100_000);
    }

    [Fact]
    public void Import_HonoursTheLimitingMagnitude()
    {
        HygImporter.Import(Fixtures.HygRows(), limitingMagnitude: 2).Select(s => s.ProperName)
            .Should().Equal("Polaris", "Sirius", "Vega");
    }
}

public sealed class WdsImporterTests
{
    [Fact]
    public void Parse_ReadsTheFixedColumns()
    {
        var albireo = Fixtures.WdsLines().Select(WdsImporter.Parse).OfType<WdsImporter.WdsPair>().Single(p => p.Discoverer == "STFA 43");

        albireo.Should().BeEquivalentTo(new
        {
            WdsId = "19307+2758",
            Components = "AB",
            FirstYear = 1755,
            LastYear = 2025,
            PositionAngleDegrees = 54.0,
            SeparationArcsec = 34.3,
            PrimaryMagnitude = 3.19,
            SecondaryMagnitude = 4.68,
            SpectralType = "K3II+B9.5"
        }, o => o.ExcludingMissingMembers());
        albireo.RightAscensionHours.Should().BeApproximately(19 + (30 / 60.0) + (43.29 / 3600), 1e-9);
        albireo.DeclinationDegrees.Should().BeApproximately(27 + (57 / 60.0) + (34.9 / 3600), 1e-9);
        WdsImporter.Parse("garbage line").Should().BeNull();
        WdsImporter.Parse("<pre>").Should().BeNull();
    }

    [Fact]
    public void Import_KeepsSplittablePairs_AndMatchesThemToHygStars()
    {
        var stars = HygImporter.Import(Fixtures.HygRows());

        var (pairs, selected, unmatched) = WdsImporter.Import(Fixtures.WdsLines(), stars);

        // Polaris Aa,Ab (0.1") is too close and Sirius Ba,Bb has a faint primary; Albireo and eps Lyr are not in the HYG excerpt.
        selected.Should().Be(4);
        unmatched.Should().Be(2);
        pairs.Select(p => (p.Discoverer, p.HygId)).Should().Equal(("STF  93", 11734), ("AGC   1", 32263));
        pairs[1].Should().BeEquivalentTo(new { Components = "AB", SeparationArcsec = 11.1, SecondaryMagnitude = 8.44, PositionAngleDegrees = 59.0 },
            o => o.ExcludingMissingMembers());
    }
}

public sealed class CatalogBuildTests : IDisposable
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

    private string Build(string name)
    {
        var path = Path.Combine(_directory, name);
        CatalogBuild.Run(Fixtures.OpenNgcRows(), Fixtures.HygRows(), Fixtures.Curation, path, Fixtures.WdsLines());
        return path;
    }

    [Fact]
    public void Build_WritesADatabaseThatPassesTheBasicChecks()
    {
        var path = Build("catalogs.sqlite");

        var failures = CatalogVerifier.Verify(path, new CatalogVerifier.Expectations(
            MinimumObjects: 14, MinimumStars: 4, MessierCount: 7, CaldwellCount: 3, SpotChecks: false, MinimumDoubleStarPairs: 2));

        failures.Should().BeEmpty();
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var first = Build("first.sqlite");
        var second = Build("second.sqlite");

        Fixtures.Sha256(first).Should().Be(Fixtures.Sha256(second));
    }

    [Fact]
    public void Build_ReportsCountsAndDifferences()
    {
        var result = CatalogBuild.Run(Fixtures.OpenNgcRows(), Fixtures.HygRows(), Fixtures.Curation, Path.Combine(_directory, "r.sqlite"),
            Fixtures.WdsLines());

        result.StarCount.Should().Be(4);
        result.DoubleStarPairs.Should().Be(2);
        result.Report.Should().Contain("WDS 2026-10-29: 4 pairs selected").And.Contain("2 matched to 2 HYG stars, 2 unmatched");
        result.Report.Should().Contain("Galaxy: 3").And.Contain("Constellation differences with OpenNGC (0");
    }

}

internal static class Fixtures
{
    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Path(params string[] parts) => System.IO.Path.Combine([AppContext.BaseDirectory, "fixtures", .. parts]);

    public static MessierCuration Curation { get; } = Program.LoadCuration(Path("curated", "messier.json"));

    public static List<IReadOnlyDictionary<string, string>> OpenNgcRows()
    {
        using var reader = new StreamReader(Path("openngc", "sample.csv"));
        return [.. Csv.Read(reader, ';')];
    }

    public static IReadOnlyList<string> WdsLines() => File.ReadAllLines(Path("wds", "sample.txt"));

    public static List<IReadOnlyDictionary<string, string>> HygRows()
    {
        using var reader = new StreamReader(Path("hyg", "sample.csv"));
        return [.. Csv.Read(reader, ',')];
    }
}
