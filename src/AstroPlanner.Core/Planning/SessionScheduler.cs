using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Planning;

/// <summary>A target of the session plan, in priority order, with what the scheduler needs.</summary>
/// <param name="Id">Catalog id (returned as is).</param>
/// <param name="Profile">The target over the night (<see cref="FeasibilityCalculator.Profile"/>).</param>
/// <param name="Report">Its feasibility tonight (sub length, ISO/gain, SNR).</param>
public sealed record SessionTarget(string Id, TargetNightProfile Profile, FeasibilityReport Report)
{
    /// <summary>The user pinned the block: it is kept as is (<see cref="LockedStartLocal"/>–<see cref="LockedEndLocal"/>).</summary>
    public bool Locked { get; init; }

    public DateTime? LockedStartLocal { get; init; }
    public DateTime? LockedEndLocal { get; init; }
}

public enum ScheduleStatus
{
    /// <summary>Placed by the scheduler.</summary>
    Scheduled,

    /// <summary>Kept where the user locked it.</summary>
    Locked,

    /// <summary>Never usable tonight (no darkness, never above the limits, or the model says not possible).</summary>
    NotPossibleTonight,

    /// <summary>Usable, but higher-priority targets already took all its time.</summary>
    NoFreeTime
}

/// <summary>Where a target goes in the night and what to shoot there (spec §4.6).</summary>
/// <param name="UsableHours">Cloud-weighted usable time inside the block.</param>
/// <param name="MeridianFlipLocal">Upper transit inside the block, for equatorial mounts only.</param>
/// <param name="EstimatedSignalToNoise">Stack SNR for the block (the night's SNR scaled by √time).</param>
public sealed record ScheduledTarget(
    string Id,
    ScheduleStatus Status,
    DateTime? StartLocal = null,
    DateTime? EndLocal = null,
    double UsableHours = 0,
    double SubSeconds = 0,
    int? Iso = null,
    int? Gain = null,
    int SubCount = 0,
    TimeSpan Integration = default,
    DateTime? MeridianFlipLocal = null,
    double EstimatedSignalToNoise = 0)
{
    public bool HasBlock => StartLocal is not null && EndLocal is not null;
}

/// <summary>The schedule of the whole plan.</summary>
/// <param name="Targets">One per input target, in the input order.</param>
/// <param name="DarkHours">Dark time of the night (by the darkness definition of the request).</param>
/// <param name="IdleDarkHours">Dark time no target uses.</param>
public sealed record SessionSchedule(IReadOnlyList<ScheduledTarget> Targets, double DarkHours, double IdleDarkHours);

/// <summary>Tunables of <see cref="SessionScheduler"/> (docs/feasibility-model.md, "Session planner").</summary>
public sealed record SessionSchedulerOptions
{
    public static SessionSchedulerOptions Default { get; } = new();

    /// <summary>Shortest block worth setting up for (unless the target is visible for less).</summary>
    public TimeSpan MinimumBlock { get; init; } = TimeSpan.FromMinutes(20);
}

