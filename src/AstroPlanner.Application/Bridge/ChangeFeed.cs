namespace AstroPlanner.Application.Bridge;

/// <summary>
/// Turns a burst of changes into one event. Every <see cref="Invalidate"/> bumps
/// <see cref="Revision"/>; the first of a burst starts a <see cref="DefaultDelay"/> timer, and when
/// it fires the latest revision is published once, on the UI thread. The page reacts by fetching
/// a fresh snapshot (<c>x.get</c>) when its copy is older, so no change is lost and a busy view
/// model (a slider, a recalculation that touches ten properties) costs one round trip.
/// </summary>
public sealed class ChangeFeed : IDisposable
{
    /// <summary>Changes closer together than this are announced once.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(50);

    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly TimeSpan _delay;
    private readonly Action<int> _publish;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private int _revision;
    private bool _disposed;

    /// <param name="publish">Sends the change event with the revision (called on the UI thread).</param>
    public ChangeFeed(IUiDispatcher ui, TimeProvider time, Action<int> publish, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(publish);
        _ui = ui;
        _time = time;
        _publish = publish;
        _delay = delay ?? DefaultDelay;
    }

    /// <summary>The revision of the current state: 0 until the first change, then 1, 2… (wraps to 1 after int.MaxValue).</summary>
    public int Revision
    {
        get
        {
            lock (_gate)
            {
                return _revision;
            }
        }
    }

    /// <summary>The state changed: a new revision, announced after the delay. Safe on any thread.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _revision = _revision == int.MaxValue ? 1 : _revision + 1;
            _timer ??= _time.CreateTimer(static state => ((ChangeFeed)state!).OnTimer(), this, _delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimer() => _ui.Post(Flush);

    private void Flush()
    {
        int revision;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_disposed)
            {
                return;
            }

            revision = _revision;
        }

        _publish(revision);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
