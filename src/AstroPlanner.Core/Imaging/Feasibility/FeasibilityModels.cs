using AstroPlanner.Core.Models;
using AstroPlanner.Core.SkyQuality;
using AstroPlanner.Core.Weather;

namespace AstroPlanner.Core.Imaging.Feasibility;

/// <summary>Overall answer of the feasibility model (spec §8), worst to best.</summary>
public enum FeasibilityVerdict
{
    NotPossible,
    Unlikely,
    Marginal,
    Good,
    Excellent
}

/// <summary>How the object fills the frame (spec §8.2).</summary>
public enum FramingClass
{
    /// <summary>Under <see cref="FeasibilityConstants.TooSmallPixels"/> across.</summary>
    TooSmall,
    /// <summary>Fills less than <see cref="FeasibilityConstants.SmallFill"/> of the frame.</summary>
    Small,
    Good,
    /// <summary>Larger than the frame: a mosaic is suggested.</summary>
    TooLarge,
    /// <summary>A point of light (a star, or no catalogued size): nothing to frame, at most "unlikely".</summary>
    PointLike
}

/// <summary>What counts as "dark enough" to image (spec §4.9, §8.4).</summary>
public enum DarknessDefinition
{
    /// <summary>Sun below −18°.</summary>
    Astronomical,
    /// <summary>Sun below −12°; the twilight glow is added to the sky background.</summary>
    Nautical
}

/// <summary>Factors of the explanation ("Why this verdict?"), formatted by the UI.</summary>
public enum FeasibilityFactor
{
    /// <summary>The Sun never gets low enough tonight. No values.</summary>
    NoDarkness,
    /// <summary>The target never clears the minimum altitude / horizon in darkness. Value = max altitude (°).</summary>
    NeverAboveMinimum,
    /// <summary>Clouds cover all the usable time. Value = geometric hours.</summary>
    NoClearTime,
    /// <summary>Value = usable hours (cloud-weighted), Value2 = geometric hours.</summary>
    UsableTime,
    /// <summary>Value = mean cloud cover (%) over the geometric window.</summary>
    Clouds,
    /// <summary>Value = magnitudes the Moon brightens the sky, Value2 = illuminated fraction (0–1), Value3 = separation (°).</summary>
    Moonlight,
    /// <summary>Value = mean sky brightness at the target (mag/arcsec²), Value2 = zenith SQM; Source = where it comes from.</summary>
    SkyBackground,
    /// <summary>Value = object size in pixels (major axis), Value2 = fraction of the frame; Framing = class.</summary>
    Framing,
    /// <summary>Value = mosaic panels needed.</summary>
    Mosaic,
    /// <summary>Value = sub length (s); Limit = what limits it.</summary>
    SubExposure,
    /// <summary>Value = estimated signal-to-noise of the stack.</summary>
    SignalToNoise,
    /// <summary>Value = seeing FWHM in pixels, Value2 = image scale (″/px); Sampling = verdict.</summary>
    Sampling,
    /// <summary>The catalog has no magnitude/surface brightness: Value = the assumed mag/arcsec².</summary>
    AssumedBrightness,
    /// <summary>Forecast seeing (7Timer!): Value = FWHM (″).</summary>
    Seeing,
    /// <summary>Forecast transparency (7Timer!): Value = mag/air mass, Value2 = extinction added to the site's k.</summary>
    Transparency
}

public enum ReasonImpact
{
    Blocking,
    Negative,
    Neutral,
    Positive
}

/// <summary>One line of the explanation, with the numbers behind it.</summary>
public sealed record FeasibilityReason(
    FeasibilityFactor Factor,
    ReasonImpact Impact,
    double Value = double.NaN,
    double Value2 = double.NaN,
    double Value3 = double.NaN)
{
    public FramingClass? Framing { get; init; }
    public ExposureLimitSource? Limit { get; init; }
    public SamplingVerdict? Sampling { get; init; }
    public SkyQualitySource? Source { get; init; }
}

/// <summary>Object size against the frame.</summary>
/// <param name="Fill">max(major / long side, minor / short side).</param>
/// <param name="MosaicPanels">Panels for a mosaic with 20 % overlap (1 = fits).</param>
public sealed record FramingAssessment(FramingClass Class, double MajorPixels, double MinorPixels, double Fill, int MosaicPanels);