/// <summary>
/// Greedy session scheduler (spec §4.6). Works on the samples of the night:
/// <list type="number">
/// <item>Locked targets keep their block.</item>
/// <item>The others, in priority order, each take the contiguous stretch of free, usable samples
/// with the highest SNR gain (<see cref="TargetNightProfile.Quality"/>), at most a fair share of
/// the free dark time (dark time ÷ targets, never under <see cref="SessionSchedulerOptions.MinimumBlock"/>).</item>
/// <item>Blocks then grow, one sample per round and in priority order, into adjacent free time
/// where their target is still usable, so that no usable dark time is left idle.</item>
/// </list>
/// Per block: subs of the target's recommended length over the cloud-weighted usable time,
/// total integration, SNR scaled from the night's estimate, and the meridian flip (upper transit)
/// for equatorial mounts. Deterministic: same input, same schedule.
/// </summary>
public static class SessionScheduler
{
    public static SessionSchedule Schedule(
        IReadOnlyList<SessionTarget> targets,
        NightSkySamples night,
        FeasibilityRequest request,
        SessionSchedulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(night);
        ArgumentNullException.ThrowIfNull(request);
        options ??= SessionSchedulerOptions.Default;

        var n = night.Count;
        foreach (var target in targets)
        {
            if (target.Profile.Usable.Count != n)
            {
                throw new ArgumentException($"The profile of {target.Id} does not match the sampled night.", nameof(targets));
            }
        }

        var owner = new int[n];
        Array.Fill(owner, -1);
        var blocks = new (int Start, int End)?[targets.Count];
        var statuses = new ScheduleStatus[targets.Count];

        // 1. Locked blocks, as the user left them.
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (!target.Locked || target.LockedStartLocal is not { } start || target.LockedEndLocal is not { } end)
            {
                continue;
            }

            statuses[i] = ScheduleStatus.Locked;
            var startUtc = TimeZoneConversion.ToUtc(start, request.TimeZone);
            var endUtc = TimeZoneConversion.ToUtc(end, request.TimeZone);
            int first = -1, last = -1;
            for (var k = 0; k < n; k++)
            {
                if (night.TimesUtc[k] >= startUtc && night.TimesUtc[k] < endUtc && owner[k] < 0)
                {
                    owner[k] = i;
                    first = first < 0 ? k : first;
                    last = k;
                }
            }

            if (first >= 0)
            {
                blocks[i] = (first, last);
            }
        }

        // 2. Best stretch for the others, in priority order.
        var minimumSamples = Math.Max(1, (int)Math.Ceiling(options.MinimumBlock / request.Constants.Step));
        var pending = Enumerable.Range(0, targets.Count).Where(i => statuses[i] != ScheduleStatus.Locked).ToList();
        var schedulable = pending.Count(i => IsPossible(targets[i]));
        var freeDark = Enumerable.Range(0, n).Count(k => night.IsDark[k] && owner[k] < 0);
        var share = schedulable > 0 ? Math.Max(minimumSamples, (int)Math.Ceiling(freeDark / (double)schedulable)) : 0;
        foreach (var i in pending)
        {
            var target = targets[i];
            if (!IsPossible(target))
            {
                statuses[i] = ScheduleStatus.NotPossibleTonight;
                continue;
            }

            var required = Math.Min(minimumSamples, LongestRun(k => target.Profile.Usable[k], n));
            var best = BestStretch(target.Profile, owner, share, required);
            if (best is not { } stretch)
            {
                statuses[i] = ScheduleStatus.NoFreeTime;
                continue;
            }

            statuses[i] = ScheduleStatus.Scheduled;
            blocks[i] = stretch;
            for (var k = stretch.Start; k <= stretch.End; k++)
            {
                owner[k] = i;
            }
        }

        // 3. Grow into the free time next to each block.
        bool grew;
        do
        {
            grew = false;
            foreach (var i in pending)
            {
                if (blocks[i] is not { } block)
                {
                    continue;
                }

                var usable = targets[i].Profile.Usable;
                if (block.End + 1 < n && owner[block.End + 1] < 0 && usable[block.End + 1])
                {
                    block.End++;
                    owner[block.End] = i;
                    grew = true;
                }

                if (block.Start > 0 && owner[block.Start - 1] < 0 && usable[block.Start - 1])
                {
                    block.Start--;
                    owner[block.Start] = i;
                    grew = true;
                }

                blocks[i] = block;
            }
        }
        while (grew);

        // 4. What to shoot in each block.
        var results = new ScheduledTarget[targets.Count];
        var equatorial = request.Rig.Mount.Kind is MountKind.Equatorial or MountKind.EquatorialGuided;
        for (var i = 0; i < targets.Count; i++)
        {
            results[i] = blocks[i] is { } block
                ? Describe(targets[i], statuses[i], block, night, request, equatorial)
                : new ScheduledTarget(targets[i].Id, statuses[i]);
        }

        double darkSeconds = 0, idleSeconds = 0;
        for (var k = 0; k < n; k++)
        {
            if (night.IsDark[k])
            {
                darkSeconds += night.DurationsSeconds[k];
                idleSeconds += owner[k] < 0 ? night.DurationsSeconds[k] : 0;
            }
        }

