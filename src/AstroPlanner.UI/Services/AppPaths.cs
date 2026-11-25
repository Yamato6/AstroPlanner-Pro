using System.IO;

namespace AstroPlanner.UI.Services;

/// <summary>
/// User data folder: %LocalAppData%\AstroPlannerPro (settings, plan, logbook and logs).
/// The installer never deletes it. AstroPlanner v1 data (%LocalAppData%\AstroPlanner) is never touched.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AstroPlannerPro");

    public static string DataFile(string fileName) => Path.Combine(DataDirectory, fileName);

    /// <summary>
    /// The sky's images shipped next to the exe (<c>data\</c>: panorama, landscapes, manifest.json),
    /// read-only; absent in a build without them.
    /// </summary>
    public static string MediaDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");
}
