namespace AstroPlanner.Application.Bridge;

/// <summary>
/// What only the host window can do for the planetarium UI: full screen, the data folder and the
/// browser. The WPF window implements it; tests use a fake, so nothing opens while they run.
/// </summary>
public interface IAppHost
{
    /// <summary>Whether the window fills the screen without its frame (F11).</summary>
    bool IsFullScreen { get; }

    /// <summary>Raised after <see cref="IsFullScreen"/> changes, whoever changed it.</summary>
    event EventHandler? FullScreenChanged;

    void SetFullScreen(bool fullScreen);

    /// <summary>Where app.db, the caches and the logs live.</summary>
    string DataFolder { get; }

    /// <summary>Shows the data folder in Explorer.</summary>
    void OpenDataFolder();

    /// <summary>Opens an https link in the browser; false (and nothing opens) when it is not on SafeLinks' allow-list.</summary>
    bool OpenLink(Uri link);
}
