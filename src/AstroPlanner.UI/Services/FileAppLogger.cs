using System.Globalization;
using System.IO;

namespace AstroPlanner.UI.Services;

/// <summary>
/// <see cref="IAppLogger"/> that writes one file per day:
/// %LocalAppData%\AstroPlannerPro\logs\astroplannerpro-YYYYMMDD.log
/// </summary>
public sealed class FileAppLogger : IAppLogger
{
    private readonly string _directory;
    private readonly Lock _sync = new();

    public FileAppLogger(string? directory = null)
    {
        _directory = directory ?? AppPaths.DataFile("logs");
    }

    public string LogLocation => _directory;

    /// <summary>Today's file.</summary>
    public string CurrentFilePath =>
        Path.Combine(_directory, string.Create(CultureInfo.InvariantCulture, $"astroplannerpro-{DateTime.Now:yyyyMMdd}.log"));

    public void LogInfo(string message) => Write("INFO", message, null);

    public void LogError(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(_directory);
                File.AppendAllText(CurrentFilePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never crash the application.
        }
    }
}
