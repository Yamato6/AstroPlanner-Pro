using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;
using AstroPlanner.SkyBridge;
using AstroPlanner.Application.Resources;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.Sky;

namespace AstroPlanner.Application.ViewModels;

/// <summary>
/// The sky tab (spec §4.2, §5.3). Coordinates the WebView2 renderer through an
/// <see cref="ISkyChannel"/> and its parts: <see cref="Time"/> (the sky clock), <see cref="Layers"/>,
/// <see cref="Camera"/> (the camera frame), <see cref="Search"/>, <see cref="Modes"/> (dome, all-sky,
/// finder chart) and <see cref="Map"/> (the 2D chart without WebView2); the time-dependent messages
/// go through <see cref="SkyBodyFeed"/>. It keeps the selection in step with Targets and shows the
/// renderer's state, hover, statistics and the sky brightness.
/// </summary>
public sealed partial class SkyViewModel : ObservableObject, IDisposable
{
    private readonly SkySession _session;
    private readonly SkyBodyFeed _feed;
    private readonly TargetsViewModel _targets;
    private readonly SessionPlanViewModel _plan;
    private readonly ThemeState? _theme;
    private string? _sentSelectionId;

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
        SatelliteState? satellites = null,
        ThemeState? theme = null)
    {
        _targets = targets;
        _plan = plan;
        _theme = theme;
        _session = new SkySession(context, store, logger, time ?? TimeProvider.System);
        _feed = new SkyBodyFeed(_session, () => _targets.AllObjects.Select(row => row.Target), satellites);
        Map = new SkyMapViewModel(context, () => _targets.Objects, () => _targets.SelectedObject,
            row => _targets.SelectedObject = row, () => _plan.Items.Select(item => item.Id));
        Layers = new SkyLayersViewModel(_session);
        Camera = new SkyCameraFrameViewModel(_session, equipment, SelectedDirection);
        Search = new SkySearchViewModel(_session, () => _targets.AllObjects, _targets.SelectById, CenterOn, nameResolver, online);
        Modes = new SkyModesViewModel(_session, () => Camera.IsFovVisible ? Camera.CameraFov : null,
            () => SelectedDirection() ?? Camera.Center, () => _targets.SelectedObject?.Id);
        Time = new SkyTimeViewModel(_session, _feed);
        Time.Resynced += (_, _) => UpdateSkyBrightnessText();

        theme?.Changed += (_, _) => _session.Send(SkyMessageCodec.SetTheme(new ThemePayload(theme.SkyTheme)));
        _targets.SmallBodiesChanged += (_, _) =>
        {
            if (Context.InputError is null)
            {
                _feed.SendBodies(_session.Clock.UtcNow);
            }
        };
        _targets.SelectionOrTableChanged += (_, _) =>
        {
            Map.Invalidate();
            var changed = SendSelectionIfChanged();
            OnPropertyChanged(nameof(SelectedText));
            if (changed)
            {
                FollowSelection();
            }
        };
        _plan.Changed += (_, _) =>
        {
            Map.Invalidate();
            SendHighlights();
        };
        satellites?.Changed += (_, _) =>
        {
            if (Context.InputError is null && _feed.SatellitesChanged)
            {
                _feed.SendBodies(_session.Clock.UtcNow);
            }
        };
        Context.Recalculated += (_, _) => OnContextRecalculated();
        Context.PropertyChanged += OnContextPropertyChanged;
    }

    private IObserverContext Context => _session.Context;

    /// <summary>The sky clock: play, pause, step and rate.</summary>
    public SkyTimeViewModel Time { get; }

    /// <summary>The visible layers, the star limit and the statistics overlay.</summary>
    public SkyLayersViewModel Layers { get; }

    /// <summary>The camera frame drawn on the sky.</summary>
    public SkyCameraFrameViewModel Camera { get; }

    /// <summary>The search box (catalog, then CDS Sesame).</summary>
    public SkySearchViewModel Search { get; }

    /// <summary>The view modes: 3D dome, all-sky chart and finder chart.</summary>
    public SkyModesViewModel Modes { get; }

    /// <summary>The 2D chart shown without WebView2.</summary>
    public SkyMapViewModel Map { get; }

    // ---- renderer state -------------------------------------------------------------------------

    /// <summary>True while the WebView2 renderer is in use; false shows the 2D chart.</summary>
    [ObservableProperty]
    public partial bool IsRendererAvailable { get; set; } = true;

    /// <summary>Why the renderer is not available (shown above the 2D chart).</summary>
    [ObservableProperty]
    public partial string? RendererMessage { get; private set; }

    /// <summary>Object under the pointer in the renderer.</summary>
    [ObservableProperty]
    public partial string? HoverText { get; private set; }

    /// <summary>Picked object that is not in the catalog (Sun, Moon, a star): name shown in the side panel.</summary>
    [ObservableProperty]
    public partial string? SkyOnlySelectionText { get; private set; }

    /// <summary>Last frame statistics reported by the renderer.</summary>
    [ObservableProperty]
    public partial string? StatsText { get; private set; }

    /// <summary>Raised when the renderer asks for the search box (the "/" shortcut).</summary>
    public event EventHandler? FocusSearchRequested;

    /// <summary>Raised when the user asks to see the selected object's details.</summary>
    public event EventHandler? OpenDetailsRequested;

    public string SelectedText => SkyOnlySelectionText
        ?? (_targets.SelectedObject is { } selected
            ? DisplayNames.Format(Strings.SkySelectedFormat, selected.Title)
            : Strings.SkyNothingSelected);

    public bool HasCatalogSelection => _targets.SelectedObject is not null && SkyOnlySelectionText is null;

    // ---- channel --------------------------------------------------------------------------------

    /// <summary>Connects the renderer; nothing is sent until it reports <c>ready</c>.</summary>
    public void Attach(ISkyChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        Detach();
        _session.Channel = channel;
        _session.IsReady = false;
        channel.MessageReceived += OnMessage;
        IsRendererAvailable = true;
        RendererMessage = null;
    }

    public void Detach()
    {
        _session.Channel?.MessageReceived -= OnMessage;
        _session.Channel = null;
        _session.IsReady = false;
    }

    /// <summary>The renderer cannot run here: show the 2D chart and say why.</summary>
    public void UseFallback(string reason)
    {
        Detach();
        IsRendererAvailable = false;
        RendererMessage = reason == "runtime" ? Strings.SkyRendererNoRuntime : Strings.SkyRendererNotBuilt;
        Map.Invalidate();
    }

    private void OnMessage(object? sender, IncomingSkyMessage message)
    {
        switch (message.Payload)
        {
            case ReadyPayload ready:
                _session.Logger.LogInfo($"Sky renderer {ready.RendererVersion} ready (WebGL {ready.WebGlVersion}).");
                _session.IsReady = true;
                SendAll();
                break;
            case FirstFramePayload first:
                _session.Logger.LogInfo(string.Create(CultureInfo.InvariantCulture, $"Sky renderer: first frame after {first.Milliseconds:0} ms."));
                break;
            case SelectionPayload selection:
                OnRendererSelection(selection);
                break;
            case HoverPayload hover:
                HoverText = hover.Label ?? hover.Id;
                break;
            case ViewChangedPayload view:
                _session.Settings.AzimuthDegrees = view.AzimuthDegrees;
                _session.Settings.AltitudeDegrees = view.AltitudeDegrees;
                _session.Settings.FovDegrees = view.FovDegrees;
                _session.SaveSettings();
                break;
            case StatsPayload stats:
                StatsText = string.Create(CultureInfo.InvariantCulture,
                    $"{stats.Fps:0} fps · p95 {stats.P95FrameMs:0.0} ms · {stats.Width}×{stats.Height} · {stats.Stars} stars");
                if (stats.Benchmark)
                {
                    _session.Logger.LogInfo("Sky benchmark: " + StatsText);
                }

                break;
            case ShortcutPayload shortcut:
                OnShortcut(shortcut.Action);
                break;
            case FovChangedPayload fov:
                Camera.OnRendererFovChanged(fov);
                break;
            case ErrorPayload error:
                _session.Logger.LogError("Sky renderer: " + error.Message);
                break;
        }
    }

    private void OnShortcut(string action)
    {
        switch (action)
        {
            case SkyShortcuts.TogglePause:
                Time.TogglePlayCommand.Execute(null);
                break;
            case SkyShortcuts.FocusSearch:
                FocusSearchRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SkyShortcuts.ViewDome:
                Modes.SetViewMode(SkyViewModes.Dome);
                break;
            case SkyShortcuts.ViewAllSky:
                Modes.SetViewMode(SkyViewModes.AllSky);
                break;
            case SkyShortcuts.ToggleFov:
                Camera.IsFovVisible = !Camera.IsFovVisible;
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

        // A star is picked by its HYG id; its catalog row (if bright enough to be one) has a HIP/HD id.
        var rowId = selection.Kind == SkyObjectKinds.Star
            ? int.TryParse(selection.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hygId) ? _targets.StarRowId(hygId) : null
            : selection.Id;
        _sentSelectionId = rowId ?? selection.Id;
        if ((selection.Kind is SkyObjectKinds.DeepSky or SkyObjectKinds.Body or SkyObjectKinds.Star) && rowId is not null
            && _targets.SelectById(rowId))
        {
            SkyOnlySelectionText = null;
        }
        else
        {
            SkyOnlySelectionText = _feed.SatelliteSelectionText(selection.Id) ?? HoverText ?? SkyBodyFeed.BodyName(selection.Id);
        }

        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(HasCatalogSelection));
    }

    private void SendAll()
    {
        _session.Send(SkyMessageCodec.Init(new InitPayload(
            $"https://{SkyHosts.DataHost}/", RefractionModel.Standard, Layers.Layers(), Modes.ViewMode, _theme?.SkyTheme ?? "dark", Layers.ShowStats)));
        SendSiteAndTime();
        _session.Send(SkyMessageCodec.CenterOn(CenterOnView()));
        SendHighlights();
        _sentSelectionId = null;
        SendSelectionIfChanged();
        Camera.CenterOnSelectionIfUnset();
        Camera.Send();
        if (Modes.IsFinderMode)
        {
            Modes.CenterFinder();
        }
    }

    private void SendSiteAndTime()
    {
        if (!_session.IsReady || Context.InputError is not null)
        {
            return;
        }

        _feed.SendSiteAndTime();
        UpdateSkyBrightnessText();
    }

    private void SendHighlights() =>
        _session.Send(SkyMessageCodec.SetHighlights(new HighlightsPayload([.. _plan.Items.Select(item => item.Id)])));

    /// <summary>Sends the Targets selection when it changed; true if it did.</summary>
    private bool SendSelectionIfChanged()
    {
        var selected = _targets.SelectedObject;
        var id = selected?.Id;
        if (id == _sentSelectionId || !_session.IsReady)
        {
            return false;
        }

        _sentSelectionId = id;
        if (selected is not null)
        {
            SkyOnlySelectionText = null;
        }

        _session.Send(SkyMessageCodec.SelectObject(selected is null
            ? new SelectionPayload(null, null)
            : selected.Target.Star is { } star
                ? new SelectionPayload(SkyObjectKinds.Star, star.HygId.ToString(CultureInfo.InvariantCulture))
                : new SelectionPayload(selected.Target.IsMoving ? SkyObjectKinds.Body : SkyObjectKinds.DeepSky, selected.Id)));
        OnPropertyChanged(nameof(HasCatalogSelection));
        return true;
    }

    /// <summary>Restores the last camera (azimuth/altitude → J2000 direction at the sky time).</summary>
    private CenterOnPayload CenterOnView()
    {
        var utc = _session.Clock.UtcNow;
        var frame = SkyFrame.Compute(Context.Observer, utc);
        var v = frame.J2000ToHorizontalAt(utc).Transpose().Transform(
            SkyFrame.FromHorizontal(new HorizontalCoordinates(_session.Settings.AltitudeDegrees, _session.Settings.AzimuthDegrees)));
        return new CenterOnPayload(v.X, v.Y, v.Z, _session.Settings.FovDegrees);
    }

    // ---- context --------------------------------------------------------------------------------

    private void OnContextRecalculated()
    {
        if (Time.IsPlaying || Context.InputError is not null)
        {
            return;
        }

        Time.SyncToContext();
        SendSiteAndTime();
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IObserverContext.EffectiveZenithSqm))
        {
            _feed.SendAppearance(_session.Clock.UtcNow);
            UpdateSkyBrightnessText();
        }
    }

    // ---- actions --------------------------------------------------------------------------------

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

    /// <summary>Nothing selected, in the sky or in Targets.</summary>
    public void ClearSelection()
    {
        SkyOnlySelectionText = null;
        _targets.SelectedObject = null;
        _sentSelectionId = null;
        if (_session.IsReady)
        {
            _session.Send(SkyMessageCodec.SelectObject(new SelectionPayload(null, null)));
        }

        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(HasCatalogSelection));
    }

    private void CenterOn(CelestialObject target, double? fovDegrees)
    {
        if (DirectionOf(target) is { } v)
        {
            var direction = SkyBridgeMapper.ToVector(v);
            _session.Send(SkyMessageCodec.CenterOn(new CenterOnPayload(direction.X, direction.Y, direction.Z, fovDegrees)));
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

        var utc = _session.Clock.UtcNow;
        var body = MovingBodyCalculator.GetBodies(Context.Observer, utc, target.IsSmallBody ? [target] : null)
            .FirstOrDefault(b => b.Id == target.Id);
        return body is null
            ? null
            : SkyFrame.Compute(Context.Observer, utc).J2000ToHorizontalAt(utc).Transpose().Transform(SkyFrame.FromHorizontal(body.Horizontal));
    }

    private Vector3d? SelectedDirection() =>
        _targets.SelectedObject is { } selected && Context.InputError is null ? DirectionOf(selected.Target) : null;

    /// <summary>A new selection moves the camera field onto it and recentres the finder chart.</summary>
    private void FollowSelection()
    {
        if (SelectedDirection() is not { } direction)
        {
            return;
        }

        Camera.MoveTo(direction);
        if (Modes.IsFinderMode)
        {
            Modes.CenterFinder();
        }
    }

    // ---- sky brightness -----------------------------------------------------------------------------

    /// <summary>Zenith sky brightness at the sky time (site + twilight + Moon), for the side panel.</summary>
    [ObservableProperty]
    public partial string? SkyBrightnessText { get; private set; }

    /// <summary>Where the site's dark-sky brightness comes from (manual SQM or a natural sky).</summary>
    public string SiteSqmText => _feed.SiteSqm is { } sqm
        ? FeasibilityText.SiteSky(sqm, Context.ZenithSqmSource)
        : Strings.SkySiteSqmUnknown;

    private void UpdateSkyBrightnessText()
    {
        OnPropertyChanged(nameof(SiteSqmText));
        if (Context.InputError is not null)
        {
            return;
        }

        var now = SkyAppearanceCalculator.At(Context.Observer, _session.Clock.UtcNow, _feed.SiteSqm);
        SkyBrightnessText = DisplayNames.Format(Strings.SkyBrightnessFormat, now.ZenithSqm, now.LimitingMagnitude);
    }

    public void Dispose()
    {
        Time.Dispose();
        Detach();
    }
}
