namespace AstroPlanner.Application.Services;

/// <summary>
/// Application event and error log, to diagnose failures the user cannot describe in detail.
/// Never log personal data beyond rounded coordinates.
/// </summary>
public interface IAppLogger
{
    /// <summary>Where the log is stored, to tell the user.</summary>
    string LogLocation { get; }

    void LogInfo(string message);

    void LogError(string message, Exception? exception = null);
}
