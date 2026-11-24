using AstroPlanner.Core.Imaging;
using AstroPlanner.Core.Imaging.Feasibility;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Application.Resources;

namespace AstroPlanner.Application.Services;

/// <summary>One line of "Why this verdict?": the text and whether it helps or hurts.</summary>
public sealed record FeasibilityLine(string Text, ReasonImpact Impact)
{
    public bool IsBlocking => Impact == ReasonImpact.Blocking;
    public bool IsNegative => Impact is ReasonImpact.Negative or ReasonImpact.Blocking;
    public bool IsPositive => Impact == ReasonImpact.Positive;
}

/// <summary>User-facing texts of the feasibility model (Core returns numbers and enums only).</summary>
public static class FeasibilityText
{
    public static string Verdict(FeasibilityVerdict verdict) => verdict switch
    {
        FeasibilityVerdict.Excellent => Strings.VerdictExcellent,
        FeasibilityVerdict.Good => Strings.VerdictGood,
        FeasibilityVerdict.Marginal => Strings.VerdictMarginal,
        FeasibilityVerdict.Unlikely => Strings.VerdictUnlikely,
        _ => Strings.VerdictNotPossible
    };

    public static string Source(SkyQualitySource source) => source switch
    {
        SkyQualitySource.Manual => Strings.SkyQualityManual,
        SkyQualitySource.Atlas => Strings.SkyQualityAtlas,
        _ => Strings.SkyQualityDefault
    };

    /// <summary>"SQM 19.3 · Bortle 7 (atlas)".</summary>
    public static string SiteSky(double sqm, SkyQualitySource source) =>
        DisplayNames.Format(Strings.SiteSkyFormat, sqm, SkyBrightness.BortleFromSqm(sqm), Source(source));

    public static string Seconds(double seconds) =>
        seconds.ToString(seconds < 10 ? "0.#" : "0", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>"120 × 30 s at ISO 800" (or "at gain 100", or nothing).</summary>
    public static string Subs(int count, double subSeconds, int? iso, int? gain) =>
        DisplayNames.Format(Strings.PlanSubsFormat, count, Seconds(subSeconds), Setting(iso, gain)).TrimEnd();

    /// <summary>"at ISO 800", "at gain 100" or empty.</summary>
    public static string Setting(int? iso, int? gain) =>
        iso is { } i ? DisplayNames.Format(Strings.RecommendedIsoFormat, i)
        : gain is { } g ? DisplayNames.Format(Strings.RecommendedGainFormat, g)
        : string.Empty;

    public static IReadOnlyList<FeasibilityLine> Lines(FeasibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return [.. report.Reasons.Select(r => new FeasibilityLine(Line(r), r.Impact))];
    }

    public static string Line(FeasibilityReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return reason.Factor switch
        {
            FeasibilityFactor.NoDarkness => Strings.ReasonNoDarkness,
            FeasibilityFactor.NeverAboveMinimum => DisplayNames.Format(Strings.ReasonNeverAboveMinimumFormat, reason.Value),
            FeasibilityFactor.NoClearTime => DisplayNames.Format(Strings.ReasonNoClearTimeFormat, reason.Value),
            FeasibilityFactor.UsableTime => Math.Abs(reason.Value - reason.Value2) < 0.05
                ? DisplayNames.Format(Strings.ReasonUsableTimeFormat, reason.Value)
                : DisplayNames.Format(Strings.ReasonUsableTimeCloudsFormat, reason.Value, reason.Value2),
            FeasibilityFactor.Clouds => DisplayNames.Format(Strings.ReasonCloudsFormat, reason.Value),
            FeasibilityFactor.Moonlight => DisplayNames.Format(Strings.ReasonMoonlightFormat, reason.Value2, reason.Value3, reason.Value),
            FeasibilityFactor.SkyBackground => DisplayNames.Format(Strings.ReasonSkyBackgroundFormat, reason.Value, reason.Value2,
                Source(reason.Source ?? SkyQualitySource.Default)),
            FeasibilityFactor.Framing => reason.Framing switch
            {
                FramingClass.TooSmall => DisplayNames.Format(Strings.ReasonFramingTooSmallFormat, reason.Value),
                FramingClass.Small => DisplayNames.Format(Strings.ReasonFramingSmallFormat, reason.Value, reason.Value2),
                FramingClass.TooLarge => DisplayNames.Format(Strings.ReasonFramingTooLargeFormat, reason.Value2),
                FramingClass.PointLike => Strings.ReasonFramingPoint,
                _ => DisplayNames.Format(Strings.ReasonFramingGoodFormat, reason.Value, reason.Value2)
            },
            FeasibilityFactor.Mosaic => DisplayNames.Format(Strings.ReasonMosaicFormat, reason.Value),
            FeasibilityFactor.SubExposure => DisplayNames.Format(reason.Limit switch
            {
                ExposureLimitSource.Npf => Strings.ReasonSubNpfFormat,
                ExposureLimitSource.Mount => Strings.ReasonSubMountFormat,
                ExposureLimitSource.SkyLimited => Strings.ReasonSubSkyLimitedFormat,
                _ => Strings.ReasonSubCapFormat
            }, Seconds(reason.Value)),
            FeasibilityFactor.SignalToNoise => DisplayNames.Format(Strings.ReasonSnrFormat, reason.Value),
            FeasibilityFactor.Sampling => DisplayNames.Format(
                reason.Sampling == SamplingVerdict.Oversampled ? Strings.ReasonOversampledFormat : Strings.ReasonUndersampledFormat,
                reason.Value, reason.Value2),
            FeasibilityFactor.AssumedBrightness => DisplayNames.Format(Strings.ReasonAssumedBrightnessFormat, reason.Value),
            FeasibilityFactor.Seeing => DisplayNames.Format(Strings.ReasonSeeingFormat, reason.Value),
            FeasibilityFactor.Resolution => DisplayNames.Format(
                reason.Impact == ReasonImpact.Negative ? Strings.ReasonResolutionUnsplitFormat : Strings.ReasonResolutionSplitFormat,
                reason.Value, reason.Value2, reason.Value3),
            FeasibilityFactor.Transparency => DisplayNames.Format(Strings.ReasonTransparencyFormat, reason.Value, reason.Value2),
            _ => reason.Factor.ToString()
        };
    }

    /// <summary>"120 × 10 s at ISO 800 (20 min), best 21:20–05:05", or empty when nothing can be captured.</summary>
    public static string Recommended(FeasibilityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.Recommended is not { } r)
        {
            return string.Empty;
        }

        var setting = r.Iso is { } iso ? DisplayNames.Format(Strings.RecommendedIsoFormat, iso)
            : r.Gain is { } gain ? DisplayNames.Format(Strings.RecommendedGainFormat, gain)
            : string.Empty;
        return DisplayNames.Format(Strings.RecommendedFormat, r.SubCount, Seconds(r.SubSeconds), setting,
            DisplayNames.Duration(r.TotalIntegration), DisplayNames.Window(r.WindowStartLocal, r.WindowEndLocal));
    }
}
