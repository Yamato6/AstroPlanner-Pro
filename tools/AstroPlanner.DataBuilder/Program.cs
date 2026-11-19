using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AstroPlanner.Data.Catalog;

namespace AstroPlanner.DataBuilder;

/// <summary>
/// Entry point. Commands:
///   download        fetch the pinned sources into data/raw (verifies SHA-256)
///   build-catalogs  data/raw + data/curated → data/build/catalogs.sqlite and data/build/sky/
///   verify          sanity checks of data/build/catalogs.sqlite
///   all             the three, in order
/// Option: --root &lt;repository root&gt; (default: the folder containing AstroPlanner.slnx).
/// </summary>
internal static class Program
{
    private const string UserAgent = "AstroPlannerPro-DataBuilder/1.0 (+https://github.com/Yamato6/AstroPlanner-Pro)";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = args.FirstOrDefault() ?? "help";
            var paths = RepositoryPaths.Find(Option(args, "--root"));

            switch (command)
            {
                case "download":
                    await DownloadAsync(paths).ConfigureAwait(false);
                    return 0;
                case "build-catalogs":
                    Build(paths);
                    return 0;
                case "verify":
                    return Verify(paths);
                case "all":
                    await DownloadAsync(paths).ConfigureAwait(false);
                    Build(paths);
                    return Verify(paths);
                default:
                    Console.WriteLine("Usage: AstroPlanner.DataBuilder download | build-catalogs | verify | all [--root <repo>]");
                    return command == "help" ? 0 : 2;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or JsonException)
        {
            await Console.Error.WriteLineAsync("Error: " + ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task DownloadAsync(RepositoryPaths paths)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);

        foreach (var source in DataSources.All)
        {
            var path = paths.Raw(source.RelativePath);
            if (File.Exists(path) && Sha256(path) == source.Sha256)
            {
                Console.WriteLine($"ok        {source.RelativePath}");
                continue;
            }

            Console.WriteLine($"download  {source.Url}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = await http.GetByteArrayAsync(source.Url).ConfigureAwait(false);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (hash != source.Sha256)
            {
                throw new InvalidDataException($"{source.RelativePath}: SHA-256 {hash} does not match the pinned {source.Sha256}.");
            }

            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        }
    }

    private static void Build(RepositoryPaths paths)
    {
        foreach (var source in DataSources.All)
        {
            var path = paths.Raw(source.RelativePath);
            if (!File.Exists(path) || Sha256(path) != source.Sha256)
            {
                throw new InvalidDataException($"{source.RelativePath} is missing or changed: run 'download' first.");
            }
        }

        var curation = LoadCuration(paths.Curated("messier.json"));
        var result = CatalogBuild.Run(
            OpenNgcRows(paths.Raw(DataSources.OpenNgc.RelativePath)).Concat(OpenNgcRows(paths.Raw(DataSources.OpenNgcAddendum.RelativePath))),
            HygRows(paths.Raw(DataSources.Hyg.RelativePath)),
            curation,
            paths.Build(CatalogSchema.FileName));

        foreach (var source in DataSources.D3Celestial)
        {
            var target = paths.Build(Path.Combine("sky", Path.GetFileName(source.RelativePath)));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(paths.Raw(source.RelativePath), target, overwrite: true);
        }

        File.WriteAllText(paths.Build("catalogs-report.txt"), result.Report, new UTF8Encoding(false));
        Console.WriteLine(result.Report);
    }

    private static int Verify(RepositoryPaths paths)
    {
        var failures = CatalogVerifier.Verify(paths.Build(CatalogSchema.FileName), CatalogVerifier.FullBuild);
        foreach (var failure in failures)
        {
            Console.WriteLine("FAIL " + failure);
        }

        Console.WriteLine(failures.Count == 0 ? "verify: ok" : $"verify: {failures.Count} failure(s)");
        return failures.Count == 0 ? 0 : 1;
    }

    internal static MessierCuration LoadCuration(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        static Dictionary<string, string> Map(JsonElement element) =>
            element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);

        return new MessierCuration(
            Map(document.RootElement.GetProperty("commonNames")),
            Map(document.RootElement.GetProperty("messierOverrides")));
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> OpenNgcRows(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        foreach (var row in Csv.Read(reader, ';'))
        {
            yield return row;
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, string>> HygRows(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        foreach (var row in Csv.Read(reader, ','))
        {
            yield return row;
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}

/// <summary>Builds the catalog from parsed rows; separated from IO so that tests can run it on small fixtures.</summary>
internal static class CatalogBuild
{
    public sealed record Result(OpenNgcImport OpenNgc, int StarCount, string Report);

    public static Result Run(
        IEnumerable<IReadOnlyDictionary<string, string>> openNgcRows,
        IEnumerable<IReadOnlyDictionary<string, string>> hygRows,
        MessierCuration curation,
        string outputPath)
    {
        var openNgc = OpenNgcImporter.Import(openNgcRows, curation);
        var stars = HygImporter.Import(hygRows);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema_version"] = CatalogSchema.Version.ToString(CultureInfo.InvariantCulture),
            ["openngc_version"] = DataSources.OpenNgc.Version,
            ["openngc_sha256"] = DataSources.OpenNgc.Sha256,
            ["openngc_addendum_sha256"] = DataSources.OpenNgcAddendum.Sha256,
            ["hyg_version"] = DataSources.Hyg.Version,
            ["hyg_sha256"] = DataSources.Hyg.Sha256,
            ["star_limiting_magnitude"] = HygImporter.DefaultLimitingMagnitude.ToString("0.0", CultureInfo.InvariantCulture),
            ["object_count"] = openNgc.Objects.Count.ToString(CultureInfo.InvariantCulture),
            ["star_count"] = stars.Count.ToString(CultureInfo.InvariantCulture)
        };

        CatalogWriter.Write(outputPath, openNgc.Objects, stars, metadata);
        return new Result(openNgc, stars.Count, Report(openNgc, stars.Count));
    }

    private static string Report(OpenNgcImport openNgc, int starCount)
    {
        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"# catalogs.sqlite build report (schema {CatalogSchema.Version})");
        report.AppendLine(CultureInfo.InvariantCulture, $"OpenNGC {DataSources.OpenNgc.Version}: {openNgc.Objects.Count} objects ({openNgc.DuplicateRows} duplicate rows folded, {openNgc.NonExistentRows} non-existent dropped)");
        report.AppendLine(CultureInfo.InvariantCulture, $"HYG {DataSources.Hyg.Version}: {starCount} stars to magnitude {HygImporter.DefaultLimitingMagnitude:0.0}");
        report.AppendLine();
        report.AppendLine("## Objects by type");
        foreach (var group in openNgc.Objects.GroupBy(o => o.Type).OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"{group.Key}: {group.Count()}");
        }

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"## Constellation differences with OpenNGC ({openNgc.ConstellationMismatches.Count}; the app uses the computed IAU boundary)");
        foreach (var mismatch in openNgc.ConstellationMismatches)
        {
            report.AppendLine(mismatch);
        }

        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"## Warnings ({openNgc.Warnings.Count})");
        foreach (var warning in openNgc.Warnings)
        {
            report.AppendLine(warning);
        }

        return report.ToString();
    }
}

/// <summary>Repository folders used by the builder.</summary>
internal sealed record RepositoryPaths(string Root)
{
    public static RepositoryPaths Find(string? root)
    {
        if (root is not null)
        {
            return new RepositoryPaths(Path.GetFullPath(root));
        }

        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AstroPlanner.slnx")))
            {
                return new RepositoryPaths(directory.FullName);
            }
        }

        throw new IOException("AstroPlanner.slnx not found: run inside the repository or pass --root.");
    }

    public string Raw(string relativePath) => Path.Combine(Root, "data", "raw", relativePath);
    public string Curated(string fileName) => Path.Combine(Root, "data", "curated", fileName);
    public string Build(string relativePath) => Path.Combine(Root, "data", "build", relativePath);
}
