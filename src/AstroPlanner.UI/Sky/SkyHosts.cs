namespace AstroPlanner.UI.Sky;

/// <summary>
/// The two virtual hosts WebView2 serves the sky from: the web app (pages and scripts) and the
/// sky data. Shared by the host control and the view models that build URLs.
/// </summary>
public static class SkyHosts
{
    public const string AppHost = "app.astroplanner";
    public const string DataHost = "data.astroplanner";
}
