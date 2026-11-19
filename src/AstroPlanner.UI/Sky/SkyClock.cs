namespace AstroPlanner.UI.Sky;

/// <summary>
/// Time of the sky view: an instant plus a rate (1 = real time, 0 = paused, 3600 = one hour per
/// second). The renderer advances the same clock locally; this one is the reference C# re-syncs
/// it with.
/// </summary>
public sealed class SkyClock(TimeProvider time)
{
    private DateTime _baseUtc = time.GetUtcNow().UtcDateTime;
    private DateTimeOffset _baseReal = time.GetUtcNow();

    public double Rate { get; private set; }

    /// <summary>Current sky time (UTC).</summary>
    public DateTime UtcNow
    {
        get
        {
            var elapsed = (time.GetUtcNow() - _baseReal).TotalSeconds * Rate;
            return DateTime.SpecifyKind(_baseUtc.AddSeconds(elapsed), DateTimeKind.Utc);
        }
    }

    public void Set(DateTime utc, double rate)
    {
        _baseUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        _baseReal = time.GetUtcNow();
        Rate = rate;
    }
}