/// <summary>What the model suggests doing (spec §8: sub, ISO/gain, number of subs, window, total).</summary>
public sealed record RecommendedSettings(
    double SubSeconds,
    int? Iso,
    int? Gain,
    int SubCount,
    TimeSpan TotalIntegration,
    DateTime? WindowStartLocal,
    DateTime? WindowEndLocal);

/// <summary>
/// Everything the feasibility model computed for one (target, rig, site, night) — spec §8.6:
/// every intermediate value is kept for the "Why this verdict?" panel.
/// </summary>
public sealed record FeasibilityReport
{
    public required FeasibilityVerdict Verdict { get; init; }

    /// <summary>0–100.</summary>
    public required int Score { get; init; }

    /// <summary>Ordered: blocking, negative, positive, neutral.</summary>
    public required IReadOnlyList<FeasibilityReason> Reasons { get; init; }

    /// <summary>Null when nothing can be captured tonight.</summary>
    public RecommendedSettings? Recommended { get; init; }

    public required FramingAssessment Framing { get; init; }
    public double MaximumAltitudeDegrees { get; init; }
    public double GeometricHours { get; init; }
    public double UsableHours { get; init; }
    public double MeanCloudPercent { get; init; }
    public bool HasForecast { get; init; }
    public double ZenithSqm { get; init; }
    public SkyQualitySource ZenithSqmSource { get; init; }
    public double MeanSkySqm { get; init; } = double.NaN;
    public double MeanSkySqmWithoutMoon { get; init; } = double.NaN;
    public double MoonIlluminatedFraction { get; init; }
    public double MoonSeparationDegrees { get; init; } = double.NaN;

    /// <summary>Characteristic surface brightness used for the signal (mag/arcsec²).</summary>
    public double SurfaceBrightness { get; init; } = double.NaN;

    public bool IsPointLike { get; init; }
    public bool BrightnessAssumed { get; init; }
    public double MountLimitSeconds { get; init; }
    public ExposureLimitSource MountLimitSource { get; init; }
    public double SkyLimitedSeconds { get; init; } = double.PositiveInfinity;
    public double SubSeconds { get; init; }
    public ExposureLimitSource SubLimit { get; init; }
    public int SubCount { get; init; }
    public double ObjectElectronsPerSecond { get; init; }
    public double SkyElectronsPerSecond { get; init; }
    public double SignalToNoise { get; init; }
    public Sampling Sampling { get; init; }
    public DateTime? BestWindowStartLocal { get; init; }
    public DateTime? BestWindowEndLocal { get; init; }
}

/// <summary>Night, site, rig and constraints of a feasibility run (spec §8.1).</summary>
public sealed record FeasibilityRequest(
    ObserverLocation Observer,
    TimeZoneInfo TimeZone,
    NightInfo Night,
    double MinimumAltitudeDegrees,
    RigSetup Rig,
    double ZenithSqm,
    SkyQualitySource ZenithSqmSource = SkyQualitySource.Default,
    HorizonMask? HorizonMask = null,
    DarknessDefinition Darkness = DarknessDefinition.Astronomical,
    WeatherForecast? Weather = null)
{
    public FeasibilityConstants Constants { get; init; } = FeasibilityConstants.Default;

    /// <summary>Forecast seeing and transparency of the night; null (or empty) = the constants' defaults.</summary>
    public NightConditions? Conditions { get; init; }

    /// <summary>
    /// The constants with the night's forecast applied (spec §9.2): the forecast seeing replaces
    /// <see cref="FeasibilityConstants.SeeingArcsec"/>, and the haze above 7Timer's clearest class
    /// (<see cref="FeasibilityConstants.ClearTransparency"/>) is added to the site's extinction.
    /// </summary>
    public FeasibilityConstants EffectiveConstants =>
        Conditions is not { } n ? Constants : Constants with
        {
            SeeingArcsec = n.SeeingArcsec ?? Constants.SeeingArcsec,
            ExtinctionCoefficient = Constants.ExtinctionCoefficient + HazeExtinction
        };

    /// <summary>Extinction added by the forecast transparency, mag/air mass (0 without forecast).</summary>
    public double HazeExtinction =>
        Conditions?.TransparencyMagnitudesPerAirmass is { } t ? Math.Max(0, t - Constants.ClearTransparency) : 0;
}

/// <summary>
/// Every tunable constant, weight and threshold of the feasibility model in one place (spec §8.5),
/// documented in docs/feasibility-model.md. Defaults are deliberately simple and conservative.
/// </summary>
public sealed record FeasibilityConstants
{
    public static FeasibilityConstants Default { get; } = new();

