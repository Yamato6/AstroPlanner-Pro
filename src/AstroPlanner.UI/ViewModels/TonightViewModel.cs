using CommunityToolkit.Mvvm.ComponentModel;
using AstroPlanner.Core.Models;
using AstroPlanner.UI.Services;

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
                CloudDetailText = "Consultando el pronóstico…";
            }
        };
        _logbook.Changed += () => OnPropertyChanged(nameof(ObservedSummaryText));
    }

    public NightInfo? Night => _context.Night;

    public MoonState? Moon => _context.Moon;

    public SkyCondition SkyCondition => _context.SkyCondition;

    /// <summary>Messier marathon summary for the catalog card.</summary>
    public string ObservedSummaryText => $"Messier observados: {_logbook.MessierProgressText}";

    public string DarkNightText => Night switch
    {
        null => "—",
        { HasAstronomicalDarkness: true } night => $"{night.AstronomicalDuskLocal:HH:mm} → {night.AstronomicalDawnLocal:HH:mm}",
        _ => "Sin noche oscura"
    };

    public string SunTimesText => Night switch
    {
        null => "",
        { IsFallbackWindow: true } => "El Sol no se pone o no sale: ventana 18:00 → 06:00",
        { } night => $"Puesta {night.SunsetLocal:HH:mm} · Salida {night.SunriseLocal:HH:mm}"
    };

    public string MoonIlluminationText => DisplayNames.MoonIllumination(Moon);

    public string MoonDetailText => Moon is not { } moon
        ? ""
        : $"{DisplayNames.MoonPhase(moon.IlluminatedFraction, moon.IsWaxing)} · "
          + (moon.IsAboveHorizon ? $"{moon.Horizontal.AltitudeDegrees:0}° de altura" : "bajo el horizonte");

    /// <summary>Warning when the sky is not dark at the chosen time; null at night.</summary>
    public string? SkyBanner => (SkyCondition, Night) switch
    {
        (_, { HasAstronomicalDarkness: false }) when SkyCondition != SkyCondition.Day =>
            "En esta fecha y lugar el Sol no baja de -18°: el cielo nunca está totalmente oscuro.",
        (SkyCondition.Day, { HasAstronomicalDarkness: true } night) =>
            $"Es de día a esta hora. La noche oscura empieza a las {night.AstronomicalDuskLocal:HH:mm}.",
        (SkyCondition.Day, _) => "Es de día a esta hora.",
        (SkyCondition.Twilight, var night) =>
            $"Crepúsculo: el cielo aún no está oscuro (noche oscura de {night!.AstronomicalDuskLocal:HH:mm} "
            + $"a {night.AstronomicalDawnLocal:HH:mm}).",
        _ => null
    };

    /// <summary>Average cloud cover of the dark night, e.g. "20 %".</summary>
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
            CloudDetailText = "Pronóstico desactivado en Configuración";
        }
        else if (forecast is null)
        {
            CloudCoverText = "—";
            CloudDetailText = _weather.LastError is null ? "Sin pronóstico" : $"Sin pronóstico: {_weather.LastError}";
        }
        else if (Night is { } night)
        {
            // The dark night if there is one; otherwise the whole observing window.
            var start = night.AstronomicalDuskLocal ?? night.WindowStartLocal;
            var end = night.AstronomicalDawnLocal ?? night.WindowEndLocal;
            if (forecast.Summarize(_context.ToUtc(start), _context.ToUtc(end)) is { } summary)
            {
                var clearest = TimeZoneInfo.ConvertTimeFromUtc(summary.ClearestHourUtc, _context.TimeZone);
                CloudCoverText = $"{summary.AverageCloudCoverPercent:0} %";
                CloudDetailText = $"{WeatherForecast.Describe(summary.AverageCloudCoverPercent)} · "
                    + $"mejor {clearest:HH:mm} ({summary.ClearestHourCloudCoverPercent} %)";
            }
            else
            {
                CloudCoverText = "—";
                CloudDetailText = "Sin pronóstico para esta fecha (hasta 7 días)";
            }
        }
    }
}
