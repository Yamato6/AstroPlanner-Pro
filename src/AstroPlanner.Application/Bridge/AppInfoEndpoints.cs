using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;
using AstroPlanner.SkyBridge;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// <c>app.info</c> (versions, theme), <c>app.about</c> (the About window's text), opening links
/// and the data folder, and <c>theme.changed</c>.
/// </summary>
public sealed class AppInfoEndpoints : IEndpointModule
{
    /// <summary>Where the source code is (About › License).</summary>
    public static readonly Uri RepositoryUrl = new("https://github.com/Yamato6/AstroPlanner-Pro");

    private static readonly Uri OpenMeteoUrl = new("https://open-meteo.com/");
    private static readonly Uri MilkyWayUrl = new("https://www.eso.org/public/images/eso0932a/");
    private static readonly Uri PolyHavenUrl = new("https://polyhaven.com/");

    private readonly ThemeState _theme;
    private readonly AppVersion _version;
    private readonly IAppHost _host;
    private RpcDispatcher? _rpc;

    /// <param name="version">The executable's version (the host knows it, not this library).</param>
    public AppInfoEndpoints(ThemeState theme, AppVersion version, IAppHost host)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(host);
        _theme = theme;
        _version = version;
        _host = host;
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.AppInfo, (_, _) => Task.FromResult(new AppInfoResponse(
            _version.ToString(), _theme.SkyTheme, SkyMessageCodec.ProtocolVersion, AppMessageCodec.ProtocolVersion)));
        rpc.Register(AppMethods.AppAbout, (_, _) => Task.FromResult(About()));
        rpc.Register(AppMethods.AppOpenLink, (request, _) => Task.FromResult(new OpenLinkResponse(OpenLink(request.Url))));
        rpc.Register(AppMethods.AppOpenDataFolder, (_, _) =>
        {
            _host.OpenDataFolder();
            return Task.FromResult(new NoParameters());
        });
        _theme.Changed += OnThemeChanged;
    }

    /// <summary>The About window: the same text as the classic one, from the resources.</summary>
    public AboutResponse About() => new(
        _version.ToString(),
        Strings.AboutDescription,
        [
            new AboutCredit(Title(Strings.AboutCloudForecast), $"Open-Meteo.com {Strings.AboutCloudForecastLicense}", OpenMeteoUrl.AbsoluteUri),
            new AboutCredit(Title(Strings.AboutOnlineData), Strings.AboutOnlineDataText, null),
            new AboutCredit(Title(Strings.AboutCatalogs), Strings.AboutCatalogsText, null),
            new AboutCredit(Title(Strings.AboutLightPollution), Strings.AboutLightPollutionText, null),
            new AboutCredit(Title(Strings.AboutMilkyWay), Strings.AboutMilkyWayText, MilkyWayUrl.AbsoluteUri),
            new AboutCredit(Title(Strings.AboutLandscapes), Strings.AboutLandscapesText, PolyHavenUrl.AbsoluteUri),
            new AboutCredit(Title(Strings.AboutAlgorithms), Strings.AboutAlgorithmsText, null),
            new AboutCredit(Title(Strings.AboutLibraries), Strings.AboutLibrariesText, null),
        ],
        Strings.AboutYourDataText,
        _host.DataFolder,
        Strings.AboutLicenseText,
        RepositoryUrl.AbsoluteUri);

    /// <summary>Only https links on SafeLinks' allow-list reach the browser.</summary>
    private bool OpenLink(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var link) && SafeLinks.IsAllowed(link) && _host.OpenLink(link);

    /// <summary>"Cloud forecast:" → "Cloud forecast" (the classic window runs the title into the text).</summary>
    private static string Title(string label) => label.TrimEnd(':', ' ');

    private void OnThemeChanged(object? sender, EventArgs e) =>
        _rpc?.Publish(AppEvents.ThemeChanged, new ThemeChangedEvent(_theme.SkyTheme));

    public void Dispose()
    {
        _theme.Changed -= OnThemeChanged;
        _rpc = null;
    }
}
