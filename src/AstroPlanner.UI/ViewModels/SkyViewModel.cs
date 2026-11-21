using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Satellites;
using AstroPlanner.Core.Sky;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.SkyBridge;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using AstroPlanner.UI.Sky;

namespace AstroPlanner.UI.ViewModels;

/// <summary>A visible-layer switch of the sky view.</summary>
public sealed partial class SkyLayerToggle(string key, string name, bool isEnabled, Action changed) : ObservableObject
{
    public string Key { get; } = key;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = isEnabled;

    partial void OnIsEnabledChanged(bool value) => changed();
}

/// <summary>A camera sensor size offered in the field-of-view settings.</summary>
public sealed record SensorPreset(string Name, double WidthMm, double HeightMm)
{
    public override string ToString() => Name;
}

/// <summary>
/// The sky tab. Drives the WebView2 renderer through an <see cref="ISkyChannel"/> (spec §4.2,
/// §5.3): sends the observer, the frame transform from Core, the Sun/Moon/planet tracks, layers,
/// plan highlights and the selection; receives picks, hover, view changes and statistics. Owns
/// the sky clock (play at ×1…×3600, pause, step). While playing only the renderer animates;
/// pausing or stepping applies the sky time to the observer context, so Targets recalculates
/// once. Without WebView2 the tab falls back to the 2D chart (<see cref="GetSkyMapPoints"/>).
/// </summary>
public sealed partial class SkyViewModel : ObservableObject, IDisposable
{
    /// <summary>Body tracks cover ± this around the sky time; regenerated when the time leaves them.</summary>
    internal static readonly TimeSpan TrackHalfWindow = TimeSpan.FromHours(12);

    internal static readonly TimeSpan TrackStep = TimeSpan.FromMinutes(10);

    /// <summary>A new frame transform is sent when the sky time moved more than this from the last one.</summary>
    internal static readonly TimeSpan FrameRefresh = TimeSpan.FromMinutes(10);

    /// <summary>How often the renderer clock is re-synced while playing (real time).</summary>
    internal static readonly TimeSpan ResyncInterval = TimeSpan.FromSeconds(5);

    /// <summary>The observer time counts as "now" (sky runs at ×1) when it is this close to the clock.</summary>
    private static readonly TimeSpan LiveTolerance = TimeSpan.FromSeconds(90);

    private readonly IObserverContext _context;
    private readonly TargetsViewModel _targets;
    private readonly SessionPlanViewModel _plan;
    private readonly ISkyViewStore _store;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _time;
    private readonly EquipmentState? _equipment;
    private readonly SkyClock _clock;
    private readonly SkyViewSettings _settings;
    private ISkyChannel? _channel;
    private bool _rendererReady;
    private DateTime? _frameEpochUtc;
    private DateTime? _tracksCenterUtc;
    private DateTime? _satellitesCenterUtc;
    private readonly SatelliteState? _satellites;
    private string? _sentSelectionId;
    private ITimer? _resyncTimer;
    private SynchronizationContext? _uiContext;
    private readonly bool _initialized;
    private Vector3d? _fovCenter;
    private bool _applyingRendererFov;

