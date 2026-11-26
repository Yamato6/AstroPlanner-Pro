using AstroPlanner.Application.Services;
using AstroPlanner.Application.Sky;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>survey.*</c> area (spec §8.3, Settings › Survey): which HiPS survey the sky shows when
/// zooming in, realistic or full contrast, and the tile cache on disk (its limit, its size,
/// clearing it). The settings are saved with the planetarium UI's own (<see cref="IInterfaceStore"/>);
/// <c>survey.changed</c> also follows the online images switch (off: cached tiles only).
/// </summary>
public sealed class SurveyEndpoints : IEndpointModule
{
    private const long Megabyte = 1024 * 1024;

    private readonly ISurveyTileSource _tiles;
    private readonly IInterfaceStore _store;
    private readonly ISizedCache _cache;
    private readonly OnlineInfoState _online;
    private readonly IAppLogger _logger;
    private readonly ChangeFeed _changes;
    private SurveyPreferences _preferences;
    private RpcDispatcher? _rpc;

    public SurveyEndpoints(ISurveyTileSource tiles, IInterfaceStore store, ISizedCache tileCache, OnlineInfoState online,
        IAppLogger logger, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tileCache);
        ArgumentNullException.ThrowIfNull(online);
        ArgumentNullException.ThrowIfNull(logger);
        _tiles = tiles;
        _store = store;
        _cache = tileCache;
        _online = online;
        _logger = logger;
        _preferences = Sanitized(store.LoadSurvey());
        _cache.LimitBytes = _preferences.CacheLimitBytes;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.SurveyChanged, new SurveyChangedEvent(revision)));
    }

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.SurveyGet, (_, _) => Task.FromResult(Snapshot()));
        rpc.Register(AppMethods.SurveySet, (request, _) =>
        {
            // A survey the host serves, or none; anything else is ignored.
            if (request.SurveyId is null || _tiles.Surveys.Any(s => s.Id == request.SurveyId))
            {
                Save(_preferences with { SurveyId = request.SurveyId });
            }

            return Task.FromResult(Snapshot());
        });
        rpc.Register(AppMethods.SurveySetMode, (request, _) =>
        {
            Save(_preferences with { Mode = request.Mode });
            return Task.FromResult(Snapshot());
        });
        rpc.Register(AppMethods.SurveySetCacheLimit, async (request, _) =>
        {
            var limit = request.CacheLimitMegabytes * Megabyte;
            Save(_preferences with { CacheLimitBytes = limit });
            // A lower limit trims the cache at once: thousands of files, off the UI thread.
            await Task.Run(() => _cache.LimitBytes = limit, CancellationToken.None).ConfigureAwait(true);
            return Snapshot();
        });
        rpc.Register(AppMethods.SurveyClearCache, async (_, _) =>
        {
            await Task.Run(_cache.Clear, CancellationToken.None).ConfigureAwait(true);
            _changes.Invalidate();
            return Snapshot();
        });
        _online.Changed += OnOnlineChanged;
    }

    /// <summary>The current state as a contract.</summary>
    public SurveySnapshot Snapshot() =>
        new(_changes.Revision,
            SkyHosts.TilesBaseUrl,
            [.. _tiles.Surveys.Take(8).Select(s => new SurveyOption(
                s.Id, Truncate(s.Name, 80), Math.Clamp(s.MaximumOrder, SurveyTileProxy.MinimumOrder, 13),
                Math.Clamp(s.TileWidth, 64, 1024), Truncate(_tiles.CreditFor(s), 300)))],
            _preferences.SurveyId,
            _preferences.Mode,
            _online.Images.Enabled,
            (int)Math.Min(int.MaxValue, _cache.SizeBytes / Megabyte),
            (int)(_preferences.CacheLimitBytes / Megabyte));

    /// <summary>Saved values the host still accepts (an edited or older file falls back to the defaults).</summary>
    private SurveyPreferences Sanitized(SurveyPreferences saved)
    {
        var defaults = new SurveyPreferences();
        return saved with
        {
            SurveyId = saved.SurveyId is null || _tiles.Surveys.Any(s => s.Id == saved.SurveyId) ? saved.SurveyId : defaults.SurveyId,
            Mode = saved.Mode is SurveyModes.Realistic or SurveyModes.Survey ? saved.Mode : defaults.Mode,
            CacheLimitBytes = Math.Clamp(saved.CacheLimitBytes, SurveyPreferences.MinimumCacheLimitBytes, SurveyPreferences.MaximumCacheLimitBytes),
        };
    }

    private void Save(SurveyPreferences preferences)
    {
        if (preferences == _preferences)
        {
            return;
        }

        _preferences = preferences;
        try
        {
            _store.SaveSurvey(_preferences);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            // It still applies for this session.
            _logger.LogError("Could not save the survey settings.", ex);
        }

        _changes.Invalidate();
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    private void OnOnlineChanged(object? sender, EventArgs e) => _changes.Invalidate();

    public void Dispose()
    {
        _online.Changed -= OnOnlineChanged;
        _changes.Dispose();
        _rpc = null;
    }
}
