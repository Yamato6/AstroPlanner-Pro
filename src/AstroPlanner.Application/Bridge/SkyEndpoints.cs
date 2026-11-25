using System.ComponentModel;
using System.Globalization;
using AstroPlanner.Application.Services;
using AstroPlanner.Application.ViewModels;
using AstroPlanner.SkyBridge;
using AstroPlanner.SkyBridge.App;

namespace AstroPlanner.Application.Bridge;

/// <summary>
/// The <c>sky.*</c> area: time (play, pause, now, step, set, rate), layers, view mode and the current
/// selection, through the same view models as the classic Sky tab. Every command answers with
/// the new <see cref="SkySnapshot"/>; <c>sky.changed</c> announces changes made elsewhere.
/// </summary>
public sealed class SkyEndpoints : IEndpointModule
{
    private readonly SkyViewModel _sky;
    private readonly TargetsViewModel _targets;
    private readonly IObserverContext _context;
    private readonly ChangeFeed _changes;
    private RpcDispatcher? _rpc;

    public SkyEndpoints(ShellViewModel shell, IUiDispatcher ui, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _sky = shell.Sky;
        _targets = shell.Targets;
        _context = shell.Observer;
        _changes = new ChangeFeed(ui, time, revision => _rpc?.Publish(AppEvents.SkyChanged, new SkyChangedEvent(revision)));
    }

    /// <summary>The revision of the snapshot <c>sky.get</c> returns now.</summary>
    public int Revision => _changes.Revision;

    public void Register(RpcDispatcher rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        rpc.Register(AppMethods.SkyGet, (_, _) => Answer());
        rpc.Register(AppMethods.SkyPlay, (_, _) => Run(() => _sky.Time.PlayCommand.Execute(null)));
        rpc.Register(AppMethods.SkyPause, (_, _) => Run(() => _sky.Time.PauseCommand.Execute(null)));
        rpc.Register(AppMethods.SkyNow, (_, _) => Run(() => _sky.Time.NowCommand.Execute(null)));
        rpc.Register(AppMethods.SkyStep, (request, _) =>
            Run(() => _sky.Time.StepCommand.Execute(request.Minutes.ToString(CultureInfo.InvariantCulture))));
        rpc.Register(AppMethods.SkySetRate, (request, _) => Run(() => _sky.Time.SelectedRate = NearestRate(request.Rate)));
        rpc.Register(AppMethods.SkySetLayer, (request, _) => Run(() =>
        {
            if (_sky.Layers.LayerToggles.FirstOrDefault(t => t.Key == request.Key) is { } toggle)
            {
                toggle.IsEnabled = request.Enabled;
            }
        }));
        rpc.Register(AppMethods.SkySetViewMode, (request, _) => Run(() => _sky.Modes.SetViewMode(request.Mode)));
        rpc.Register(AppMethods.SkySetTime, (request, _) =>
            Run(() => _sky.Time.SetTime(TimeOf(request.UtcMilliseconds))));

        _sky.PropertyChanged += OnChanged;
        _sky.Time.PropertyChanged += OnChanged;
        _sky.Layers.PropertyChanged += OnChanged;
        _sky.Modes.PropertyChanged += OnChanged;
        _targets.PropertyChanged += OnTargetsChanged;
        _context.PropertyChanged += OnContextChanged;
        foreach (var toggle in _sky.Layers.LayerToggles)
        {
            toggle.PropertyChanged += OnChanged;
        }
    }

    /// <summary>The current state as a contract (read on the UI thread).</summary>
    public SkySnapshot Snapshot()
    {
        var time = _sky.Time;
        var utc = time.IsPlaying ? time.Clock.UtcNow : _context.ToUtc(_context.ObservationDateTime);
        return new SkySnapshot(
            _changes.Revision,
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds(),
            time.IsPlaying ? (int)time.SelectedRate : 0,
            time.IsPlaying,
            [.. time.Rates.Select(rate => (int)rate)],
            _sky.Modes.ViewMode,
            [.. _sky.Layers.LayerToggles.Select(t => new SkyLayerState(t.Key, t.Name, t.IsEnabled))],
            Math.Clamp(_sky.Layers.StarLimitMagnitude, -2, 20),
            Selection());
    }

    private SkySelection? Selection()
    {
        if (_sky.SkyOnlySelectionText is { Length: > 0 } skyOnly)
        {
            return new SkySelection(null, Truncate(skyOnly, 200), null);
        }

        return _targets.SelectedObject is { } row
            ? new SkySelection(Truncate(row.Id, 64), Truncate(row.Title, 200), Truncate(DisplayNames.Of(row.Type), 80))
            : null;
    }

    private Task<SkySnapshot> Answer() => Task.FromResult(Snapshot());

    /// <summary>Runs a command on the view models, then answers with the state it left.</summary>
    private Task<SkySnapshot> Run(Action command)
    {
        command();
        return Answer();
    }

    /// <summary>The instant, limited to the years the ephemerides are good for.</summary>
    internal static DateTime TimeOf(long utcMilliseconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds(Math.Clamp(utcMilliseconds, EarliestUtcMilliseconds, LatestUtcMilliseconds)).UtcDateTime;

    private static readonly long EarliestUtcMilliseconds = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly long LatestUtcMilliseconds = new DateTimeOffset(2100, 12, 31, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private int NearestRate(int rate) =>
        (int)_sky.Time.Rates.MinBy(candidate => Math.Abs(Math.Log(candidate) - Math.Log(rate)));

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => _changes.Invalidate();

    private void OnTargetsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetsViewModel.SelectedObject))
        {
            _changes.Invalidate();
        }
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IObserverContext.ObservationDateTime) or nameof(IObserverContext.TimeZoneId))
        {
            _changes.Invalidate();
        }
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    public void Dispose()
    {
        _sky.PropertyChanged -= OnChanged;
        _sky.Time.PropertyChanged -= OnChanged;
        _sky.Layers.PropertyChanged -= OnChanged;
        _sky.Modes.PropertyChanged -= OnChanged;
        _targets.PropertyChanged -= OnTargetsChanged;
        _context.PropertyChanged -= OnContextChanged;
        foreach (var toggle in _sky.Layers.LayerToggles)
        {
            toggle.PropertyChanged -= OnChanged;
        }

        _changes.Dispose();
        _rpc = null;
    }
}
