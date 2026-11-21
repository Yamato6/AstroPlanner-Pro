using System.Globalization;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Tests;

/// <summary>HEALPix NESTED against astropy-healpix 2.0.1 (tests/fixtures/healpix/nested.csv).</summary>
public sealed class HealpixTests
{
    private static readonly IReadOnlyList<Row> Rows = Load();

    private sealed record Row(int Order, double Lon, double Lat, long Nested, double Dx, double Dy, double CenterLon, double CenterLat);

    private static List<Row> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "healpix", "nested.csv");
        return [.. File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split(',')).Select(p => new Row(
            int.Parse(p[0], CultureInfo.InvariantCulture), D(p[1]), D(p[2]), long.Parse(p[3], CultureInfo.InvariantCulture),
            D(p[4]), D(p[5]), D(p[6]), D(p[7])))];

        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void Fixture_CoversEveryOrder() =>
        Rows.Select(r => r.Order).Distinct().Should().BeEquivalentTo([0, 1, 3, 5, 9, 12, 15, 18]);

    [Fact]
    public void ToNested_MatchesAstropyHealpix()
    {
        // Points exactly on a pixel edge (poles, face corners) may fall on either side by rounding;
        // they are excluded when the reference offset is within 1e-9 of an edge.
        var mismatches = Rows
            .Where(r => !OnEdge(r))
            .Where(r => Healpix.ToNested(r.Order, r.Lon, r.Lat) != r.Nested)
            .Select(r => $"order {r.Order} ({r.Lon}, {r.Lat}): {Healpix.ToNested(r.Order, r.Lon, r.Lat)} vs {r.Nested}")
            .ToList();

        mismatches.Should().BeEmpty();
        Rows.Count(r => !OnEdge(r)).Should().BeGreaterThan(650, "only the poles and exact face corners are excluded");
    }

    [Fact]
    public void FacePoint_OffsetsInsideThePixel_MatchAstropyHealpix()
    {
        foreach (var row in Rows.Where(r => !OnEdge(r) && r.Order <= 15))
        {
            var point = Healpix.ToFacePoint(row.Order, row.Lon, row.Lat);
            (point.X - Math.Floor(point.X)).Should().BeApproximately(row.Dx, 1e-6, $"dx at order {row.Order} ({row.Lon}, {row.Lat})");
            (point.Y - Math.Floor(point.Y)).Should().BeApproximately(row.Dy, 1e-6, $"dy at order {row.Order} ({row.Lon}, {row.Lat})");
        }
    }

    [Fact]
    public void Center_MatchesAstropyHealpix()
    {
        foreach (var row in Rows)
        {
            var (lon, lat) = Healpix.Center(row.Order, row.Nested);
            lat.Should().BeApproximately(row.CenterLat, 1e-9, $"order {row.Order} pixel {row.Nested}");
            AngleDifference(lon, row.CenterLon).Should().BeLessThan(1e-9, $"order {row.Order} pixel {row.Nested}");
        }
    }

    [Theory]
    [InlineData(83.8221, -5.3911)]
    [InlineData(10.6847, 41.269)]
    [InlineData(37.9545, 89.2641)]
    [InlineData(95.988, -52.6957)]
    [InlineData(359.99, 0.5)]
    public void FromFacePoint_InvertsToFacePoint(double lon, double lat)
    {
        foreach (var order in new[] { 0, 6, 18 })
        {
            var (backLon, backLat) = Healpix.FromFacePoint(order, Healpix.ToFacePoint(order, lon, lat));
            backLat.Should().BeApproximately(lat, 1e-9);
            AngleDifference(backLon, lon).Should().BeLessThan(1e-8);
        }
    }

    [Fact]
    public void NestedAndFaceXy_RoundTrip()
    {
        foreach (var nested in new long[] { 0, 1, 2, 3, 12345, 3 * 4096 * 4096 + 777, 12L * (1L << 36) - 1 })
        {
            var order = nested < 12 * 4096L * 4096 ? 12 : 18;
            var (face, x, y) = Healpix.NestedToFaceXy(order, nested);
            Healpix.FaceXyToNested(order, face, x, y).Should().Be(nested);
        }

        Healpix.FaceXyToNested(1, 0, 1, 0).Should().Be(1, "x is in the even bits");
        Healpix.FaceXyToNested(1, 0, 0, 1).Should().Be(2, "y is in the odd bits");
    }

    [Fact]
    public void Sizes_AndRanges()
    {
        Healpix.PixelCount(0).Should().Be(12);
        Healpix.PixelCount(9).Should().Be(3_145_728);
        Healpix.PixelSizeDegrees(9).Should().BeApproximately(0.1145, 1e-4);
        FluentActions.Invoking(() => Healpix.Nside(30)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Healpix.NestedToFaceXy(0, 12)).Should().Throw<ArgumentOutOfRangeException>();
    }

    private static bool OnEdge(Row row) =>
        row.Dx < 1e-9 || row.Dx > 1 - 1e-9 || row.Dy < 1e-9 || row.Dy > 1 - 1e-9 || Math.Abs(row.Lat) > 89.9999;

    private static double AngleDifference(double a, double b)
    {
        var d = Math.Abs(a - b) % 360;
        return Math.Min(d, 360 - d);
    }
}
