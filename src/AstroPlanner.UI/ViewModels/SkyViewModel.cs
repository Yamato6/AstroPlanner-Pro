using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;
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
    private readonly SkyClock _clock;
    private readonly SkyViewSettings _settings;
    private ISkyChannel? _channel;
    private bool _rendererReady;
    private DateTime? _frameEpochUtc;
    private DateTime? _tracksCenterUtc;
    private string? _sentSelectionId;
    private ITimer? _resyncTimer;
    private SynchronizationContext? _uiContext;

    public SkyViewModel(
        IObserverContext context,
        TargetsViewModel targets,
        SessionPlanViewModel plan,
        ISkyViewStore store,
        IAppLogger logger,
        TimeProvider? time = null)
    {
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
            Toggle("labels", Strings.SkyLayerLabels, _settings.Labels),
            Toggle("altAzGrid", Strings.SkyLayerAltAzGrid, _settings.AltAzGrid),
            Toggle("equatorialGrid", Strings.SkyLayerEquatorialGrid, _settings.EquatorialGrid),
            Toggle("ecliptic", Strings.SkyLayerEcliptic, _settings.Ecliptic),
            Toggle("meridian", Strings.SkyLayerMeridian, _settings.Meridian),
            Toggle("ground", Strings.SkyLayerGround, _settings.Ground)
        ];

        _targets.SelectionOrTableChanged += (_, _) =>
        {
            MapVersion++;
            SendSelectionIfChanged();
            OnPropertyChanged(nameof(SelectedText));
        };
        _plan.Changed += (_, _) =>
        {
            MapVersion++;
            SendHighlights();
        };
        _context.Recalculated += (_, _) => OnContextRecalculated();
        _context.PropertyChanged += OnContextPropertyChanged;
        SyncClockToContext();
    }

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

        if (_tracksCenterUtc is null || (utc - _tracksCenterUtc.Value).Duration() > TrackHalfWindow - TimeSpan.FromHours(1))
        {
            SendBodies(utc);
        }

        SendTime();
        OnPropertyChanged(nameof(SkyTimeText));
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
            // viewDome / viewAllSky / toggleFov arrive with the 2D charts and the FOV overlay (M2b).
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
            SkyOnlySelectionText = HoverText ?? BodyName(selection.Id);
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
            $"https://{SkyHost.DataHost}/", RefractionModel.Standard, Layers(), SkyViewModes.Dome, "dark", ShowStats)));
        SendSiteAndTime();
        Send(SkyMessageCodec.CenterOn(CenterOnView()));
        SendHighlights();
        _sentSelectionId = null;
        SendSelectionIfChanged();
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
        Send(SkyMessageCodec.MovingBodies(SkyBridgeMapper.MovingBodies(
            _context.Observer, utc, TrackHalfWindow, TrackStep, BodyDisplayName)));
    }

    private string BodyDisplayName(string id) =>
        _targets.AllObjects.FirstOrDefault(o => o.Id == id)?.Name ?? BodyName(id);

    private void SendTime() =>
        Send(SkyMessageCodec.SetTime(new TimePayload(SkyMessageCodec.FormatUtc(_clock.UtcNow), _clock.Rate)));

    private void SendHighlights() =>
        Send(SkyMessageCodec.SetHighlights(new HighlightsPayload([.. _plan.Items.Select(item => item.Id)])));

    private void SendSelectionIfChanged()
    {
        var selected = _targets.SelectedObject;
        var id = selected?.Id;
        if (id == _sentSelectionId || !_rendererReady)
        {
            return;
        }

        _sentSelectionId = id;
        if (selected is not null)
        {
            SkyOnlySelectionText = null;
        }

        Send(SkyMessageCodec.SelectObject(selected is null
            ? new SelectionPayload(null, null)
            : new SelectionPayload(selected.IsPlanet ? SkyObjectKinds.Body : SkyObjectKinds.DeepSky, selected.Id)));
        OnPropertyChanged(nameof(HasCatalogSelection));
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

    /// <summary>Finds an object by id or name ("m31", "ngc 7000", "Orion", "Jupiter"), selects it and centers the view.</summary>
    [RelayCommand]
    private void Search()
    {
        var query = CatalogSearch.Parse(SearchText);
        if (query.IsEmpty)
        {
            return;
        }

        // An exact name ("Jupiter", "Andromeda Galaxy") wins over objects that only contain the text.
        var matches = _targets.AllObjects.Where(item => CatalogSearch.Matches(item, query)).ToList();
        var match = matches.FirstOrDefault(item => string.Equals(item.Name, SearchText.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? matches.FirstOrDefault();
        if (match is null)
        {
            SearchMessage = DisplayNames.Format(Strings.SkySearchNotFoundFormat, SearchText.Trim());
            return;
        }

        SearchMessage = null;
        _targets.SelectById(match.Id);
        CenterOn(match.Target, fovDegrees: match.IsPlanet ? 20 : 30);
    }

    private void CenterOn(CelestialObject target, double? fovDegrees)
    {
        if (target.Planet is not null)
        {
            var utc = _clock.UtcNow;
            var body = MovingBodyCalculator.GetBodies(_context.Observer, utc).FirstOrDefault(b => b.Id == target.Id);
            if (body is null)
            {
                return;
            }

            var v = SkyFrame.Compute(_context.Observer, utc).J2000ToHorizontalAt(utc).Transpose()
                .Transform(SkyFrame.FromHorizontal(body.Horizontal));
            Send(SkyMessageCodec.CenterOn(new CenterOnPayload(v.X, v.Y, v.Z, fovDegrees)));
        }
        else
        {
            Send(SkyMessageCodec.CenterOn(SkyBridgeMapper.CenterOn(target.Coordinates, fovDegrees)));
        }
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
