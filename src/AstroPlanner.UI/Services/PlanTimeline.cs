using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Planning;
using AstroPlanner.UI.ViewModels;

namespace AstroPlanner.UI.Services;

/// <summary>A time interval in local wall-clock time.</summary>
public readonly record struct LocalSpan(DateTime Start, DateTime End);

/// <summary>One row of the session timeline (one plan item, in priority order).</summary>
/// <param name="Usable">When the target could be imaged tonight (dark, above the limits).</param>
/// <param name="Block">Its scheduled block; null when it got none.</param>
/// <param name="CrossingsLocal">When sunlit satellites cross the rig's field during the block (null = none or not computed yet).</param>
public sealed record PlanTimelineRow(
    string Label,
    IReadOnlyList<LocalSpan> Usable,
    LocalSpan? Block,
    FeasibilityVerdict? Verdict,
    DateTime? MeridianFlipLocal,
    bool Locked,
    IReadOnlyList<DateTime>? CrossingsLocal = null);

/// <summary>A satellite crossing the field of a planned block, in local time, with its display line.</summary>
public sealed record PlanCrossing(string Name, DateTime EnterLocal, DateTime ExitLocal, double? Magnitude)
{
    public string Text => Magnitude is { } magnitude
        ? DisplayNames.Format(Resources.Strings.PlanCrossingFormat, Name, EnterLocal, ExitLocal, magnitude)
        : DisplayNames.Format(Resources.Strings.PlanCrossingNoMagnitudeFormat, Name, EnterLocal, ExitLocal);
}

/// <summary>
/// The scheduled night as the Plan tab and the PDF draw it (spec §4.6 timeline): the window
/// (sunset → sunrise), the not-dark bands (twilight by the chosen darkness), forecast clouds and
/// one row per target.
/// </summary>
public sealed record PlanTimeline(
    DateTime WindowStartLocal,
    DateTime WindowEndLocal,
    IReadOnlyList<LocalSpan> TwilightBands,
    IReadOnlyList<(LocalSpan Span, int CloudCoverPercent)> Clouds,
    IReadOnlyList<PlanTimelineRow> Rows)
{
    public static PlanTimeline Create(
        NightSkySamples night,
        TimeZoneInfo timeZone,
        IReadOnlyList<PlanItemViewModel> items,
        IReadOnlyList<TargetNightProfile> profiles,
        IReadOnlyList<(DateTime StartLocal, DateTime EndLocal, int CloudCoverPercent)> clouds)
    {
        ArgumentNullException.ThrowIfNull(night);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(clouds);

        var rows = new List<PlanTimelineRow>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var usable = profiles[index].Usable;
            rows.Add(new PlanTimelineRow(
                item.Id,
                Runs(night, timeZone, k => usable[k]),
                item.Schedule is { StartLocal: { } start, EndLocal: { } end } ? new LocalSpan(start, end) : null,
                item.Feasibility?.Verdict,
                item.MeridianFlipLocal,
                item.IsLocked,
                item.Crossings.Count == 0 ? null : [.. item.Crossings.Select(c => c.EnterLocal)]));
        }

        return new PlanTimeline(
            Local(night.TimesUtc[0]),
            Local(night.TimesUtc[^1].AddSeconds(night.DurationsSeconds[^1])),
            Runs(night, timeZone, k => !night.IsDark[k]),
            [.. clouds.Where(c => c.EndLocal > c.StartLocal).Select(c => (new LocalSpan(c.StartLocal, c.EndLocal), c.CloudCoverPercent))],
            rows);

        DateTime Local(DateTime utc) => TimeZoneConversion.ToLocal(utc, timeZone);
    }

    /// <summary>Contiguous runs of samples where <paramref name="predicate"/> holds, in local time.</summary>
    private static List<LocalSpan> Runs(NightSkySamples night, TimeZoneInfo timeZone, Func<int, bool> predicate)
    {
        var spans = new List<LocalSpan>();
        var start = -1;
        for (var k = 0; k <= night.Count; k++)
        {
            var inside = k < night.Count && predicate(k);
            if (inside && start < 0)
            {
                start = k;
            }
            else if (!inside && start >= 0)
            {
                var endUtc = night.TimesUtc[k - 1].AddSeconds(night.DurationsSeconds[k - 1]);
                spans.Add(new LocalSpan(TimeZoneConversion.ToLocal(night.TimesUtc[start], timeZone), TimeZoneConversion.ToLocal(endUtc, timeZone)));
                start = -1;
            }
        }

        return spans;
    }
}
