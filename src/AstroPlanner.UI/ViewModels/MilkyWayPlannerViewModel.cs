using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Planning;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;

namespace AstroPlanner.UI.ViewModels;

/// <summary>One night of the planner list: "Sat 2027-03-13 · 04:20–05:40 · Moon down 1 h 20 min · Moon 3 %".</summary>
public sealed record MilkyWayNightRow(MilkyWayNight Night, string Text);

/// <summary>
/// The Milky Way core over the next 12 months (spec §4.1, §6) for the active site: hours of core
/// in astronomical darkness per night, how many with the Moon down, and the best nights.
/// </summary>
public sealed partial class MilkyWayPlannerViewModel(IAstronomyService astronomy, IObserverContext context) : ObservableObject
{
    /// <summary>Nights listed among the best ones.</summary>
    public const int BestNightCount = 20;

    /// <summary>Every night of the year from tonight (empty until loaded).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MilkyWayNight> Nights { get; private set; } = [];

    /// <summary>The good nights (core in a dark, Moon-free sky), longest Moon-free time first.</summary>
    public ObservableCollection<MilkyWayNightRow> BestNights { get; } = [];

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>"From Aguascalientes · core above 10° in astronomical darkness".</summary>
    public string SubtitleText => DisplayNames.Format(Strings.MilkyWaySubtitleFormat, context.LocationName,
        MilkyWayPlanner.DefaultMinimumAltitudeDegrees);

    /// <summary>Why there is no list (never visible from this latitude); empty otherwise.</summary>
    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (context.Night is not { } night || context.InputError is not null)
        {
            StatusText = Strings.MilkyWayNoSite;
            return;
        }

        IsLoading = true;
        StatusText = Strings.MilkyWayLoading;
        try
        {
            var (observer, zone) = (context.Observer, context.TimeZone);
            var first = DateOnly.FromDateTime(night.WindowStartLocal);
            Nights = await Task.Run(() => MilkyWayPlanner.Nights(astronomy, observer, zone, first, 365, cancellationToken: cancellationToken),
                cancellationToken);
            BestNights.Clear();
            foreach (var good in Nights.Where(n => n.IsGood).OrderByDescending(n => n.CoreMoonFree).ThenBy(n => n.Date).Take(BestNightCount)
                         .OrderBy(n => n.Date))
            {
                BestNights.Add(new MilkyWayNightRow(good, DisplayNames.Format(Strings.MilkyWayNightFormat,
                    good.Date.ToDateTime(TimeOnly.MinValue), good.StartLocal, good.EndLocal, DisplayNames.Duration(good.CoreMoonFree),
                    good.MoonIlluminatedFraction * 100)));
            }

            StatusText = BestNights.Count == 0 ? Strings.MilkyWayNever : "";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
