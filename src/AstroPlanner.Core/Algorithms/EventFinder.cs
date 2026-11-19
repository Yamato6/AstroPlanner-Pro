namespace AstroPlanner.Core.Algorithms;

/// <summary>
/// Finds event times (threshold crossings and maxima) of a smooth function of time such as an
/// altitude. The function is first sampled coarsely to bracket the events, then each one is
/// refined: crossings by bisection and the maximum by golden-section search, both down to
/// <see cref="Precision"/>. With the default 10-minute step this finds every event that is not
/// shorter than the step (e.g. an object that rises and sets again within 10 minutes).
/// </summary>
public static class EventFinder
{
    /// <summary>Coarse sampling step used to bracket the events.</summary>
    public static readonly TimeSpan DefaultStep = TimeSpan.FromMinutes(10);

    /// <summary>Width of the final bracket around each refined event.</summary>
    public static readonly TimeSpan Precision = TimeSpan.FromSeconds(1);

    private static readonly double GoldenRatio = (Math.Sqrt(5) - 1) / 2;

    /// <summary>
    /// Samples <paramref name="function"/> from <paramref name="start"/> to
    /// <paramref name="end"/> every <paramref name="step"/>; both ends are included.
    /// </summary>
    public static IReadOnlyList<(DateTime Time, double Value)> Sample(
        Func<DateTime, double> function,
        DateTime start,
        DateTime end,
        TimeSpan step)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (step <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(step), "The step must be positive.");
        }

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "The end must not be before the start.");
        }

        var samples = new List<(DateTime Time, double Value)>();
        for (var time = start; time < end; time += step)
        {
            samples.Add((time, function(time)));
        }

        samples.Add((end, function(end)));
        return samples;
    }

    /// <summary>
    /// First time <paramref name="function"/> crosses <paramref name="threshold"/> upwards
    /// (<paramref name="rising"/> = true) or downwards, bracketed by
    /// <paramref name="samples"/> (taken from the same function) and refined by bisection.
    /// Null if there is no crossing.
    /// </summary>
    public static DateTime? FindCrossing(
        Func<DateTime, double> function,
        IReadOnlyList<(DateTime Time, double Value)> samples,
        double threshold,
        bool rising)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(samples);

        for (var index = 1; index < samples.Count; index++)
        {
            var below = samples[index - 1].Value < threshold;
            var above = samples[index].Value >= threshold;
            if (rising ? below && above : !below && !above)
            {
                return Bisect(function, samples[index - 1].Time, samples[index].Time, threshold, rising);
            }
        }

        return null;
    }

    /// <summary>
    /// Time and value of the maximum of <paramref name="function"/> within the sampled range:
    /// the highest sample, refined by golden-section search between its neighbours. At the
    /// edges of the range the maximum may be the edge itself (e.g. an object that is setting
    /// when the night starts).
    /// </summary>
    public static (DateTime Time, double Value) FindMaximum(
        Func<DateTime, double> function,
        IReadOnlyList<(DateTime Time, double Value)> samples)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count == 0)
        {
            throw new ArgumentException("At least one sample is needed.", nameof(samples));
        }

        var best = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            if (samples[index].Value > samples[best].Value)
            {
                best = index;
            }
        }

        if (samples.Count == 1)
        {
            return samples[0];
        }

        var low = samples[Math.Max(best - 1, 0)].Time;
        var high = samples[Math.Min(best + 1, samples.Count - 1)].Time;
        var refined = GoldenSectionMaximum(function, low, high);

        return refined.Value > samples[best].Value ? refined : samples[best];
    }

    private static DateTime Bisect(Func<DateTime, double> function, DateTime low, DateTime high, double threshold, bool rising)
    {
        // Invariant: the crossing is in [low, high]; at low the function is on the "before" side.
        while (high - low > Precision)
        {
            var middle = low + (high - low) / 2;
            var isAbove = function(middle) >= threshold;
            if (isAbove == rising)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return low + (high - low) / 2;
    }

    private static (DateTime Time, double Value) GoldenSectionMaximum(Func<DateTime, double> function, DateTime low, DateTime high)
    {
        var a = low;
        var b = high;
        var c = b - (b - a) * GoldenRatio;
        var d = a + (b - a) * GoldenRatio;
        var fc = function(c);
        var fd = function(d);

        while (b - a > Precision)
        {
            if (fc > fd)
            {
                b = d;
                d = c;
                fd = fc;
                c = b - (b - a) * GoldenRatio;
                fc = function(c);
            }
            else
            {
                a = c;
                c = d;
                fc = fd;
                d = a + (b - a) * GoldenRatio;
                fd = function(d);
            }
        }

        var time = a + (b - a) / 2;
        return (time, function(time));
    }
}
