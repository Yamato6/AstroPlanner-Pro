using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>ui.*</c> area: night vision (the same saved theme as the classic window's switch),
/// reduce transparency and hiding idle toolbars (saved with the planetarium UI's preferences) and full screen (the host
/// window's). Every command answers with the new <see cref="UiSnapshot"/>; <c>ui.changed</c>
/// announces changes made elsewhere (the classic Settings, the window leaving full screen).
/// </summary>
public sealed class InterfaceEndpoints : IEndpointModule
{
    private readonly SettingsViewModel _settings;
    private readonly IInterfaceStore _store;
    private readonly IAppHost _host;
    private readonly IAppLogger _logger;
    private readonly ChangeFeed _changes;
    private InterfacePreferences _preferences;
    private RpcDispatcher? _rpc;

    public InterfaceEndpoints(SettingsViewModel settings, IInterfaceStore store, IAppHost host, IAppLogger logger, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(logger);
        _settings = settings;
        _store = store;
        _host = host;
        _logger = logger;
        _preferences = store.LoadPreferences();
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.UiChanged, new UiChangedEvent(revision)));
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.UiGet, (_, _) => Answer());
        rpc.Register(AppMethods.UiSetTheme, (request, _) =>
            Run(() => _settings.SetTheme(request.Theme == AppThemes.Night ? AppTheme.Night : AppTheme.Dark)));
        rpc.Register(AppMethods.UiSetReduceTransparency, (request, _) =>
            Run(() => SavePreferences(_preferences with { ReduceTransparency = request.Enabled })));
        rpc.Register(AppMethods.UiSetAutoHide, (request, _) =>
            Run(() => SavePreferences(_preferences with { AutoHideToolbars = request.Enabled })));
        rpc.Register(AppMethods.UiSetSkyInertia, (request, _) =>
            Run(() => SavePreferences(_preferences with { SkyInertia = request.Enabled })));
        rpc.Register(AppMethods.UiSetSkyQuality, (request, _) =>
            Run(() => SavePreferences(_preferences with { SkyQuality = request.Quality })));
        rpc.Register(AppMethods.UiSetLightPollutionTint, (request, _) =>
            Run(() => SavePreferences(_preferences with { LightPollutionTint = request.Tint })));
        rpc.Register(AppMethods.UiSetScintillation, (request, _) =>
            Run(() => SavePreferences(_preferences with { Scintillation = request.Enabled })));
        rpc.Register(AppMethods.UiSetFullScreen, (request, _) => Run(() => _host.SetFullScreen(request.Enabled)));
        _settings.Theme.Changed += OnChanged;
        _host.FullScreenChanged += OnChanged;
    }

    /// <summary>The current state as a contract.</summary>
    public UiSnapshot Snapshot() =>
        new(_changes.Revision, _settings.Theme.SkyTheme, _preferences.ReduceTransparency, _preferences.AutoHideToolbars,
            _preferences.SkyInertia,
            Allowed(_preferences.SkyQuality, SkyQualities.Medium, SkyQualities.Low, SkyQualities.Medium, SkyQualities.High),
            Allowed(_preferences.LightPollutionTint, LightPollutionTints.Led, LightPollutionTints.Sodium, LightPollutionTints.Led, LightPollutionTints.Neutral),
            _preferences.Scintillation, _host.IsFullScreen);

    /// <summary>A saved choice if it is still one of <paramref name="allowed"/>, else the default (an edited or older file).</summary>
    private static string Allowed(string? value, string fallback, params string[] allowed) =>
        value is not null && allowed.Contains(value, StringComparer.Ordinal) ? value : fallback;

    private void SavePreferences(InterfacePreferences preferences)
    {
        if (preferences == _preferences)
        {
            return;
        }

        _preferences = preferences;
        try
        {
            _store.SavePreferences(_preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            // It still applies for this session.
            _logger.LogError("Could not save the interface preferences.", ex);
        }

        _changes.Invalidate();
    }

    private Task<UiSnapshot> Answer() => Task.FromResult(Snapshot());

    private Task<UiSnapshot> Run(Action command)
    {
        command();
        return Answer();
    }

    private void OnChanged(object? sender, EventArgs e) => _changes.Invalidate();

    public void Dispose()
    {
        _settings.Theme.Changed -= OnChanged;
        _host.FullScreenChanged -= OnChanged;
        _changes.Dispose();
        _rpc = null;
    }
}
