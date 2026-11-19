using System.IO;

namespace AstroPlanner.UI.Services;

/// <summary>
/// Atomic write of user data files: write a temporary file and rename it, so that a crash
/// half-way never leaves a corrupt file behind.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string filePath, string contents)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempFile = filePath + ".tmp";
        File.WriteAllText(tempFile, contents);
        File.Move(tempFile, filePath, overwrite: true);
    }
}
