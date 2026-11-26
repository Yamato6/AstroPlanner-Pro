namespace AstroPlanner.Application.Sky;

/// <summary>
/// The virtual hosts WebView2 serves the sky from: the web app (pages and scripts), the sky data
/// and, when the folder exists, the sky's images next to the exe (<see cref="Services.MediaLibrary"/>).
/// Shared by the host control and the view models that build URLs.
/// </summary>
public static class SkyHosts
{
    public const string AppHost = "app.astroplanner";
    public const string DataHost = "data.astroplanner";
    public const string MediaHost = "media.astroplanner";

    /// <summary>Survey tiles, answered by the host's tile proxy (<see cref="Services.SurveyTileProxy"/>), never by the internet directly.</summary>
    public const string TilesHost = "tiles.astroplanner";

    public const string TilesBaseUrl = "https://" + TilesHost + "/";
}
