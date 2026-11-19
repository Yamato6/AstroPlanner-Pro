using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;

namespace AstroPlanner.UI.Sky;

/// <summary>Folders the WebView2 virtual hosts map to.</summary>
/// <param name="AppDirectory">The built renderer (index.html + assets), served as https://app.astroplanner/.</param>
/// <param name="DataDirectory">The static sky data, served as https://data.astroplanner/.</param>
public sealed record SkyAssetFolders(string AppDirectory, string DataDirectory);

/// <summary>
/// Extracts the renderer and its data, embedded in the exe as one zip by the UI build, to
/// <c>%LocalAppData%\AstroPlannerPro\sky\&lt;hash&gt;</c>. The folder name is the content hash, so
/// an update extracts a fresh copy and older copies are removed. Same pattern as the catalog
/// database (SqliteCatalogRepository).
/// </summary>
public static class SkyAssets
{
    public const string ResourceName = "AstroPlanner.UI.Sky.sky-assets.zip";

    /// <summary>The embedded zip, or null when the build had no Node.js (renderer not built).</summary>
    public static Stream? OpenEmbedded() =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

    /// <summary>
    /// Extracts <paramref name="zip"/> under <paramref name="rootDirectory"/> (once per content
    /// hash) and returns the two folders, or null when the archive has no renderer.
    /// </summary>
    public static SkyAssetFolders? Extract(Stream zip, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(zip);

        using var buffer = new MemoryStream();
        zip.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes))[..16];
        var target = Path.Combine(rootDirectory, hash);
        var folders = new SkyAssetFolders(Path.Combine(target, "app"), Path.Combine(target, "data"));

        if (!File.Exists(Path.Combine(folders.AppDirectory, "index.html")))
        {
            Directory.CreateDirectory(rootDirectory);
            var staging = Path.Combine(rootDirectory, hash + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(staging);
                }

                if (!File.Exists(Path.Combine(staging, "app", "index.html")))
                {
                    return null;
                }

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                Directory.Move(staging, target);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }

        RemoveOtherCopies(rootDirectory, hash);
        return folders;
    }

    private static void RemoveOtherCopies(string rootDirectory, string keep)
    {
        foreach (var directory in Directory.EnumerateDirectories(rootDirectory))
        {
            if (!string.Equals(Path.GetFileName(directory), keep, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException)
                {
                    // In use by another instance of the app: removed next time.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
