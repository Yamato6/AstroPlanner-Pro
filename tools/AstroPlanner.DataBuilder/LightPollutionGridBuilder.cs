using System.Globalization;
using System.Text;
using AstroPlanner.Data.LightPollution;

namespace AstroPlanner.DataBuilder;

/// <summary>A rectangular area kept at the GeoTIFF's native resolution.</summary>
internal sealed record LightPollutionRegion(string FileName, double NorthDegrees, double SouthDegrees, double WestDegrees, double EastDegrees);

/// <summary>A place whose artificial brightness <c>verify</c> checks to lie in a range (mcd/m²).</summary>
internal sealed record LightPollutionCheckpoint(string Name, double LatitudeDegrees, double LongitudeDegrees, double Minimum, double Maximum);

/// <summary>
/// <c>build-lp-grid</c> (spec §7.2): turns the World Atlas 2015 GeoTIFF (artificial zenith
/// brightness, mcd/m², 30″) into the compact grids the app ships — a regional grid at the native
/// 30″ and a world grid averaged (in linear units) over blocks of <c>factor</c> × <c>factor</c>
/// cells — in a single streaming pass over the 3 GB file.
/// </summary>
internal static class LightPollutionGridBuilder
{
    /// <summary>Mexico at 30″ (spec §7.2: lat 14–33°N, lon 86–118°W).</summary>
    public static readonly LightPollutionRegion Mexico = new(LightPollutionAtlas.FileNames[0], 33, 14, -118, -86);

    public static readonly string WorldFileName = LightPollutionAtlas.FileNames[1];

    /// <summary>30″ × 10 = 5′ world cells.</summary>
    public const int WorldFactor = 10;

    /// <summary>Sanity checks of the full build (values from the atlas itself, with generous margins).</summary>
    public static readonly IReadOnlyList<LightPollutionCheckpoint> FullBuildCheckpoints =
    [
        new("Aguascalientes (city centre)", 21.8818, -102.2916, 0.5, 6),
        new("Mexico City (centre)", 19.4326, -99.1332, 1.5, 15),
        new("San Pedro Mártir observatory", 31.0, -115.46, 0, 0.01),
        new("Pacific Ocean", 20.0, -110.0, 0, 0.001),
        new("New York (world grid)", 40.758, -73.9855, 3, 60),
        new("Cerro Tololo region (world grid)", -30.17, -70.80, 0, 0.05)
    ];

    public sealed record Result(string Report);

