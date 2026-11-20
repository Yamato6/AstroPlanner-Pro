using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.Models;

namespace AstroPlanner.UI.Services;

/// <summary>The feasibility request of the current night, site, sky, rig and forecast (spec §8.1).</summary>
public static class FeasibilityRequests
{
    /// <summary>Null while the night is unknown or the input is invalid.</summary>
    public static FeasibilityRequest? Create(IObserverContext context, RigSetup rig, WeatherState? weather)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rig);
        if (context.Night is not { } night || context.InputError is not null)
        {
            return null;
        }

        return new FeasibilityRequest(context.Observer, context.TimeZone, night, context.MinimumAltitude, rig,
            context.EffectiveZenithSqm, context.ZenithSqmSource, context.HorizonMask, context.Darkness, Forecast(weather))
        {
            Constants = FeasibilityConstants.Default with { ExtinctionCoefficient = context.ExtinctionCoefficient },
            Conditions = weather?.GetNightConditions(rig.Mount.Kind)
        };
    }

    /// <summary>The cached forecast, when weather is enabled.</summary>
    public static WeatherForecast? Forecast(WeatherState? weather) => weather is { IsEnabled: true } ? weather.Forecast : null;
}
