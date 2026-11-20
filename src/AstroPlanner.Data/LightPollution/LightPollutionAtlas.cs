using AstroPlanner.Core.Interfaces;

namespace AstroPlanner.Data.LightPollution;

/// <summary>
/// The shipped light-pollution grids (World Atlas 2015, CC BY-NC 4.0, spec §7.2): a high-resolution
/// regional grid tried first, then the low-resolution world grid. Embedded in this assembly and
/// decompressed on first use.
/// </summary>
public sealed class LightPollutionAtlas : ILightPollutionAtlas
{
    /// <summary>Grid files, finest first (also the names in data/build/lp).</summary>
    public static readonly IReadOnlyList<string> FileNames = ["mexico-30s.bin.gz", "world-5m.bin.gz"];

    private const string ResourcePrefix = "LightPollution/";

    private readonly Lazy<IReadOnlyList<LightPollutionGrid>> _grids;

    public LightPollutionAtlas(IReadOnlyList<LightPollutionGrid> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        _grids = new Lazy<IReadOnlyList<LightPollutionGrid>>(grids);
    }

    private LightPollutionAtlas(Func<IReadOnlyList<LightPollutionGrid>> load)
    {
        _grids = new Lazy<IReadOnlyList<LightPollutionGrid>>(load, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The grids embedded in the application (none when the build had no grid files).</summary>
    public static LightPollutionAtlas FromEmbedded() => new(() =>
    {
        var assembly = typeof(LightPollutionAtlas).Assembly;
        var grids = new List<LightPollutionGrid>();
        foreach (var name in FileNames)
        {
            using var stream = assembly.GetManifestResourceStream(ResourcePrefix + name);
            if (stream is not null)
            {
                grids.Add(LightPollutionGrid.Read(stream));
            }
        }

        return grids;
    });

    /// <summary>The grids in a folder (data/build/lp), finest first; missing files are skipped.</summary>
    public static LightPollutionAtlas FromDirectory(string directory) => new(() =>
    {
        var grids = new List<LightPollutionGrid>();
        foreach (var name in FileNames)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                grids.Add(LightPollutionGrid.Read(stream));
            }
        }

        return grids;
    });

    /// <summary>True when at least one grid is available.</summary>
    public bool IsAvailable => _grids.Value.Count > 0;

    public double? ArtificialBrightness(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || !double.IsFinite(longitudeDegrees))
        {
            return null;
        }

        var longitude = ((longitudeDegrees + 180) % 360 + 360) % 360 - 180;
        foreach (var grid in _grids.Value)
        {
            if (grid.Sample(latitudeDegrees, longitude) is { } value)
            {
                return value;
            }
        }

        return null;
    }
}
