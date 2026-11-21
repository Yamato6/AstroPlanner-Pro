using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Sky;

namespace AstroPlanner.Core.Planning;

/// <summary>The Milky Way core during one night.</summary>
/// <param name="Date">The evening the night starts.</param>
/// <param name="CoreInDarkness">Time the core is above the minimum altitude in astronomical darkness.</param>
/// <param name="CoreMoonFree">Of that, the time with the Moon below the horizon.</param>
/// <param name="StartLocal">Start of the core's dark window (null when there is none).</param>
/// <param name="EndLocal">End of that window.</param>
/// <param name="MoonIlluminatedFraction">Moon illumination at midnight (0–1).</param>
public sealed record MilkyWayNight(
    DateOnly Date,
    TimeSpan CoreInDarkness,
    TimeSpan CoreMoonFree,
    DateTime? StartLocal,
    DateTime? EndLocal,
    double MoonIlluminatedFraction)
{
    /// <summary>At least half an hour of core in a dark, Moon-free sky.</summary>
    public bool IsGood => CoreMoonFree >= MilkyWayPlanner.GoodNightMinimum;
}

/// <summary>
/// When the Milky Way core can be photographed (spec §4.1, §6): for each night, how long the
/// galactic centre stays above a minimum altitude in astronomical darkness, and how much of that
/// time the Moon is down. Sampled every <see cref="Step"/>.
/// </summary>
public static class MilkyWayPlanner
{
    public const double DefaultMinimumAltitudeDegrees = 10;
    public static readonly TimeSpan Step = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan GoodNightMinimum = TimeSpan.FromMinutes(30);

    public static MilkyWayNight Night(
        IAstronomyService astronomy, ObserverLocation observer, TimeZoneInfo timeZone, DateOnly date,
        double minimumAltitudeDegrees = DefaultMinimumAltitudeDegrees)
    {
        ArgumentNullException.ThrowIfNull(astronomy);
        ArgumentNullException.ThrowIfNull(observer);
        ArgumentNullException.ThrowIfNull(timeZone);

        var night = astronomy.GetNightInfo(observer, date.ToDateTime(new TimeOnly(22, 0)), timeZone);
        var midnightUtc = TimeZoneConversion.ToUtc(date.ToDateTime(TimeOnly.MinValue).AddDays(1), timeZone);
        var moonFraction = astronomy.GetMoon(observer, midnightUtc).IlluminatedFraction;
        if (!night.HasAstronomicalDarkness)
        {
            return new MilkyWayNight(date, TimeSpan.Zero, TimeSpan.Zero, null, null, moonFraction);
        }

        var dark = TimeSpan.Zero;
        var moonFree = TimeSpan.Zero;
        DateTime? start = null;
        DateTime? end = null;
        var endLocal = night.AstronomicalDawnLocal!.Value;
        for (var local = night.AstronomicalDuskLocal!.Value; local < endLocal; local += Step)
        {
            var slice = endLocal - local < Step ? endLocal - local : Step;
            var utc = TimeZoneConversion.ToUtc(local + (slice / 2), timeZone);
            if (astronomy.GetHorizontal(GalacticCenter.Target, observer, utc).AltitudeDegrees < minimumAltitudeDegrees)
            {
                continue;
            }

            dark += slice;
            start ??= local;
            end = local + slice;
            if (astronomy.GetMoon(observer, utc).Horizontal.AltitudeDegrees < 0)
            {
                moonFree += slice;
            }
        }

        return new MilkyWayNight(date, dark, moonFree, start, end, moonFraction);
    }

    /// <summary>Every night from <paramref name="firstDate"/> for <paramref name="days"/> days.</summary>
    public static IReadOnlyList<MilkyWayNight> Nights(
        IAstronomyService astronomy, ObserverLocation observer, TimeZoneInfo timeZone, DateOnly firstDate, int days = 365,
        double minimumAltitudeDegrees = DefaultMinimumAltitudeDegrees, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(days);
        var nights = new MilkyWayNight[days];
        Parallel.For(0, days, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            nights[i] = Night(astronomy, observer, timeZone, firstDate.AddDays(i), minimumAltitudeDegrees));
        return nights;
    }

    /// <summary>The first good night (core in a dark, Moon-free sky) on or after the first of <paramref name="nights"/>.</summary>
    public static MilkyWayNight? NextGood(IReadOnlyList<MilkyWayNight> nights) => nights.FirstOrDefault(n => n.IsGood);
}