        return new SessionSchedule(results, darkSeconds / 3600, idleSeconds / 3600);
    }

    private static bool IsPossible(SessionTarget target) =>
        target.Report.Verdict != FeasibilityVerdict.NotPossible && target.Profile.Usable.Any(u => u);

    private static int LongestRun(Func<int, bool> predicate, int n)
    {
        int longest = 0, run = 0;
        for (var k = 0; k < n; k++)
        {
            run = predicate(k) ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    /// <summary>
    /// The window of at most <paramref name="length"/> free, usable samples with the largest
    /// summed quality (ties: the earliest); null when no free run reaches <paramref name="required"/>.
    /// </summary>
    private static (int Start, int End)? BestStretch(TargetNightProfile profile, int[] owner, int length, int required)
    {
        (int Start, int End)? best = null;
        var bestValue = double.NegativeInfinity;
        var n = owner.Length;
        var k = 0;
        while (k < n)
        {
            if (owner[k] >= 0 || !profile.Usable[k])
            {
                k++;
                continue;
            }

            var runStart = k;
            while (k < n && owner[k] < 0 && profile.Usable[k])
            {
                k++;
            }

            var runLength = k - runStart;
            if (runLength < required)
            {
                continue;
            }

            var window = Math.Min(length, runLength);
            double sum = 0;
            for (var j = runStart; j < runStart + window; j++)
            {
                sum += profile.Quality[j];
            }

            for (var start = runStart; ; start++)
            {
                if (best is null || sum > bestValue + 1e-12 * Math.Abs(bestValue))
                {
                    bestValue = sum;
                    best = (start, start + window - 1);
                }

                if (start + window >= k)
                {
                    break;
                }

                sum += profile.Quality[start + window] - profile.Quality[start];
            }
        }

        return best;
    }

    private static ScheduledTarget Describe(
        SessionTarget target,
        ScheduleStatus status,
        (int Start, int End) block,
        NightSkySamples night,
        FeasibilityRequest request,
        bool equatorial)
    {
        var profile = target.Profile;
        var report = target.Report;
        double usableSeconds = 0;
        DateTime? flipUtc = null;
        for (var k = block.Start; k <= block.End; k++)
        {
            if (profile.Usable[k])
            {
                usableSeconds += night.DurationsSeconds[k] * (1 - night.CloudFraction[k]);
            }

            // Upper transit: the hour angle goes from negative to positive (not across ±180°).
            if (equatorial && flipUtc is null && k > block.Start)
            {
                var before = profile.HourAngleDegrees[k - 1];
                var after = profile.HourAngleDegrees[k];
                if (before < 0 && after >= 0 && after - before < 180)
                {
                    var fraction = -before / (after - before);
                    flipUtc = night.TimesUtc[k - 1] + (night.TimesUtc[k] - night.TimesUtc[k - 1]) * fraction;
                }
            }
        }

        var endUtc = night.TimesUtc[block.End].AddSeconds(night.DurationsSeconds[block.End]);
        var sub = report.SubSeconds;
        var subCount = sub > 0 ? (int)Math.Floor(usableSeconds / (sub + request.Constants.OverheadSeconds)) : 0;
        var nightUsableSeconds = report.UsableHours * 3600;
        var snr = nightUsableSeconds > 0 ? report.SignalToNoise * Math.Sqrt(Math.Min(1, usableSeconds / nightUsableSeconds)) : 0;
        var rig = request.Rig;
        return new ScheduledTarget(
            target.Id,
            status,
            Local(night.TimesUtc[block.Start]),
            Local(endUtc),
            usableSeconds / 3600,
            sub,
            rig.Camera.RecommendedIso,
            rig.Camera.RecommendedGain,
            subCount,
            TimeSpan.FromSeconds(subCount * sub),
            flipUtc is { } flip ? Local(flip) : null,
            subCount > 0 ? snr : 0);

        DateTime Local(DateTime utc) => TimeZoneConversion.ToLocal(utc, request.TimeZone);
    }
}
