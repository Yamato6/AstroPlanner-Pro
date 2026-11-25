using System.Diagnostics;
using System.IO;

namespace AstroPlanner.Application.Services;

/// <summary>
/// Opens links and folders from the UI. Only https addresses of the sites the app links to
/// are opened: a tampered link (e.g. in the GitHub response) cannot start a program or open
/// another site.
/// </summary>
public static class SafeLinks
{
    private static readonly string[] AllowedHosts =
    [
        "github.com", "open-meteo.com", "7timer.info",
        // M4b: articles, images and their licenses, survey and name-resolver credits.
        "wikipedia.org", "wikimedia.org", "creativecommons.org", "cds.unistra.fr", "esa.int", "stsci.edu",
        // M5a: comet and asteroid elements.
        "minorplanetcenter.net",
        // M5b: satellite elements.
        "celestrak.org",
        // R2c: credits of the sky's images.
        "eso.org", "polyhaven.com"
    ];

    public static bool IsAllowed(Uri? uri) =>
        uri is { IsAbsoluteUri: true, Scheme: "https" }
        && AllowedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                                    || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    /// <summary>Opens the link in the browser if it is allowed; returns whether it was opened.</summary>
    public static bool Open(Uri? uri)
    {
        if (!IsAllowed(uri))
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
        return true;
    }

    /// <summary>Opens a local folder in Explorer (creating it if it does not exist yet).</summary>
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
