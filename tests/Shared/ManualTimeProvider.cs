namespace AstroPlanner.Testing;

/// <summary>
/// Manual clock: tests control the current time and fire timers explicitly
/// (no waiting for real minutes).
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public ManualTimer? LastTimer { get; private set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        LastTimer = new ManualTimer(callback, state);

    internal sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public bool IsDisposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() => IsDisposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
