using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Resources;
using AstroPlanner.UI.Services;
using static AstroPlanner.UI.Services.DisplayNames;

namespace AstroPlanner.UI.ViewModels;

/// <summary>
/// Tonight at a glance: dark night, Sun times, Moon, clouds, day/twilight warning and
/// Messier marathon progress.
/// </summary>
public sealed partial class TonightViewModel : ObservableObject
{
    private readonly IObserverContext _context;
    private readonly WeatherState _weather;
    private readonly LogbookViewModel _logbook;

    public TonightViewModel(IObserverContext context, WeatherState weather, LogbookViewModel logbook)
    {
        _context = context;
        _weather = weather;
        _logbook = logbook;

        _context.Recalculated += (_, _) => OnSkyChanged();
        _weather.Changed += (_, _) => UpdateCloudSummary();
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
                    summary.ClearestHourCloudCoverPercent);
            }
            else
            {
                CloudCoverText = "—";
                CloudDetailText = Strings.CloudsNoForecastForDate;
            }
        }
    }
}