    public SkyViewModel(
        IObserverContext context,
        TargetsViewModel targets,
        SessionPlanViewModel plan,
        ISkyViewStore store,
        IAppLogger logger,
        TimeProvider? time = null,
        EquipmentState? equipment = null,
        INameResolver? nameResolver = null,
        OnlineInfoState? online = null,
        SatelliteState? satellites = null)
    {
        _satellites = satellites;
        _equipment = equipment;
        _nameResolver = nameResolver;
        _online = online;
        _context = context;
        _targets = targets;
        _plan = plan;
        _store = store;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _clock = new SkyClock(_time);
        _settings = store.Load();
        StarLimitMagnitude = _settings.StarLimitMagnitude;
        ShowStats = _settings.ShowStats;

        LayerToggles =
        [
            Toggle("stars", Strings.SkyLayerStars, _settings.Stars),
            Toggle("constellationLines", Strings.SkyLayerConstellationLines, _settings.ConstellationLines),
            Toggle("constellationNames", Strings.SkyLayerConstellationNames, _settings.ConstellationNames),
            Toggle("constellationBoundaries", Strings.SkyLayerConstellationBoundaries, _settings.ConstellationBoundaries),
            Toggle("milkyWay", Strings.SkyLayerMilkyWay, _settings.MilkyWay),
            Toggle("deepSky", Strings.SkyLayerDeepSky, _settings.DeepSky),
            Toggle("bodies", Strings.SkyLayerBodies, _settings.Bodies),
            Toggle("satellites", Strings.SkyLayerSatellites, _settings.Satellites),
            Toggle("labels", Strings.SkyLayerLabels, _settings.Labels),
            Toggle("altAzGrid", Strings.SkyLayerAltAzGrid, _settings.AltAzGrid),
            Toggle("equatorialGrid", Strings.SkyLayerEquatorialGrid, _settings.EquatorialGrid),
            Toggle("ecliptic", Strings.SkyLayerEcliptic, _settings.Ecliptic),
            Toggle("meridian", Strings.SkyLayerMeridian, _settings.Meridian),
            Toggle("ground", Strings.SkyLayerGround, _settings.Ground),
            Toggle("skyBrightness", Strings.SkyLayerSkyBrightness, _settings.SkyBrightness)
        ];

        ViewMode = _settings.ViewMode is SkyViewModes.AllSky or SkyViewModes.Finder ? _settings.ViewMode : SkyViewModes.Dome;
        SensorWidthMm = _settings.SensorWidthMm;
        SensorHeightMm = _settings.SensorHeightMm;
        FocalLengthMm = _settings.FocalLengthMm;
        FovRotationDegrees = _settings.FovRotationDegrees;
        IsFovVisible = _settings.FovVisible;
        SelectedSensorPreset = SensorPresets.FirstOrDefault(p => p.WidthMm == SensorWidthMm && p.HeightMm == SensorHeightMm);
        _initialized = true;

        _targets.SmallBodiesChanged += (_, _) =>
        {
            if (_context.InputError is null)
            {
                SendBodies(_clock.UtcNow);
            }
        };
        _targets.SelectionOrTableChanged += (_, _) =>
        {
            MapVersion++;
            var changed = SendSelectionIfChanged();
            OnPropertyChanged(nameof(SelectedText));
            if (changed)
            {
                FollowSelection();
            }
        };
        _plan.Changed += (_, _) =>
        {
            MapVersion++;
            SendHighlights();
        };
        if (_satellites is not null)
        {
            _satellites.Changed += (_, _) =>
            {
                if (_context.InputError is null && _satellites.Satellites.Count != _sentSatelliteCount)
                {
                    SendBodies(_clock.UtcNow);
                }
            };
        }
        _context.Recalculated += (_, _) => OnContextRecalculated();
        _context.PropertyChanged += OnContextPropertyChanged;
        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) => ApplyActiveRig();
            ApplyActiveRig();
        }

        SyncClockToContext();
    }

    /// <summary>Name of the rig the camera frame comes from; null when it is typed by hand.</summary>
    [ObservableProperty]
    public partial string? FovRigName { get; private set; }

    /// <summary>The camera frame follows the active rig (sensor and effective focal length); without one it stays manual.</summary>
    private void ApplyActiveRig()
    {
        if (_equipment?.ActiveRig is not { } rig)
        {
            FovRigName = null;
            return;
        }

        SensorWidthMm = rig.Camera.SensorWidthMm;
        SensorHeightMm = rig.Camera.SensorHeightMm;
        FocalLengthMm = rig.FocalLengthMm;
        SelectedSensorPreset = SensorPresets.FirstOrDefault(p => p.WidthMm == SensorWidthMm && p.HeightMm == SensorHeightMm);
        FovRigName = rig.Name;
    }

    /// <summary>The site's zenith SQM for the renderer: manual or atlas; null (natural sky) when unknown.</summary>
    private double? SiteSqm => _context.ZenithSqmSource == SkyQualitySource.Default ? null : _context.EffectiveZenithSqm;

    private SkyLayerToggle Toggle(string key, string name, bool value) => new(key, name, value, OnLayersChanged);

    // ---- fallback 2D chart (no WebView2) ------------------------------------------------------

    /// <summary>Changes whenever the 2D chart must be redrawn.</summary>
    [ObservableProperty]
    public partial int MapVersion { get; private set; }

    public MoonState? Moon => _context.Moon;

    public string MoonIlluminationText => DisplayNames.MoonIllumination(_context.Moon);

    public double MinimumAltitude => _context.MinimumAltitude;

    /// <summary>Most objects drawn on the 2D chart: with the full catalog thousands are above the horizon.</summary>
    public const int MaximumPoints = 300;

    /// <summary>How many of the brightest objects get a label (plan, selection and planets always do).</summary>
    public const int MaximumLabels = 60;

    /// <summary>
    /// Objects above the horizon that pass the filters: those in the plan, the selected one and
    /// the planets first, then the brightest, up to <see cref="MaximumPoints"/>.
    /// </summary>
    public IReadOnlyList<SkyMapPoint> GetSkyMapPoints()
    {
        var planIds = _plan.Items.Select(item => item.Id).ToHashSet();
        var selected = _targets.SelectedObject;
        bool IsHighlighted(CelestialObjectRowViewModel item) =>
            item.IsPlanet || planIds.Contains(item.Id) || ReferenceEquals(item, selected);

        return _targets.Objects
            .Where(item => item.AltitudeDegrees > 0)
            .OrderByDescending(IsHighlighted)
            .ThenBy(item => double.IsNaN(item.ApparentMagnitude) ? double.MaxValue : item.ApparentMagnitude)
            .Take(MaximumPoints)
            .Select((item, rank) => new SkyMapPoint(
                item.Id,
                item.AltitudeDegrees,
                item.AzimuthDegrees,
                item.ApparentMagnitude,
                item.IsVisible,
                planIds.Contains(item.Id),
                ReferenceEquals(item, selected),
                item.IsPlanet,
                HasLabel: IsHighlighted(item) || rank < MaximumLabels))
            .ToList();
    }

    /// <summary>
    /// Selects the object nearest to (x, y), in projection units (degrees), if it is closer
    /// than <paramref name="maximumDistance"/>.
    /// </summary>
    public bool SelectNearestOnSkyMap(double x, double y, double maximumDistance = 6)
    {
        var nearest = GetSkyMapPoints()
            .Select(point => (point.Id, Distance: Math.Sqrt(
                Math.Pow(point.Position.X - x, 2) + Math.Pow(point.Position.Y - y, 2))))
            .Where(candidate => candidate.Distance <= maximumDistance)
            .OrderBy(candidate => candidate.Distance)
            .FirstOrDefault();

        if (nearest.Id is null)
        {
            return false;
        }

        _targets.SelectedObject = _targets.Objects.First(item => item.Id == nearest.Id);
        return true;
    }

    // ---- renderer state -------------------------------------------------------------------------

    /// <summary>True while the WebView2 renderer is in use; false shows the 2D chart.</summary>
    [ObservableProperty]
    public partial bool IsRendererAvailable { get; set; } = true;

    /// <summary>Why the renderer is not available (shown above the 2D chart).</summary>
    [ObservableProperty]
    public partial string? RendererMessage { get; private set; }

    public IReadOnlyList<SkyLayerToggle> LayerToggles { get; }

    [ObservableProperty]
    public partial double StarLimitMagnitude { get; set; }

    partial void OnStarLimitMagnitudeChanged(double value) => OnLayersChanged();

    [ObservableProperty]
    public partial bool ShowStats { get; set; }

    partial void OnShowStatsChanged(bool value)
    {
        _settings.ShowStats = value;
        SaveSettings();
    }

    /// <summary>Object under the pointer in the renderer.</summary>
    [ObservableProperty]
    public partial string? HoverText { get; private set; }

    /// <summary>Picked object that is not in the catalog (Sun, Moon, a star): name shown in the side panel.</summary>
    [ObservableProperty]
    public partial string? SkyOnlySelectionText { get; private set; }

    /// <summary>Last frame statistics reported by the renderer.</summary>
    [ObservableProperty]
    public partial string? StatsText { get; private set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    [ObservableProperty]
    public partial string? SearchMessage { get; private set; }

    /// <summary>The last name resolved online (Sesame), shown at the centre of the view; null otherwise.</summary>
    [ObservableProperty]
    public partial ResolvedName? ResolvedTarget { get; private set; }

    private readonly INameResolver? _nameResolver;
    private readonly OnlineInfoState? _online;

    /// <summary>Raised when the renderer asks for the search box (the "/" shortcut).</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Raised when the user asks to see the selected object's details.</summary>
    public event EventHandler? OpenDetailsRequested;

    public string SelectedText => SkyOnlySelectionText
        ?? (_targets.SelectedObject is { } selected
            ? DisplayNames.Format(Strings.SkySelectedFormat, selected.Title)
            : Strings.SkyNothingSelected);

    public bool HasCatalogSelection => _targets.SelectedObject is not null && SkyOnlySelectionText is null;

    // ---- clock ----------------------------------------------------------------------------------

    public IReadOnlyList<double> Rates { get; } = [1, 60, 600, 3600];

    [ObservableProperty]
    public partial double SelectedRate { get; set; } = 60;

    partial void OnSelectedRateChanged(double value)
    {
        if (IsPlaying)
        {
            _clock.Set(_clock.UtcNow, value);
            SendTime();
        }
    }

    [ObservableProperty]
    public partial bool IsPlaying { get; private set; }

    /// <summary>Sky time in the observer's time zone, for display.</summary>
    public string SkyTimeText =>
        TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _context.TimeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    internal SkyClock Clock => _clock;

    [RelayCommand]
    private void Play()
    {
        if (IsPlaying)
        {
            return;
        }

        IsPlaying = true;
        _clock.Set(_context.ToUtc(_context.ObservationDateTime), SelectedRate);
        SendTime();
        _uiContext = SynchronizationContext.Current;
        _resyncTimer = _time.CreateTimer(_ => OnResyncTimer(), null, ResyncInterval, ResyncInterval);
    }

    [RelayCommand]
    private void Pause()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        _resyncTimer?.Dispose();
        _resyncTimer = null;
        var utc = _clock.UtcNow;
        _clock.Set(utc, 0);
        SendTime();
        ApplyToContext(utc);
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>Moves the sky (and the observation time) by the given minutes ("1", "-10", "1440"…).</summary>
    [RelayCommand]
    private void Step(string minutes)
    {
        if (!int.TryParse(minutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delta))
        {
            return;
        }

        Pause();
        ApplyToContext(_context.ToUtc(_context.ObservationDateTime).AddMinutes(delta));
    }

    [RelayCommand]
    private void Now()
    {
        Pause();
        _context.ObservationDateTime = _context.Now;
    }

    private void ApplyToContext(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _context.TimeZone);
        _context.ObservationDateTime = DateTime.SpecifyKind(
            new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Unspecified);
    }

    private void OnResyncTimer()
    {
        if (_uiContext is null || SynchronizationContext.Current == _uiContext)
        {
            Resync();
        }
        else
        {
            _uiContext.Post(_ => Resync(), null);
        }
    }

    /// <summary>While playing: re-syncs the renderer clock and refreshes the frame and tracks when needed.</summary>
    internal void Resync()
    {
        if (!IsPlaying)
        {
            return;
        }

        var utc = _clock.UtcNow;
        if (_frameEpochUtc is null || (utc - _frameEpochUtc.Value).Duration() > FrameRefresh)
        {
            SendFrame(utc);
        }

        if (_tracksCenterUtc is null || (utc - _tracksCenterUtc.Value).Duration() > TrackHalfWindow - TimeSpan.FromHours(1)
            || (_satellitesCenterUtc is { } satellites && (utc - satellites).Duration() > SatelliteTracks.HalfWindow - SatelliteResendMargin))
        {
            SendBodies(utc);
        }

        SendTime();
        OnPropertyChanged(nameof(SkyTimeText));
        UpdateSkyBrightnessText();
    }

    /// <summary>When paused, the sky shows the observer context time (running at ×1 if that is "now").</summary>
    private void SyncClockToContext()
    {
        var utc = _context.ToUtc(_context.ObservationDateTime);
        var isNow = (utc - _context.ToUtc(_context.Now)).Duration() <= LiveTolerance;
        _clock.Set(utc, isNow ? 1 : 0);
        OnPropertyChanged(nameof(SkyTimeText));
    }

    private void OnContextRecalculated()
    {
        OnPropertyChanged(nameof(Moon));
        OnPropertyChanged(nameof(MoonIlluminationText));
        OnPropertyChanged(nameof(MinimumAltitude));
        MapVersion++;
        if (IsPlaying || _context.InputError is not null)
        {
            return;
        }

        SyncClockToContext();
        SendSiteAndTime();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IObserverContext.EffectiveZenithSqm))
        {
            SendAppearance(_clock.UtcNow);
            UpdateSkyBrightnessText();
            return;
        }

        if (e.PropertyName == nameof(IObserverContext.ObservationDateTime) && IsPlaying)
        {
            // The user picked a time in the top bar: stop the animation there.
            IsPlaying = false;
            _resyncTimer?.Dispose();
            _resyncTimer = null;
        }
    }

    // ---- channel --------------------------------------------------------------------------------

    /// <summary>Connects the renderer; nothing is sent until it reports <c>ready</c>.</summary>
    public void Attach(ISkyChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Detach();
        _channel = channel;
        _rendererReady = false;
        channel.MessageReceived += OnMessage;
        IsRendererAvailable = true;
        RendererMessage = null;
    }

    public void Detach()
    {
        if (_channel is not null)
        {
            _channel.MessageReceived -= OnMessage;
        }

        _channel = null;
        _rendererReady = false;
    }

    /// <summary>The renderer cannot run here: show the 2D chart and say why.</summary>
    public void UseFallback(string reason)
    {
        Detach();
        IsRendererAvailable = false;
        RendererMessage = reason == "runtime" ? Strings.SkyRendererNoRuntime : Strings.SkyRendererNotBuilt;
        MapVersion++;
    }

    private void OnMessage(object? sender, IncomingSkyMessage message)
    {
        switch (message.Payload)
        {
            case ReadyPayload ready:
                _logger.LogInfo($"Sky renderer {ready.RendererVersion} ready (WebGL {ready.WebGlVersion}).");
                _rendererReady = true;
                SendAll();
                break;
            case FirstFramePayload first:
                _logger.LogInfo(string.Create(CultureInfo.InvariantCulture, $"Sky renderer: first frame after {first.Milliseconds:0} ms."));
                break;
            case SelectionPayload selection:
                OnRendererSelection(selection);
                break;
            case HoverPayload hover:
                HoverText = hover.Label ?? hover.Id;
                break;
            case ViewChangedPayload view:
                _settings.AzimuthDegrees = view.AzimuthDegrees;
                _settings.AltitudeDegrees = view.AltitudeDegrees;
                _settings.FovDegrees = view.FovDegrees;
                SaveSettings();
                break;
            case StatsPayload stats:
                StatsText = string.Create(CultureInfo.InvariantCulture,
                    $"{stats.Fps:0} fps · p95 {stats.P95FrameMs:0.0} ms · {stats.Width}×{stats.Height} · {stats.Stars} stars");
                if (stats.Benchmark)
                {
                    _logger.LogInfo("Sky benchmark: " + StatsText);
                }

                break;
            case ShortcutPayload shortcut:
                OnShortcut(shortcut.Action);
                break;
            case FovChangedPayload fov:
                OnRendererFovChanged(fov);
                break;
            case ErrorPayload error:
                _logger.LogError("Sky renderer: " + error.Message);
                break;
        }
    }

    private void OnShortcut(string action)
    {
        switch (action)
        {
            case SkyShortcuts.TogglePause:
                TogglePlay();
                break;
            case SkyShortcuts.FocusSearch:
                FocusSearchRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SkyShortcuts.ViewDome:
                SetViewMode(SkyViewModes.Dome);
                break;
            case SkyShortcuts.ViewAllSky:
                SetViewMode(SkyViewModes.AllSky);
                break;
            case SkyShortcuts.ToggleFov:
                IsFovVisible = !IsFovVisible;
                break;
        }
    }

    private void OnRendererSelection(SelectionPayload selection)
    {
        if (selection.Id is null)
        {
            SkyOnlySelectionText = null;
            OnPropertyChanged(nameof(SelectedText));
            OnPropertyChanged(nameof(HasCatalogSelection));
            return;
        }

        _sentSelectionId = selection.Id;
        if ((selection.Kind is SkyObjectKinds.DeepSky or SkyObjectKinds.Body) && _targets.SelectById(selection.Id))
        {
            SkyOnlySelectionText = null;
        }
        else
        {
            SkyOnlySelectionText = SatelliteSelectionText(selection.Id) ?? HoverText ?? BodyName(selection.Id);
        }

        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(HasCatalogSelection));
    }

    private static string BodyName(string id) => id switch
    {
        "Sun" => Strings.SkySun,
        "Moon" => Strings.SkyMoon,
        _ => id
    };

    private void Send(string json)
    {
        if (_rendererReady)
        {
            _channel?.Post(json);
        }
    }

    private void SendAll()
    {
        Send(SkyMessageCodec.Init(new InitPayload(
            $"https://{SkyHost.DataHost}/", RefractionModel.Standard, Layers(), ViewMode, "dark", ShowStats)));
        SendSiteAndTime();
        Send(SkyMessageCodec.CenterOn(CenterOnView()));
        SendHighlights();
        _sentSelectionId = null;
        SendSelectionIfChanged();
        _fovCenter ??= SelectedDirection();
        SendFov();
        if (ViewMode == SkyViewModes.Finder)
        {
            CenterFinder();
        }
    }

    private void SendSiteAndTime()
    {
        if (!_rendererReady || _context.InputError is not null)
        {
            return;
        }

        var utc = _clock.UtcNow;
        Send(SkyMessageCodec.SetObserver(SkyBridgeMapper.ToPayload(
            _context.LocationName, _context.Observer, _context.MinimumAltitude, _context.HorizonMask)));
        SendFrame(utc);
        SendBodies(utc);
        SendTime();
        UpdateSkyBrightnessText();
    }

    private void SendFrame(DateTime utc)
    {
        if (!_rendererReady)
        {
            return;
        }

        _frameEpochUtc = utc;
        Send(SkyMessageCodec.FrameTransform(SkyBridgeMapper.ToPayload(SkyFrame.Compute(_context.Observer, utc))));
    }

    private void SendBodies(DateTime utc)
    {
        if (!_rendererReady)
        {
            return;
        }

        _tracksCenterUtc = utc;
        var satellites = SkySatellites(utc);
        _satellitesCenterUtc = satellites.Count > 0 ? utc : null;
        Send(SkyMessageCodec.MovingBodies(SkyBridgeMapper.MovingBodies(
            _context.Observer, utc, TrackHalfWindow, TrackStep, BodyDisplayName, SkySmallBodies(utc), satellites)));
        SendAppearance(utc);
    }

    /// <summary>Most satellites drawn at once (those above the horizon in the ±15 min window, brightest first).</summary>
    public const int SatelliteCountLimit = 60;

    /// <summary>Satellite tracks are re-sent this long before the sky time leaves their ±15 min window.</summary>
    private static readonly TimeSpan SatelliteResendMargin = TimeSpan.FromMinutes(5);

    private int _sentSatelliteCount = -1;

    private IReadOnlyList<SatelliteTrack> SkySatellites(DateTime utc)
    {
        _sentSatelliteCount = _satellites?.Satellites.Count ?? 0;
        return _satellites is { IsEnabled: true, Satellites.Count: > 0 } state
            ? SatelliteTracks.Get(state.Satellites, _context.Observer, utc, SatelliteCountLimit)
            : [];
    }

    /// <summary>"ISS (ZARYA) · next pass 19:42–19:48, max 54°" for a satellite picked in the sky.</summary>
    private string? SatelliteSelectionText(string id)
    {
        if (_satellites is null || !id.StartsWith("sat-", StringComparison.Ordinal)
            || !int.TryParse(id.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            || _satellites.Find(number) is not { } satellite)
        {
            return null;
        }

        var next = _satellites.NextPasses(number, _clock.UtcNow, 1);
        if (next.Count == 0)
        {
            return DisplayNames.Format(Strings.SkySatelliteNoPassFormat, satellite.Name);
        }

        var pass = next[0];
        return DisplayNames.Format(Strings.SkySatelliteNextPassFormat, satellite.Name,
            TimeZoneInfo.ConvertTimeFromUtc(pass.RiseUtc, _context.TimeZone), TimeZoneInfo.ConvertTimeFromUtc(pass.SetUtc, _context.TimeZone),
            pass.MaxAltitudeDegrees, pass.IsVisible ? Strings.SkySatelliteVisible : Strings.SkySatelliteNotVisible);
    }

    /// <summary>Faintest comet or asteroid drawn in the sky.</summary>
    public const double SmallBodyMagnitudeLimit = 12;

    /// <summary>Most comets and asteroids drawn (the brightest), to keep the tracks message small.</summary>
    public const int SmallBodyCountLimit = 40;

    /// <summary>The comets and asteroids of the catalog bright enough to draw at <paramref name="utc"/>, brightest first.</summary>
    private List<CelestialObject> SkySmallBodies(DateTime utc) =>
    [
        .. _targets.AllObjects
            .Where(row => row.Target.IsSmallBody)
            .Select(row => (row.Target, Magnitude: MagnitudeAt(row.Target, utc)))
            .Where(b => double.IsFinite(b.Magnitude) && b.Magnitude <= SmallBodyMagnitudeLimit)
            .OrderBy(b => b.Magnitude)
            .Take(SmallBodyCountLimit)
            .Select(b => b.Target)
    ];

    private static double MagnitudeAt(CelestialObject target, DateTime utc) =>
        target.Orbit is { } orbit ? OrbitCalculator.GetPosition(orbit, utc).Magnitude : double.NaN;

    /// <summary>Sky brightness over the same window as the body tracks (they are refreshed together).</summary>
    private void SendAppearance(DateTime utc)
    {
        if (!_rendererReady || _context.InputError is not null)
        {
            return;
        }

        Send(SkyMessageCodec.SetSkyAppearance(SkyBridgeMapper.Appearance(
            _context.Observer, utc, TrackHalfWindow, TrackStep, SiteSqm)));
    }

    private string BodyDisplayName(string id) =>
        _targets.AllObjects.FirstOrDefault(o => o.Id == id)?.Name ?? BodyName(id);

    private void SendTime() =>
        Send(SkyMessageCodec.SetTime(new TimePayload(SkyMessageCodec.FormatUtc(_clock.UtcNow), _clock.Rate)));

    private void SendHighlights() =>
        Send(SkyMessageCodec.SetHighlights(new HighlightsPayload([.. _plan.Items.Select(item => item.Id)])));

    /// <summary>Sends the Targets selection when it changed; true if it did.</summary>
    private bool SendSelectionIfChanged()
    {
        var selected = _targets.SelectedObject;
        var id = selected?.Id;
        if (id == _sentSelectionId || !_rendererReady)
        {
            return false;
        }

        _sentSelectionId = id;
        if (selected is not null)
        {
            SkyOnlySelectionText = null;
        }

        Send(SkyMessageCodec.SelectObject(selected is null
            ? new SelectionPayload(null, null)
            : new SelectionPayload(selected.Target.IsMoving ? SkyObjectKinds.Body : SkyObjectKinds.DeepSky, selected.Id)));
        OnPropertyChanged(nameof(HasCatalogSelection));
        return true;
    }

    /// <summary>Restores the last camera (azimuth/altitude → J2000 direction at the sky time).</summary>
    private CenterOnPayload CenterOnView()
    {
        var utc = _clock.UtcNow;
        var frame = SkyFrame.Compute(_context.Observer, utc);
        var v = frame.J2000ToHorizontalAt(utc).Transpose().Transform(
            SkyFrame.FromHorizontal(new HorizontalCoordinates(_settings.AltitudeDegrees, _settings.AzimuthDegrees)));
        return new CenterOnPayload(v.X, v.Y, v.Z, _settings.FovDegrees);
    }

    // ---- actions ----------------------------------------------------------------------------------

    /// <summary>Turns the view to the selected catalog object.</summary>
    [RelayCommand]
    private void CenterOnSelection()
    {
        if (_targets.SelectedObject is { } selected)
        {
            CenterOn(selected.Target, fovDegrees: null);
        }
    }

    [RelayCommand]
    private void OpenDetails() => OpenDetailsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Finds an object by id or name ("m31", "ngc 7000", "Orion", "Jupiter"), selects it and centers
    /// the view. A name missing from the catalog is resolved online with CDS Sesame when allowed
    /// (spec §4.2): the view turns to it and the message says what it is and who resolved it.
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var text = SearchText.Trim();
        var query = CatalogSearch.Parse(SearchText);
        if (query.IsEmpty)
        {
            return;
        }

        ResolvedTarget = null;

        // An exact name ("Jupiter", "Andromeda Galaxy") wins over objects that only contain the text.
        var matches = _targets.AllObjects.Where(item => CatalogSearch.Matches(item, query)).ToList();
        var match = matches.FirstOrDefault(item => string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault();
        if (match is not null)
        {
            SearchMessage = null;
            _targets.SelectById(match.Id);
            CenterOn(match.Target, fovDegrees: match.IsPlanet ? 20 : match.Target.IsSmallBody ? 10 : 30);
            return;
        }

        if (_nameResolver is null || _online is { NameResolver.Enabled: false })
        {
            SearchMessage = DisplayNames.Format(Strings.SkySearchNotFoundFormat, text);
            return;
        }

        SearchMessage = DisplayNames.Format(Strings.SkySearchResolvingFormat, text);
        try
        {
            var resolved = await _nameResolver.ResolveAsync(text);
            ReportResolver(null);
            if (SearchText.Trim() != text)
            {
                return;   // the user typed something else meanwhile
            }

            if (resolved is null)
            {
                SearchMessage = DisplayNames.Format(Strings.SkySearchNotFoundOnlineFormat, text);
                return;
            }

            ResolvedTarget = resolved;
            SearchMessage = DisplayNames.Format(Strings.SkySearchResolvedFormat, resolved.Name, resolved.ObjectType ?? "?", resolved.Source,
                resolved.Coordinates.RightAscensionHours, resolved.Coordinates.DeclinationDegrees);
            var direction = SkyBridgeMapper.ToVector(SkyFrame.FromEquatorial(resolved.Coordinates));
            Send(SkyMessageCodec.CenterOn(new CenterOnPayload(direction.X, direction.Y, direction.Z, 5)));
        }
        catch (OnlineServiceException ex)
        {
            ReportResolver(ex.Message);
            SearchMessage = DisplayNames.Format(Strings.SkySearchOnlineFailedFormat, text, ex.Message);
        }
    }

    private void ReportResolver(string? error)
    {
        if (_online is not null)
        {
            _online.Report(_online.NameResolver, _time.GetUtcNow().UtcDateTime, error);
        }
    }

    private void CenterOn(CelestialObject target, double? fovDegrees)
    {
        if (DirectionOf(target) is { } v)
        {
            var direction = SkyBridgeMapper.ToVector(v);
            Send(SkyMessageCodec.CenterOn(new CenterOnPayload(direction.X, direction.Y, direction.Z, fovDegrees)));
        }
    }

    /// <summary>
    /// J2000-frame direction of a catalog object at the sky time; for a planet, its topocentric
    /// position rotated back like the body tracks (null if Core does not know it).
    /// </summary>
    private Vector3d? DirectionOf(CelestialObject target)
    {
        if (!target.IsMoving)
        {
            return SkyFrame.FromEquatorial(target.Coordinates);
        }

        var utc = _clock.UtcNow;
        var body = MovingBodyCalculator.GetBodies(_context.Observer, utc, target.IsSmallBody ? [target] : null)
            .FirstOrDefault(b => b.Id == target.Id);
        return body is null
            ? null
            : SkyFrame.Compute(_context.Observer, utc).J2000ToHorizontalAt(utc).Transpose().Transform(SkyFrame.FromHorizontal(body.Horizontal));
    }

    private Vector3d? SelectedDirection() =>
        _targets.SelectedObject is { } selected && _context.InputError is null ? DirectionOf(selected.Target) : null;

    // ---- view modes (3D dome, all-sky chart, finder chart) -----------------------------------------

    /// <summary>Raised when an action elsewhere (Detail, Targets) wants the Sky tab shown.</summary>
    public event EventHandler? ShowSkyRequested;

    /// <summary>Current renderer mode, as a protocol name (<see cref="SkyViewModes"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDomeMode), nameof(IsAllSkyMode), nameof(IsFinderMode))]
    public partial string ViewMode { get; private set; } = SkyViewModes.Dome;

    public bool IsDomeMode
    {
        get => ViewMode == SkyViewModes.Dome;
        set { if (value) SetViewMode(SkyViewModes.Dome); }
    }

    public bool IsAllSkyMode
    {
        get => ViewMode == SkyViewModes.AllSky;
        set { if (value) SetViewMode(SkyViewModes.AllSky); }
    }

    public bool IsFinderMode
    {
        get => ViewMode == SkyViewModes.Finder;
        set { if (value) SetViewMode(SkyViewModes.Finder); }
    }

    /// <summary>
    /// Field of the finder chart: three times the camera field when the overlay is on (so that
    /// the frame and its surroundings show), else the remembered field.
    /// </summary>
    public double FinderFovDegrees => IsFovVisible && CameraFov is { } fov
        ? Math.Clamp(3 * Math.Max(fov.WidthDegrees, fov.HeightDegrees), 1, 60)
        : Math.Clamp(_settings.FinderFovDegrees, 1, 60);

    private void SetViewMode(string mode)
    {
        if (mode == ViewMode)
        {
            return;
        }

        ViewMode = mode;
        _settings.ViewMode = mode;
        SaveSettings();
        Send(SkyMessageCodec.SetViewMode(new ViewModePayload(mode)));
        if (mode == SkyViewModes.Finder)
        {
            CenterFinder();
        }
    }

    /// <summary>The finder chart is centred on the selected object, else on the camera field.</summary>
    private void CenterFinder()
    {
        if ((SelectedDirection() ?? _fovCenter) is { } center)
        {
            var v = SkyBridgeMapper.ToVector(center);
            Send(SkyMessageCodec.CenterOn(new CenterOnPayload(v.X, v.Y, v.Z, FinderFovDegrees)));
        }
    }

    /// <summary>Finder chart of the selected object ("Open finder chart" in Detail and Targets).</summary>
    [RelayCommand]
    private void OpenFinderChart()
    {
        ShowSkyRequested?.Invoke(this, EventArgs.Empty);
        if (ViewMode == SkyViewModes.Finder)
        {
            CenterFinder();
        }
        else
        {
            SetViewMode(SkyViewModes.Finder);
        }
    }

    /// <summary>A new selection moves the camera field onto it and recentres the finder chart.</summary>
    private void FollowSelection()
    {
        if (SelectedDirection() is not { } direction)
        {
            return;
        }

        _fovCenter = direction;
        if (IsFovVisible)
        {
            SendFov();
        }

        if (ViewMode == SkyViewModes.Finder)
        {
            CenterFinder();
        }
    }

    /// <summary>File name offered when the view is exported as PNG.</summary>
    public string SuggestedImageFileName()
    {
        var subject = ViewMode == SkyViewModes.Finder && _targets.SelectedObject is { } selected
            ? "-" + string.Concat(selected.Id.Split(System.IO.Path.GetInvalidFileNameChars().Append(' ').ToArray()))
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"AstroPlannerPro-{ViewMode}{subject}-{TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, _context.TimeZone):yyyy-MM-dd-HHmm}.png");
    }

    // ---- camera field-of-view overlay ---------------------------------------------------------------

    public IReadOnlyList<SensorPreset> SensorPresets { get; } =
    [
        new(Strings.SensorFullFrame, 36.0, 24.0),
        new(Strings.SensorApsCNikonSony, 23.5, 15.6),
        new(Strings.SensorApsCCanon, 22.3, 14.9),
        new(Strings.SensorMicroFourThirds, 17.3, 13.0),
        new(Strings.SensorOneInch, 13.2, 8.8),
        new(Strings.SensorImx571, 23.5, 15.7),
        new(Strings.SensorImx533, 11.3, 11.3),
        new(Strings.SensorImx585, 11.1, 6.3)
    ];

    [ObservableProperty]
    public partial bool IsFovVisible { get; set; }

    partial void OnIsFovVisibleChanged(bool value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial SensorPreset? SelectedSensorPreset { get; set; }

    partial void OnSelectedSensorPresetChanged(SensorPreset? value)
    {
        if (value is not null)
        {
            SensorWidthMm = value.WidthMm;
            SensorHeightMm = value.HeightMm;
        }
    }

    [ObservableProperty]
    public partial double SensorWidthMm { get; set; }

    partial void OnSensorWidthMmChanged(double value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial double SensorHeightMm { get; set; }

    partial void OnSensorHeightMmChanged(double value) => OnFovSettingsChanged();

    [ObservableProperty]
    public partial double FocalLengthMm { get; set; }

    partial void OnFocalLengthMmChanged(double value) => OnFovSettingsChanged();

    /// <summary>Position angle of the image's top, from north through east (0 = north up).</summary>
    [ObservableProperty]
    public partial double FovRotationDegrees { get; set; }

    partial void OnFovRotationDegreesChanged(double value) => OnFovSettingsChanged();

    /// <summary>Field of the camera, or null while the sizes typed are not valid.</summary>
    public FieldOfView? CameraFov =>
        SensorWidthMm is > 0 and <= 100 && SensorHeightMm is > 0 and <= 100 && FocalLengthMm is >= 1 and <= 20000
            ? OpticsMath.FieldOfView(SensorWidthMm, SensorHeightMm, FocalLengthMm)
            : null;

    /// <summary>"1.96° × 1.31°" (arcminutes below one degree), or a hint when the sizes are invalid.</summary>
    public string FovText => CameraFov is { } fov ? FormatFov(fov) : Strings.SkyFovInvalid;

    /// <summary>Center of the camera field (J2000), e.g. "RA 05h 35.3m · Dec −05° 23′".</summary>
    public string? FovCenterText => IsFovVisible && _fovCenter is { } center ? FormatCenter(SkyFrame.ToEquatorial(center)) : null;

    private static string FormatFov(FieldOfView fov) =>
        Math.Max(fov.WidthDegrees, fov.HeightDegrees) < 1
            ? DisplayNames.Format(Strings.SkyFovArcminFormat, fov.WidthDegrees * 60, fov.HeightDegrees * 60)
            : DisplayNames.Format(Strings.SkyFovFormat, fov.WidthDegrees, fov.HeightDegrees);

    private static string FormatCenter(EquatorialCoordinates c)
    {
        var raMinutes = Math.Round(c.RightAscensionHours * 60, 1);
        var decMinutes = (int)Math.Round(Math.Abs(c.DeclinationDegrees) * 60);
        return DisplayNames.Format(Strings.SkyFovCenterFormat,
            (int)(raMinutes / 60) % 24, raMinutes % 60, c.DeclinationDegrees < 0 ? "−" : "+", decMinutes / 60, decMinutes % 60);
    }

    private void OnFovSettingsChanged()
    {
        if (!_initialized)
        {
            return;
        }

        if (SelectedSensorPreset is { } preset && (preset.WidthMm != SensorWidthMm || preset.HeightMm != SensorHeightMm))
        {
            SelectedSensorPreset = null;
        }

        _settings.FovVisible = IsFovVisible;
        _settings.SensorWidthMm = SensorWidthMm;
        _settings.SensorHeightMm = SensorHeightMm;
        _settings.FocalLengthMm = FocalLengthMm;
        _settings.FovRotationDegrees = FovRotationDegrees;
        SaveSettings();
        OnPropertyChanged(nameof(CameraFov));
        OnPropertyChanged(nameof(FovText));
        OnPropertyChanged(nameof(FovCenterText));
        if (!_applyingRendererFov)
        {
            _fovCenter ??= SelectedDirection();
            SendFov();
        }
    }

    private void SendFov()
    {
        if (!_rendererReady)
        {
            return;
        }

        if (!IsFovVisible || CameraFov is not { } fov)
        {
            Send(SkyMessageCodec.SetFov(new FovPayload(false, 1, 1, 0, null, "")));
            return;
        }

        var rotation = ((FovRotationDegrees % 360) + 360) % 360;
        var label = DisplayNames.Format(Strings.SkyFovLabelFormat, FocalLengthMm, FormatFov(fov));
        Send(SkyMessageCodec.SetFov(new FovPayload(true, fov.WidthDegrees, fov.HeightDegrees, rotation >= 360 ? 0 : rotation,
            _fovCenter is { } center ? SkyBridgeMapper.ToVector(center) : null, label)));
    }

    /// <summary>The user dragged or rotated the frame in the renderer: remember it without echoing it back.</summary>
    private void OnRendererFovChanged(FovChangedPayload fov)
    {
        _fovCenter = new Vector3d(fov.X, fov.Y, fov.Z);
        _applyingRendererFov = true;
        try
        {
            FovRotationDegrees = Math.Round(fov.RotationDegrees, 1);
        }
        finally
        {
            _applyingRendererFov = false;
        }

        OnPropertyChanged(nameof(FovCenterText));
    }

    // ---- sky brightness -----------------------------------------------------------------------------

    /// <summary>Zenith sky brightness at the sky time (site + twilight + Moon), for the side panel.</summary>
    [ObservableProperty]
    public partial string? SkyBrightnessText { get; private set; }

    /// <summary>Where the site's dark-sky brightness comes from (manual SQM or a natural sky).</summary>
    public string SiteSqmText => SiteSqm is { } sqm
        ? FeasibilityText.SiteSky(sqm, _context.ZenithSqmSource)
        : Strings.SkySiteSqmUnknown;

    private void UpdateSkyBrightnessText()
    {
        OnPropertyChanged(nameof(SiteSqmText));
        if (_context.InputError is not null)
        {
            return;
        }

        var now = SkyAppearanceCalculator.At(_context.Observer, _clock.UtcNow, SiteSqm);
        SkyBrightnessText = DisplayNames.Format(Strings.SkyBrightnessFormat, now.ZenithSqm, now.LimitingMagnitude);
    }

    // ---- layers -----------------------------------------------------------------------------------

    private bool Layer(string key) => LayerToggles?.FirstOrDefault(t => t.Key == key)?.IsEnabled ?? true;

    private SkyLayers Layers() => new(
        Stars: Layer("stars"),
        ConstellationLines: Layer("constellationLines"),
        ConstellationBoundaries: Layer("constellationBoundaries"),
        ConstellationNames: Layer("constellationNames"),
        AltAzGrid: Layer("altAzGrid"),
        EquatorialGrid: Layer("equatorialGrid"),
        Ecliptic: Layer("ecliptic"),
        Meridian: Layer("meridian"),
        MilkyWay: Layer("milkyWay"),
        DeepSky: Layer("deepSky"),
        Bodies: Layer("bodies"),
        Labels: Layer("labels"),
        Ground: Layer("ground"),
        SkyBrightness: Layer("skyBrightness"),
        Satellites: Layer("satellites"),
        StarLimitMagnitude: Math.Clamp(StarLimitMagnitude, 3, 9.5),
        DeepSkyLimitMagnitude: _settings.DeepSkyLimitMagnitude);

    private void OnLayersChanged()
    {
        if (LayerToggles is null)
        {
            return; // still constructing
        }

        var layers = Layers();
        _settings.Stars = layers.Stars;
        _settings.ConstellationLines = layers.ConstellationLines;
        _settings.ConstellationBoundaries = layers.ConstellationBoundaries;
        _settings.ConstellationNames = layers.ConstellationNames;
        _settings.AltAzGrid = layers.AltAzGrid;
        _settings.EquatorialGrid = layers.EquatorialGrid;
        _settings.Ecliptic = layers.Ecliptic;
        _settings.Meridian = layers.Meridian;
        _settings.MilkyWay = layers.MilkyWay;
        _settings.DeepSky = layers.DeepSky;
        _settings.Bodies = layers.Bodies;
        _settings.Labels = layers.Labels;
        _settings.Ground = layers.Ground;
        _settings.SkyBrightness = layers.SkyBrightness;
        _settings.Satellites = layers.Satellites;
        _settings.StarLimitMagnitude = layers.StarLimitMagnitude;
        SaveSettings();
        Send(SkyMessageCodec.SetLayers(layers));
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings.Clone());
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Data.Common.DbException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogError("Could not save the sky view settings.", ex);
        }
    }

    public void Dispose()
    {
        _resyncTimer?.Dispose();
        _resyncTimer = null;
        Detach();
    }
}
