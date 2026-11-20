using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Tonight at a glance: dark night, Sun times, Moon, clouds, day/twilight warning, Messier
/// marathon progress, the site's sky quality and the best targets for the active rig (spec §4.1).
/// </summary>
public sealed partial class TonightViewModel : ObservableObject
{
    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly LogbookViewModel _logbook;
    private readonly TargetsViewModel? _targets;
    private readonly EquipmentState? _equipment;
    private readonly SessionPlanViewModel? _plan;

    /// <summary>How many targets the "best targets" list shows.</summary>
    public const int BestTargetCount = 12;

    public TonightViewModel(
        IObserverContext context,
        WeatherState weather,
        LogbookViewModel logbook,
        TargetsViewModel? targets = null,
        EquipmentState? equipment = null,
        SessionPlanViewModel? plan = null)
    {
        _context = context;
        _weather = weather;
        _logbook = logbook;
        _targets = targets;
        _equipment = equipment;
        _plan = plan;
        if (_targets is not null)
        {
            _targets.FeasibilityUpdated += (_, _) => UpdateBestTargets();
            _targets.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TargetsViewModel.IsCalculatingFeasibility))
                {
                    OnPropertyChanged(nameof(BestTargetsHint));
                }
            };
        }

        if (_equipment is not null)
        {
            _equipment.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(ActiveRigText));
                UpdateConditions();
            };
        }

        _context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IObserverContext.EffectiveZenithSqm) or nameof(IObserverContext.ZenithSqmSource))
            {
                OnPropertyChanged(nameof(SiteSkyText));
            }
        };

        _context.Recalculated += (_, _) =>
        {
            OnSkyChanged();
            UpdateConditions();
        };
        _weather.Changed += (_, _) =>
        {
            UpdateCloudSummary();
            UpdateConditions();
        };
        _weather.PropertyChanged += (_, e) =>
        {
            // While the first forecast for a location is being fetched, say so.
            if (e.PropertyName == nameof(WeatherState.IsFetching) && _weather.IsFetching && _weather.Forecast is null)
            {
                CloudDetailText = Strings.CloudsFetching;
            }
        };
        _logbook.Changed += () => OnPropertyChanged(nameof(ObservedSummaryText));
    }

    /// <summary>Best targets tonight for the active rig, by feasibility score.</summary>
    public ObservableCollection<CelestialObjectRowViewModel> BestTargets { get; } = [];

    /// <summary>Why the list is empty (no rig, still calculating, nothing feasible); null when it has targets.</summary>
    public string? BestTargetsHint => _equipment?.ActiveRig is null ? Strings.BestTargetsNoRig
        : _targets?.IsCalculatingFeasibility == true ? Strings.BestTargetsCalculating
        : BestTargets.Count == 0 ? Strings.BestTargetsNone
        : null;

    /// <summary>"T7 + Rokinon 16 mm (tripod) · 69.8° × 49.9° · 48.0″/px".</summary>
    public string ActiveRigText => _equipment?.ActiveRig is { } rig
        ? Format(Strings.ActiveRigSummaryFormat, rig.Name, rig.FieldOfView.WidthDegrees, rig.FieldOfView.HeightDegrees, rig.ImageScaleArcsecPerPixel)
        : Strings.NoActiveRig;

    /// <summary>Zenith sky quality of the site and where it comes from.</summary>
    public string SiteSkyText => FeasibilityText.SiteSky(_context.EffectiveZenithSqm, _context.ZenithSqmSource);

    /// <summary>"Seeing 1.1″ · transparency 0.35 mag", or why there is no seeing forecast.</summary>
    [ObservableProperty]
    public partial string ConditionsText { get; private set; } = "—";

    /// <summary>"Dew margin 6 °C · gusts up to 18 km/h", or why there is no forecast.</summary>
    [ObservableProperty]
    public partial string ConditionsDetailText { get; private set; } = "";

    /// <summary>Dew and wind warnings for tonight with the active rig; empty when there are none.</summary>
    public ObservableCollection<string> Alerts { get; } = [];

    /// <summary>Adds a target to the session plan.</summary>
    [RelayCommand]
    private void AddToPlan(CelestialObjectRowViewModel? row)
    {
        if (row is not null && _plan is not null && !_plan.Contains(row))
        {
            _plan.Add(row);
        }
    }

    private void UpdateBestTargets()
    {
        BestTargets.Clear();
        if (_targets is not null)
        {
            foreach (var row in _targets.AllObjects
                         .Where(r => r.Feasibility is { Verdict: >= FeasibilityVerdict.Marginal })
                         .OrderByDescending(r => r.Feasibility!.Score)
                         .ThenByDescending(r => r.Feasibility!.UsableHours)
                         .Take(BestTargetCount))
            {
                BestTargets.Add(row);
            }
        }

        OnPropertyChanged(nameof(BestTargetsHint));
        OnPropertyChanged(nameof(ActiveRigText));
    }

    public NightInfo? Night => _context.Night;

    public MoonState? Moon => _context.Moon;

    public SkyCondition SkyCondition => _context.SkyCondition;

    /// <summary>Messier marathon summary for the catalog card.</summary>
    public string ObservedSummaryText => Format(Strings.ObservedSummaryFormat, _logbook.MessierProgressText);

    public string DarkNightText => Night switch
    {
        null => "—",
        { HasAstronomicalDarkness: true } night =>
            Format(Strings.DarkNightRangeFormat, night.AstronomicalDuskLocal, night.AstronomicalDawnLocal),
        _ => Strings.DarkNightNone
    };

    public string SunTimesText => Night switch
    {
        null => "",
        { IsFallbackWindow: true } => Strings.SunFallbackWindow,
        { } night => Format(Strings.SunTimesFormat, night.SunsetLocal, night.SunriseLocal)
    };

    public string MoonIlluminationText => MoonIllumination(Moon);

    public string MoonDetailText => Moon is not { } moon
        ? ""
        : moon.IsAboveHorizon
            ? Format(Strings.MoonDetailAboveFormat, MoonPhase(moon.IlluminatedFraction, moon.IsWaxing), moon.Horizontal.AltitudeDegrees)
            : Format(Strings.MoonDetailBelowFormat, MoonPhase(moon.IlluminatedFraction, moon.IsWaxing));

    /// <summary>Warning when the sky is not dark at the chosen time; null at night.</summary>
    public string? SkyBanner => (SkyCondition, Night) switch
    {
        (_, { HasAstronomicalDarkness: false }) when SkyCondition != SkyCondition.Day => Strings.BannerNeverDark,
        (SkyCondition.Day, { HasAstronomicalDarkness: true } night) =>
            Format(Strings.BannerDaytimeDarkAtFormat, night.AstronomicalDuskLocal),
        (SkyCondition.Day, _) => Strings.BannerDaytime,
        (SkyCondition.Twilight, var night) =>
            Format(Strings.BannerTwilightFormat, night!.AstronomicalDuskLocal, night.AstronomicalDawnLocal),
        _ => null
    };

    /// <summary>Average cloud cover of the dark night, e.g. "20%".</summary>
    [ObservableProperty]
    public partial string CloudCoverText { get; private set; } = "—";

    /// <summary>Description and clearest hour, or why there is no forecast.</summary>
    [ObservableProperty]
    public partial string CloudDetailText { get; private set; } = "";

    private void OnSkyChanged()
    {
        if (_context.InputError is not null)
        {
            return;
        }

        OnPropertyChanged(nameof(Night));
        OnPropertyChanged(nameof(Moon));
        OnPropertyChanged(nameof(SkyCondition));
        OnPropertyChanged(nameof(DarkNightText));
        OnPropertyChanged(nameof(SunTimesText));
        OnPropertyChanged(nameof(MoonIlluminationText));
        OnPropertyChanged(nameof(MoonDetailText));
        OnPropertyChanged(nameof(SkyBanner));
    }

    /// <summary>Recalculates the cloud card.</summary>
    /// <summary>Seeing, transparency, dew and wind of the dark night (spec §4.1) and their warnings.</summary>
    private void UpdateConditions()
    {
        Alerts.Clear();
        var conditions = _weather.GetNightConditions(_equipment?.ActiveRig?.Mount.Kind);
        var zone = _context.TimeZone;

        ConditionsText = !_weather.IsAstroEnabled ? Strings.ConditionsAstroDisabled
            : conditions is { SeeingArcsec: { } seeing, TransparencyMagnitudesPerAirmass: { } transparency }
                ? Format(Strings.ConditionsSeeingFormat, seeing, transparency)
                + (_weather.IsAstroForecastStale ? Format(Strings.ForecastAsOfFormat, Local(_weather.AstroForecast!.FetchedAtUtc)) : "")
            : _weather.AstroLastError is { } astroError ? Format(Strings.ConditionsAstroUnavailableFormat, astroError)
            : _weather.AstroForecast is null ? Strings.ConditionsNoForecast
            : Strings.ConditionsNoForecastForDate;

        var parts = new List<string>();
        if (conditions?.MinimumDewMarginC is { } margin)
        {
            parts.Add(Format(Strings.ConditionsDewMarginFormat, margin));
        }

        if (conditions?.MaximumGustKmh is { } gust)
        {
            parts.Add(Format(Strings.ConditionsGustFormat, gust));
        }

        ConditionsDetailText = parts.Count > 0 ? string.Join(" · ", parts)
            : !_weather.IsEnabled ? Strings.CloudsDisabled
            : Strings.ConditionsNoDewWind;

        if (conditions is null)
        {
            return;
        }

        if (conditions.DewRisk != Core.Weather.DewRisk.None && conditions.DewRiskFromUtc is { } from)
        {
            Alerts.Add(Format(conditions.DewRisk == Core.Weather.DewRisk.High ? Strings.AlertDewHighFormat : Strings.AlertDewModerateFormat,
                Local(from), conditions.MinimumDewMarginC));
        }

        if (conditions.WindWarning)
        {
            Alerts.Add(Format(Strings.AlertWindFormat, conditions.MaximumGustKmh, conditions.GustLimitKmh));
        }

        DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }

    private void UpdateCloudSummary()
    {
        var forecast = _weather.Forecast;
        if (!_weather.IsEnabled)
        {
            CloudCoverText = "—";
            CloudDetailText = Strings.CloudsDisabled;
        }
        else if (forecast is null)
        {
            CloudCoverText = "—";
            CloudDetailText = _weather.LastError is null
                ? Strings.CloudsNoForecast
                : Format(Strings.CloudsNoForecastReasonFormat, _weather.LastError);
        }
        else if (Night is { } night)
        {
            // The dark night if there is one; otherwise the whole observing window.
            var start = night.AstronomicalDuskLocal ?? night.WindowStartLocal;
            var end = night.AstronomicalDawnLocal ?? night.WindowEndLocal;
            if (forecast.Summarize(_context.ToUtc(start), _context.ToUtc(end)) is { } summary)
            {
                var clearest = TimeZoneInfo.ConvertTimeFromUtc(summary.ClearestHourUtc, _context.TimeZone);
                CloudCoverText = Format(Strings.PercentFormat, summary.AverageCloudCoverPercent);
                CloudDetailText = Format(
                    Strings.CloudsSummaryFormat,
                    CloudCover(summary.AverageCloudCoverPercent),
                    clearest,
                    summary.ClearestHourCloudCoverPercent)
                    + (_weather.IsForecastStale
                        ? Format(Strings.ForecastAsOfFormat, TimeZoneInfo.ConvertTimeFromUtc(forecast.FetchedAtUtc, _context.TimeZone))
                        : "");
            }
            else
            {
                CloudCoverText = "—";
                CloudDetailText = Strings.CloudsNoForecastForDate;
            }
        }
    }
}