    public static Result Build(string tiffPath, string outputDirectory, LightPollutionRegion region, string worldFileName, int worldFactor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(worldFactor, 1);
        using var raster = GeoTiffRaster.Open(tiffPath);
        var cell = raster.CellDegrees;

        // Regional window in native cells (whole cells covering the requested area).
        var rowStart = Math.Clamp((int)Math.Floor((raster.NorthDegrees - region.NorthDegrees) / cell + 1e-9), 0, raster.Height);
        var rowEnd = Math.Clamp((int)Math.Ceiling((raster.NorthDegrees - region.SouthDegrees) / cell - 1e-9), 0, raster.Height);
        var colStart = Math.Clamp((int)Math.Floor((region.WestDegrees - raster.WestDegrees) / cell + 1e-9), 0, raster.Width);
        var colEnd = Math.Clamp((int)Math.Ceiling((region.EastDegrees - raster.WestDegrees) / cell - 1e-9), 0, raster.Width);
        var regionRows = rowEnd - rowStart;
        var regionColumns = colEnd - colStart;
        if (regionRows <= 0 || regionColumns <= 0)
        {
            throw new InvalidDataException($"{region.FileName}: the region lies outside the GeoTIFF.");
        }

        var regionCells = new byte[regionRows * regionColumns];

        var worldRows = (raster.Height + worldFactor - 1) / worldFactor;
        var worldColumns = (raster.Width + worldFactor - 1) / worldFactor;
        var sums = new double[worldRows * worldColumns];
        var counts = new int[worldRows * worldColumns];

        var band = new float[raster.TileHeight * raster.Width];
        for (var tileRow = 0; tileRow < raster.TilesDown; tileRow++)
        {
            var rows = raster.ReadTileRow(tileRow, band);
            for (var y = 0; y < rows; y++)
            {
                var row = tileRow * raster.TileHeight + y;
                var line = band.AsSpan(y * raster.Width, raster.Width);
                var worldOffset = row / worldFactor * worldColumns;
                for (var x = 0; x < line.Length; x++)
                {
                    var value = line[x] > 0 ? line[x] : 0f;   // no-data (−3.4e38) and NaN count as dark
                    sums[worldOffset + x / worldFactor] += value;
                    counts[worldOffset + x / worldFactor]++;
                }

                if (row >= rowStart && row < rowEnd)
                {
                    var target = regionCells.AsSpan((row - rowStart) * regionColumns, regionColumns);
                    for (var x = 0; x < regionColumns; x++)
                    {
                        target[x] = LightPollutionGrid.Encode(line[colStart + x]);
                    }
                }
            }
        }

        var worldCells = new byte[sums.Length];
        for (var i = 0; i < sums.Length; i++)
        {
            worldCells[i] = LightPollutionGrid.Encode(counts[i] > 0 ? sums[i] / counts[i] : 0);
        }

        var regional = new LightPollutionGrid(regionRows, regionColumns,
            raster.NorthDegrees - rowStart * cell, raster.WestDegrees + colStart * cell, cell, regionCells);
        var world = new LightPollutionGrid(worldRows, worldColumns,
            raster.NorthDegrees, raster.WestDegrees, cell * worldFactor, worldCells);

        Directory.CreateDirectory(outputDirectory);
        var report = new StringBuilder();
        report.AppendLine("# light-pollution grids (World Atlas 2015, artificial zenith brightness, mcd/m²)");
        report.AppendLine(CultureInfo.InvariantCulture, $"source: {raster.Width} × {raster.Height} cells of {cell * 3600:0.##}″, north {raster.NorthDegrees:0.######}°, west {raster.WestDegrees:0.######}°");
        foreach (var (name, grid) in new[] { (region.FileName, regional), (worldFileName, world) })
        {
            var path = Path.Combine(outputDirectory, name);
            using (var file = File.Create(path))
            {
                grid.Write(file);
            }

            report.AppendLine(CultureInfo.InvariantCulture,
                $"{name}: {grid.Rows} × {grid.Columns} cells of {grid.CellDegrees * 3600:0.##}″, N {grid.NorthDegrees:0.####} S {grid.SouthDegrees:0.####} W {grid.WestDegrees:0.####} E {grid.EastDegrees:0.####}, {new FileInfo(path).Length:N0} bytes");
        }

        return new Result(report.ToString());
    }

    /// <summary>Checks that the grids exist, parse, and give plausible values at the checkpoints.</summary>
    public static IReadOnlyList<string> Verify(string directory, IReadOnlyList<LightPollutionCheckpoint> checkpoints)
    {
        var failures = new List<string>();
        foreach (var name in LightPollutionAtlas.FileNames)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path))
            {
                failures.Add($"lp/{name} is missing (run build-lp-grid)");
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                LightPollutionGrid.Read(stream);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                failures.Add($"lp/{name}: {ex.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return failures;
        }

        var atlas = LightPollutionAtlas.FromDirectory(directory);
        foreach (var check in checkpoints)
        {
            var value = atlas.ArtificialBrightness(check.LatitudeDegrees, check.LongitudeDegrees);
            if (value is not { } v || v < check.Minimum || v > check.Maximum)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture,
                    $"lp: {check.Name} = {value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "none"} mcd/m², expected {check.Minimum}–{check.Maximum}"));
            }
        }

        return failures;
    }
}
