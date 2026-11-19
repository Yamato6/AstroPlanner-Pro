namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Fallback observing window (18:00 → 06:00 local time) and search for altitude crossings in
/// a series of samples.
/// </summary>
public static class ObservationNight
{
    public const int StartHour = 18;

    public static readonly TimeSpan Duration = TimeSpan.FromHours(12);

    /// <summary>
    /// Start (18:00 local) of the night containing <paramref name="localTime"/>. Before noon
    /// it belongs to the night that started the day before, so that a time such as 02:00
    /// falls inside its own night.
    /// </summary>
    public static DateTime GetNightStartLocal(DateTime localTime) =>
        (localTime.Hour < 12 ? localTime.Date.AddDays(-1) : localTime.Date).AddHours(StartHour);

    /// <summary>
    /// First time the altitude crosses <paramref name="altitudeDegrees"/> upwards
    /// (<paramref name="rising"/> = true) or downwards, interpolating linearly between the two
    /// samples around the crossing. Null if there is no crossing.
    /// </summary>
    public static DateTime? FindCrossing(
        IReadOnlyList<(DateTime Time, double AltitudeDegrees)> samples,
        double altitudeDegrees,
        bool rising)
    {
        ArgumentNullException.ThrowIfNull(samples);

        for (var index = 1; index < samples.Count; index++)
        {
            var previous = samples[index - 1];
            var current = samples[index];
            var previousDelta = previous.AltitudeDegrees - altitudeDegrees;
            var currentDelta = current.AltitudeDegrees - altitudeDegrees;

            if ((rising && previousDelta < 0 && currentDelta >= 0)
                || (!rising && previousDelta >= 0 && currentDelta < 0))
            {
                var fraction = previousDelta / (previousDelta - currentDelta);
                return previous.Time.AddMinutes((current.Time - previous.Time).TotalMinutes * fraction);
            }
        }

        return null;
    }
}