    /// <summary>Photons s⁻¹ cm⁻² of a V = 0 source in a V-wide band (≈ 1000 photons s⁻¹ cm⁻² Å⁻¹ × 880 Å).</summary>
    public double PhotonZeroPoint { get; init; } = 8.8e5;

    /// <summary>Band width collected by a one-shot-colour pixel, in V bands (each Bayer pixel sees about one band).</summary>
    public double ColorBandFactor { get; init; } = 1.0;

    /// <summary>Band width collected by a mono pixel without filter, in V bands.</summary>
    public double MonoBandFactor { get; init; } = 2.5;

    /// <summary>V extinction coefficient, mag/air mass (spec §6).</summary>
    public double ExtinctionCoefficient { get; init; } = 0.25;

    /// <summary>Site glow brightening per air mass (SkyBackground.SiteGlowAt).</summary>
    public double BrighteningPerAirmass { get; init; } = SkyBackground.DefaultBrighteningPerAirmass;

    /// <summary>
    /// Extended objects are evaluated this many magnitudes brighter than their mean surface
    /// brightness: the inner regions, where the detail is, are brighter than the average.
    /// </summary>
    public double CoreBrighteningMagnitudes { get; init; } = 1.0;

    /// <summary>Surface brightness assumed when the catalog gives neither magnitude nor surface brightness.</summary>
    public double AssumedSurfaceBrightness { get; init; } = 22.5;

    /// <summary>Seeing FWHM, arcseconds (a forecast replaces it in M4).</summary>
    public double SeeingArcsec { get; init; } = 2.5;

    /// <summary>Time lost between subs (download, dither), seconds.</summary>
    public double OverheadSeconds { get; init; } = 2;

    /// <summary>Longest sub the model ever suggests, seconds.</summary>
    public double MaximumSubSeconds { get; init; } = 600;

    /// <summary>Stack SNR thresholds: below Marginal = unlikely; Good; Excellent.</summary>
    public double SnrMarginal { get; init; } = 3;
    public double SnrGood { get; init; } = 10;
    public double SnrExcellent { get; init; } = 30;

    /// <summary>Major axis under this many pixels = too small to frame.</summary>
    public double TooSmallPixels { get; init; } = 20;

    /// <summary>Filling less than this fraction of the frame = small.</summary>
    public double SmallFill { get; init; } = 0.1;

    /// <summary>Overlap between mosaic panels.</summary>
    public double MosaicOverlap { get; init; } = 0.2;

    /// <summary>Score multipliers by framing class.</summary>
    public double TooSmallWeight { get; init; } = 0.3;
    public double SmallWeight { get; init; } = 0.9;
    public double TooLargeWeight { get; init; } = 0.85;

    /// <summary>Score boundaries of the verdicts (the SNR thresholds map to 30, ≈ 56 and 80).</summary>
    public int ExcellentScore { get; init; } = 75;
    public int GoodScore { get; init; } = 55;
    public int MarginalScore { get; init; } = 30;

    /// <summary>Usable hours from which the time is a plus, and under which it is a minus.</summary>
    public double ComfortableHours { get; init; } = 3;
    public double ShortHours { get; init; } = 1;

    /// <summary>
    /// 7Timer! transparency (mag/air mass) that counts as clear: its best class is "&lt; 0.3". Only the
    /// excess over it is added to the site's extinction coefficient.
    /// </summary>
    public double ClearTransparency { get; init; } = 0.3;

    /// <summary>
    /// Apparent coma diameter assumed for comets (arcmin): the catalogues give no size, and the
    /// total magnitude is spread over it. A typical value for comets within reach of small gear.
    /// </summary>
    public double CometComaArcmin { get; init; } = 5;

    /// <summary>Sampling step of the night.</summary>
    public TimeSpan Step { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// A target over a sampled night (<see cref="Planning.NightSkySamples"/>), one value per sample:
/// apparent altitude (°), hour angle (° in [−180, 180], 0 = upper transit), whether it can be
/// imaged (dark, above the minimum altitude and the horizon mask) and the rate at which it gathers
/// SNR² there (arbitrary units, comparable only within the same target; 0 when not usable).
/// </summary>
public sealed record TargetNightProfile(
    IReadOnlyList<double> AltitudeDegrees,
    IReadOnlyList<double> HourAngleDegrees,
    IReadOnlyList<bool> Usable,
    IReadOnlyList<double> Quality);
