using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Application.Services;

/// <summary>
/// Comets and bright asteroids for the catalog (spec §4.3, §9.8): the MPC elements, downloaded
/// when older than their cache duration (the service keeps them on disk), turned into catalog
/// objects. Comets enter only when the night's magnitude is at most <see cref="CometMagnitudeLimit"/>;
/// the bright asteroids all do. The list is rebuilt when the night changes, without a download.
/// </summary>
public sealed class SmallBodyState
{
    /// <summary>Faintest comet listed (owner decision, 2026-11-21).</summary>
    public const double CometMagnitudeLimit = 15;

    /// <summary>After a failure there is no new attempt before this delay.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CometAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan AsteroidAge = TimeSpan.FromDays(30);

    private readonly IObserverContext _context;
    private readonly ISmallBodyService? _service;
    private readonly OnlineInfoState _online;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private SmallBodyCatalog? _comets;
    private SmallBodyCatalog? _asteroids;
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private DateTime? _builtForNight;
    private bool _fetching;

    public SmallBodyState(IObserverContext context, OnlineInfoState online, IAppLogger logger, TimeProvider? timeProvider = null,
        ISmallBodyService? service = null)
    {
        _context = context;
        _online = online;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _service = service;
        _context.Recalculated += (_, _) =>
        {
            if (_context.Night is { } night && night.WindowStartLocal.Date != _builtForNight)
            {
                Rebuild();
            }

            _ = RefreshSafeAsync();
        };
        _online.Changed += (_, _) =>
        {
            if (!IsEnabled && Objects.Count > 0)
            {
                Rebuild();
            }
        };
    }

    /// <summary>Raised when <see cref="Objects"/> was rebuilt.</summary>
    public event EventHandler? Changed;

    public bool IsEnabled => _service is not null && _online.SmallBodies.Enabled;

    /// <summary>The comets and asteroids for the current night.</summary>
    public IReadOnlyList<CelestialObject> Objects { get; private set; } = [];

    /// <summary>When the comet elements were downloaded (old when they come from the cache offline).</summary>
    public DateTime? CometsFetchedUtc => _comets?.FetchedAtUtc;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Downloads what is missing or old, then rebuilds the list. Never throws: a failure is
    /// reported to the online status and the previous elements are kept.
    /// </summary>
    public async Task RefreshAsync(bool force = false)
    {
        if (!IsEnabled)
        {
            if (Objects.Count > 0)
            {
                Rebuild();
            }

            return;
        }

        var cometsOld = _comets is null || Now - _comets.FetchedAtUtc >= CometAge;
        var asteroidsOld = _asteroids is null || Now - _asteroids.FetchedAtUtc >= AsteroidAge;
        if (_fetching || (!force && ((!cometsOld && !asteroidsOld) || Now - _lastAttemptUtc < RetryDelay)))
        {
            return;
        }

        _fetching = true;
        _lastAttemptUtc = Now;
        string? error = null;
        try
        {
            if (cometsOld || force)
            {
                try
                {
                    _comets = await _service!.GetCometsAsync();
                }
                catch (OnlineServiceException ex)
                {
                    error = ex.Message;
                }
            }

            if (asteroidsOld || force)
            {
                try
                {
                    _asteroids = await _service!.GetBrightAsteroidsAsync();
                }
                catch (OnlineServiceException ex)
                {
                    error ??= ex.Message;
                }
            }
        }
        finally
        {
            _fetching = false;
        }

        if (error is not null)
        {
            _logger.LogInfo($"Comet/asteroid elements unavailable: {error}");
        }

        _online.Report(_online.SmallBodies, (_comets ?? _asteroids)?.FetchedAtUtc ?? Now, error);
        Rebuild();
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
            _logger.LogError("Unexpected error while refreshing the comet and asteroid elements.", ex);
        }
    }

    /// <summary>Recomputes which comets are bright enough tonight and rebuilds the objects.</summary>
    public void Rebuild()
    {
        _builtForNight = _context.Night?.WindowStartLocal.Date;
        if (!IsEnabled)
        {
            Objects = [];
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var middleUtc = _context.Night is { } night
            ? _context.ToUtc(night.WindowStartLocal + ((night.WindowEndLocal - night.WindowStartLocal) / 2))
            : Now;
        var objects = new List<CelestialObject>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comet in _comets?.Orbits ?? [])
        {
            if (comet.AbsoluteMagnitude is null || !ids.Add(comet.Designation))
            {
                continue;
            }

            var magnitude = OrbitCalculator.GetPosition(comet, middleUtc).Magnitude;
            if (magnitude <= CometMagnitudeLimit)
            {
                objects.Add(CelestialObject.CreateSmallBody(comet.Designation, comet.Name, comet));
            }
        }

        foreach (var asteroid in _asteroids?.Orbits ?? [])
        {
            if (ids.Add(asteroid.Designation))
            {
                objects.Add(CelestialObject.CreateSmallBody(asteroid.Designation, asteroid.Name, asteroid));
            }
        }

        Objects = objects;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
