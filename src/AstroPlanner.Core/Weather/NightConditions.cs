using AstroPlanner.Core.Models;

namespace AstroPlanner.Core.Weather;

/// <summary>How likely dew is on the optics.</summary>
public enum DewRisk
{
    /// <summary>No forecast, or the air stays well above the dew point.</summary>
    None,
    /// <summary>Temperature within <see cref="ConditionThresholds.DewModerateMarginC"/> of the dew point.</summary>
    Moderate,
    /// <summary>Temperature within <see cref="ConditionThresholds.DewHighMarginC"/> of the dew point.</summary>
    High
}

/// <summary>
/// Thresholds of the dew and wind warnings (spec §4.1). Heuristics, documented in
/// docs/feasibility-model.md §11: optics cool below the air by radiation, so dew forms on them
/// before the air reaches its dew point; telescopes catch more wind than a camera on a tripod.
/// </summary>
public sealed record ConditionThresholds
{
    public static ConditionThresholds Default { get; } = new();

    public double DewHighMarginC { get; init; } = 2;
    public double DewModerateMarginC { get; init; } = 4;

    /// <summary>Gusts that shake a camera on a tripod or tracker.</summary>
    public double CameraGustKmh { get; init; } = 30;

    /// <summary>Gusts that shake a telescope (alt-az or equatorial).</summary>
    public double TelescopeGustKmh { get; init; } = 20;

    public double GustLimitFor(MountKind? mount) =>
        mount is MountKind.FixedTripod or MountKind.StarTracker or null ? CameraGustKmh : TelescopeGustKmh;
}

/// <summary>
/// The forecast conditions of a night window (usually the dark night): seeing and transparency
/// from 7Timer! ASTRO, dew margin and wind from Open-Meteo, with the dew and wind warnings.
/// Every value is null when its forecast does not cover the window.
/// </summary>
public sealed record NightConditions
{
    /// <summary>No forecast at all.</summary>
    public static NightConditions None { get; } = new();

    /// <summary>Mean forecast seeing, arcseconds.</summary>
    public double? SeeingArcsec { get; init; }

    /// <summary>Mean forecast transparency, magnitudes per air mass (7Timer scale).</summary>
    public double? TransparencyMagnitudesPerAirmass { get; init; }

    /// <summary>Smallest temperature − dew point, °C.</summary>
    public double? MinimumDewMarginC { get; init; }

    public double? MaximumGustKmh { get; init; }
    public double? MeanWindKmh { get; init; }
    public DewRisk DewRisk { get; init; }

    /// <summary>First hour (UTC) in which the dew risk is reached.</summary>
    public DateTime? DewRiskFromUtc { get; init; }

    /// <summary>Gusts over the limit for the rig's mount (<see cref="ConditionThresholds.GustLimitFor"/>).</summary>
    public bool WindWarning { get; init; }

    /// <summary>The gust limit that was applied, km/h.</summary>
    public double GustLimitKmh { get; init; }

    public bool HasAstroForecast => SeeingArcsec is not null;

    /// <summary>Conditions over [<paramref name="startUtc"/>, <paramref name="endUtc"/>).</summary>
    /// <param name="mount">Mount of the active rig (wind limit); null = a camera on a tripod.</param>
    public static NightConditions Create(
        DateTime startUtc,
        DateTime endUtc,
        WeatherForecast? weather,
        AstroForecast? astro,
        MountKind? mount = null,
        ConditionThresholds? thresholds = null)
    {
        var t = thresholds ?? ConditionThresholds.Default;
        var conditions = new NightConditions { GustLimitKmh = t.GustLimitFor(mount) };

        if (astro?.Between(startUtc, endUtc) is { Count: > 0 } points)
        {
            conditions = conditions with
            {
                SeeingArcsec = points.Average(p => p.SeeingArcsec),
                TransparencyMagnitudesPerAirmass = points.Average(p => p.TransparencyMagnitudesPerAirmass)
            };
        }

        if (weather?.Between(startUtc, endUtc) is { Count: > 0 } hours)
        {
            var margins = hours
                .Where(h => h.TemperatureC is not null && h.DewPointC is not null)
                .Select(h => (h.TimeUtc, Margin: h.TemperatureC!.Value - h.DewPointC!.Value))
                .ToList();
            var gusts = hours.Where(h => h.WindGustKmh is not null).Select(h => h.WindGustKmh!.Value).ToList();
            var winds = hours.Where(h => h.WindSpeedKmh is not null).Select(h => h.WindSpeedKmh!.Value).ToList();

            if (margins.Count > 0)
            {
                var minimum = margins.Min(m => m.Margin);
                var risk = minimum <= t.DewHighMarginC ? DewRisk.High
                    : minimum <= t.DewModerateMarginC ? DewRisk.Moderate
                    : DewRisk.None;
                conditions = conditions with
                {
                    MinimumDewMarginC = minimum,
                    DewRisk = risk,
                    DewRiskFromUtc = risk == DewRisk.None ? null : margins.First(m => m.Margin <= t.DewModerateMarginC).TimeUtc
                };
            }

            if (gusts.Count > 0)
            {
                conditions = conditions with { MaximumGustKmh = gusts.Max(), WindWarning = gusts.Max() > conditions.GustLimitKmh };
            }

            if (winds.Count > 0)
            {
                conditions = conditions with { MeanWindKmh = winds.Average() };
            }
        }

        return conditions;
    }
}
