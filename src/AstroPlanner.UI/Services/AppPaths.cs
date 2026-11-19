using System.IO;

namespace AstroPlanner.UI.Services;

/// <summary>
/// User data folder: %LocalAppData%\AstroPlanner (settings, plan, logbook and logs).
/// The installer never deletes it.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AstroPlanner");

    public static string DataFile(string fileName) => Path.Combine(DataDirectory, fileName);
}
