using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;

namespace AstroPlanner.Application.Services;

/// <summary>
/// Bright satellites and space stations (spec §4.2, §9.9): the CelesTrak elements, downloaded when
/// older than <see cref="CatalogAge"/> (the service keeps them on disk and pauses itself after an
/// error), the satellites the app can follow, and tonight's visible passes, computed in the
/// background when the night or the site change.
/// </summary>
public sealed class SatelliteState
{
    /// <summary>After a failure there is no new attempt before this delay (the service waits longer itself).</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan CatalogAge = TimeSpan.FromHours(12);

    private readonly IObserverContext _context;
    private readonly ISatelliteService? _service;
    private readonly OnlineInfoState _online;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private SatelliteCatalog? _catalog;
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private (DateTime, double, double, int)? _passesKey;
    private int _passesVersion;
    private bool _fetching;

    public SatelliteState(IObserverContext context, OnlineInfoState online, IAppLogger logger, TimeProvider? timeProvider = null,
        ISatelliteService? service = null)
    {
        _context = context;
        _online = online;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _service = service;
        _context.Recalculated += (_, _) =>
        {
            RecomputePassesIfNeeded();
            _ = RefreshSafeAsync();
        };
        _online.Changed += (_, _) =>
        {
            if (!IsEnabled && Satellites.Count > 0)
            {
                Apply(null);
            }
        };
    }

    /// <summary>Raised when <see cref="Satellites"/> or <see cref="Passes"/> changed.</summary>
    public event EventHandler? Changed;

    public bool IsEnabled => _service is not null && _online.Satellites.Enabled;

    /// <summary>The satellites the app follows (near-Earth, docked modules collapsed).</summary>
    public IReadOnlyList<TrackedSatellite> Satellites { get; private set; } = [];

    /// <summary>Tonight's visible passes (sunset to sunrise), in order of the start of their visible part.</summary>
    public IReadOnlyList<SatellitePass> Passes { get; private set; } = [];

    /// <summary>The pass computation under way (tests await it).</summary>
    public Task PassesTask { get; private set; } = Task.CompletedTask;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Downloads the elements when missing or old, then recomputes the passes. Never throws: a
    /// failure is reported to the online status and the previous elements are kept.
    /// </summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (!IsEnabled)
        {
            if (Satellites.Count > 0)
            {
                Apply(null);
            }

            return;
        }

        var old = _catalog is null || Now - _catalog.FetchedAtUtc >= CatalogAge;
        if (_fetching || (!force && (!old || Now - _lastAttemptUtc < RetryDelay)))
        {
            return;
        }

        _fetching = true;
        _lastAttemptUtc = Now;
        string? error = null;
        SatelliteCatalog? catalog = null;
        try
        {
            catalog = await _service!.GetSatellitesAsync();
        }
        catch (OnlineServiceException ex)
        {
            error = ex.Message;
            _logger.LogInfo($"Satellite elements unavailable: {error}");
        }
        finally
        {
            _fetching = false;
        }

        _online.Report(_online.Satellites, (catalog ?? _catalog)?.FetchedAtUtc ?? Now, error);
        if (catalog is not null)
        {
            Apply(catalog);
        }
    }

    /// <summary>Fire-and-forget version: logs anything unexpected.</summary>
    public async Task RefreshSafeAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError("Unexpected error while refreshing the satellite elements.", ex);
        }
    }

    /// <summary>Uses <paramref name="catalog"/> (null = none, e.g. the service was turned off).</summary>
    internal void Apply(SatelliteCatalog? catalog)
    {
        _catalog = catalog;
        Satellites = catalog is null ? [] : TrackedSatellite.From(catalog.Satellites);
        Passes = [];
        _passesKey = null;
        Changed?.Invoke(this, EventArgs.Empty);
        RecomputePassesIfNeeded();
    }

    /// <summary>The next passes of one satellite from <paramref name="fromUtc"/> (24 h), visible or not.</summary>
    public IReadOnlyList<SatellitePass> NextPasses(int catalogNumber, DateTime fromUtc, int count)
    {
        var satellite = Satellites.FirstOrDefault(s => s.CatalogNumber == catalogNumber);
        return satellite is null
            ? []
            : [.. SatelliteCalculator.FindPasses(satellite, _context.Observer, fromUtc, fromUtc.AddHours(24)).Take(count)];
    }

    public TrackedSatellite? Find(int catalogNumber) => Satellites.FirstOrDefault(s => s.CatalogNumber == catalogNumber);

    private void RecomputePassesIfNeeded()
    {
        if (_context.Night is not { } night || _context.InputError is not null || Satellites.Count == 0)
        {
            return;
        }

        var key = (night.WindowStartLocal.Date, _context.Latitude, _context.Longitude, Satellites.Count);
        if (_passesKey == key)
        {
            return;
        }

        _passesKey = key;
        var startUtc = _context.ToUtc(night.SunsetLocal ?? night.WindowStartLocal);
        var endUtc = _context.ToUtc(night.SunriseLocal ?? night.WindowEndLocal);
        PassesTask = ComputePassesAsync(Satellites, _context.Observer, startUtc, endUtc, ++_passesVersion);
    }

    private async Task ComputePassesAsync(IReadOnlyList<TrackedSatellite> satellites, ObserverLocation observer, DateTime startUtc, DateTime endUtc, int version)
    {
        try
        {
            var passes = await Task.Run(() => satellites
                .AsParallel()
                .SelectMany(s => SatelliteCalculator.FindPasses(s, observer, startUtc, endUtc).Where(p => p.IsVisible))
                .OrderBy(p => p.VisibleStartUtc)
                .ToList());
            if (version == _passesVersion)
            {
                Passes = passes;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Could not compute tonight's satellite passes.", ex);
        }
    }
}
