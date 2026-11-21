using AstroPlanner.Core.Algorithms;
using AstroPlanner.Core.Interfaces;
using AstroPlanner.Core.Models;
using AstroPlanner.Core.Planning;
using AstroPlanner.Core.SkyQuality;

namespace AstroPlanner.Core.Imaging.Feasibility;

/// <summary>
/// The capture-feasibility model of spec §8: for a (target, rig, site, night) it estimates the
/// usable time, the sky background at the target, the longest sensible sub-exposure and the
/// signal-to-noise of the whole stack, and turns them into an explainable verdict and score.
/// Formulas and assumptions: docs/feasibility-model.md. Deep-sky objects are precessed once for
/// the night; planets use their position, magnitude and disc size at mid-night (they change little
/// in one night). Objects without a size (stars) are points: nothing to frame, so at most "unlikely".
/// </summary>
public sealed class FeasibilityCalculator(IAstronomyService astronomy)
{
    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Below the minimum altitude by more than this, the horizon mask is not evaluated.</summary>
    private const double AzimuthMarginDegrees = 10.0;

    private readonly IAstronomyService _astronomy = astronomy ?? throw new ArgumentNullException(nameof(astronomy));

    /// <summary>The sampled night shared by every target of <paramref name="request"/>.</summary>
    public static NightSkySamples CreateSamples(FeasibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = request.Darkness == DarknessDefinition.Nautical
            ? NightSkySamples.NauticalDarknessDegrees
            : NightSkySamples.AstronomicalDarknessDegrees;
        return NightSkySamples.Create(request.Observer, request.TimeZone, request.Night, request.Constants.Step, limit, request.Weather);
    }

    /// <summary>Reports for <paramref name="targets"/> (same order), evaluated in parallel.</summary>
    public IReadOnlyList<FeasibilityReport> EvaluateAll(
        IReadOnlyList<CelestialObject> targets,
        FeasibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var night = CreateSamples(request);
        var reports = new FeasibilityReport[targets.Count];
        Parallel.For(0, targets.Count, new ParallelOptions { CancellationToken = cancellationToken },
            index => reports[index] = Evaluate(targets[index], request, night));
        return reports;
    }

    /// <summary>Report for one target.</summary>
    public FeasibilityReport Evaluate(CelestialObject target, FeasibilityRequest request) =>
        Evaluate(target, request, CreateSamples(request));

    /// <summary>Report for one target on an already sampled night.</summary>
    public FeasibilityReport Evaluate(CelestialObject target, FeasibilityRequest request, NightSkySamples night)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(night);

        var c = request.EffectiveConstants;
        var rig = request.Rig;
        var p = Prepare(target, request, night);
        var (ofDate, scale, surfaceRate, sampling) = (p.OfDate, p.ScaleArcsecPerPixel, p.SurfaceRate, p.Sampling);
        var (majorArcmin, minorArcmin, pointLike, assumed, surfaceBrightness) =
            (p.MajorArcmin, p.MinorArcmin, p.PointLike, p.Assumed, p.SurfaceBrightness);
        var (tx, ty, tz) = NightSkySamples.UnitVector(ofDate);

        var framing = Frame(majorArcmin, minorArcmin, rig, scale, pointLike, c);
        var (mountLimit, mountSource) = ExposureLimits.MountLimit(rig, ofDate.DeclinationDegrees);

        // --- Walk the night ------------------------------------------------------------------
        var track = new Track(ofDate, request, night);
        var includeTwilight = request.Darkness == DarknessDefinition.Nautical;

        var anyDark = false;
        var maximumAltitude = double.NegativeInfinity;
        double geometricSeconds = 0, usableSeconds = 0, cloudSeconds = 0;
        double objectElectrons = 0, skyElectrons = 0;
        double skyFlux = 0, skyFluxNoMoon = 0;
        double moonFraction = 0, moonSeparation = 0;
        int bestStart = -1, bestEnd = -1, runStart = -1;

        for (var k = 0; k < night.Count; k++)
        {
            var (_, altitude, usable) = track.At(k);
            maximumAltitude = Math.Max(maximumAltitude, altitude);
            anyDark |= night.IsDark[k];
            if (!usable)
            {
                if (runStart >= 0)
                {
                    CloseRun(k - 1);
                }

                continue;
            }

            if (runStart < 0)
            {
                runStart = k;
            }

            var dt = night.DurationsSeconds[k];
            var weight = dt * (1 - night.CloudFraction[k]);
            geometricSeconds += dt;
            usableSeconds += weight;
            cloudSeconds += night.CloudFraction[k] * dt;

            var separation = night.MoonSeparationDegrees(k, tx, ty, tz);
            var sky = SkyBackground.At(request.ZenithSqm, altitude, night.SunAltitudeDegrees[k], includeTwilight,
                night.MoonAltitudeDegrees[k], night.MoonPhaseAngleDegrees[k], separation, c.ExtinctionCoefficient, c.BrighteningPerAirmass);
            skyFlux += Math.Pow(10, -0.4 * sky.TotalSqm) * dt;
            skyFluxNoMoon += Math.Pow(10, -0.4 * SkyBrightness.Combine(sky.SiteSqm, sky.TwilightSqm)) * dt;
            moonFraction += (night.MoonAltitudeDegrees[k] > 0 ? night.MoonIlluminatedFraction[k] : 0) * dt;
            moonSeparation += separation * dt;

            var extinction = c.ExtinctionCoefficient * Math.Min(Airmass.KastenYoung(altitude), 40);
            objectElectrons += surfaceRate * Math.Pow(10, -0.4 * (surfaceBrightness + extinction)) * weight;
            skyElectrons += surfaceRate * Math.Pow(10, -0.4 * sky.TotalSqm) * weight;
        }

        if (runStart >= 0)
        {
            CloseRun(night.Count - 1);
        }

        var hasForecast = night.HasForecast;
        var report = new FeasibilityReport
        {
            Verdict = FeasibilityVerdict.NotPossible,
            Score = 0,
            Reasons = [],
            Framing = framing,
            MaximumAltitudeDegrees = double.IsFinite(maximumAltitude) ? maximumAltitude : double.NaN,
            GeometricHours = geometricSeconds / 3600,
            UsableHours = usableSeconds / 3600,
            MeanCloudPercent = geometricSeconds > 0 ? 100 * cloudSeconds / geometricSeconds : 0,
            HasForecast = hasForecast,
            ZenithSqm = request.ZenithSqm,
            ZenithSqmSource = request.ZenithSqmSource,
            SurfaceBrightness = surfaceBrightness,
            IsPointLike = pointLike,
            BrightnessAssumed = assumed,
            MountLimitSeconds = mountLimit,
            MountLimitSource = mountSource,
            Sampling = sampling
        };

        // --- Nothing to capture ---------------------------------------------------------------
        if (!anyDark)
        {
            return report with { Reasons = [new FeasibilityReason(FeasibilityFactor.NoDarkness, ReasonImpact.Blocking)] };
        }

        if (geometricSeconds <= 0)
        {
            return report with
            {
                Reasons = [new FeasibilityReason(FeasibilityFactor.NeverAboveMinimum, ReasonImpact.Blocking, report.MaximumAltitudeDegrees)]
            };
        }

        var meanSky = -2.5 * Math.Log10(skyFlux / geometricSeconds);
        var meanSkyNoMoon = -2.5 * Math.Log10(skyFluxNoMoon / geometricSeconds);
        report = report with
        {
            MeanSkySqm = meanSky,
            MeanSkySqmWithoutMoon = meanSkyNoMoon,
            MoonIlluminatedFraction = moonFraction / geometricSeconds,
            MoonSeparationDegrees = moonSeparation / geometricSeconds,
            BestWindowStartLocal = Local(night.TimesUtc[bestStart]),
            BestWindowEndLocal = Local(night.TimesUtc[bestEnd].AddSeconds(night.DurationsSeconds[bestEnd]))
        };

        if (usableSeconds < 1)
        {
            return report with
            {
                Reasons = [new FeasibilityReason(FeasibilityFactor.NoClearTime, ReasonImpact.Blocking, report.GeometricHours)]
            };
        }

        // --- Sub-exposure and stack SNR (spec §8.3, §8.5) --------------------------------------
        var objectRate = objectElectrons / usableSeconds;
        var skyRate = skyElectrons / usableSeconds;
        var readNoise = rig.Camera.ReadNoiseElectrons;
        var skyLimited = ExposureLimits.SkyLimitedSeconds(skyRate, readNoise);
        var (rawSub, subLimit) = (mountLimit, mountSource);
        if (skyLimited < rawSub)
        {
            (rawSub, subLimit) = (skyLimited, ExposureLimitSource.SkyLimited);
        }

        if (c.MaximumSubSeconds < rawSub)
        {
            (rawSub, subLimit) = (c.MaximumSubSeconds, ExposureLimitSource.Cap);
        }

        var sub = ExposureLimits.RoundDownToNice(rawSub);
        var cycle = sub + c.OverheadSeconds;
        var subCount = (int)Math.Floor(usableSeconds / cycle);
        var duty = sub / cycle;
        var signal = objectElectrons * duty;
        var background = (skyElectrons + rig.Camera.DarkCurrentElectronsPerSecond * usableSeconds) * duty;
        var snr = subCount > 0 ? signal / Math.Sqrt(signal + background + subCount * readNoise * readNoise) : 0;

        // --- Score and verdict ------------------------------------------------------------------
        var snrScore = snr < c.SnrMarginal
            ? c.MarginalScore * snr / c.SnrMarginal
            : c.MarginalScore + (80.0 - c.MarginalScore) * Math.Log10(snr / c.SnrMarginal) / Math.Log10(c.SnrExcellent / c.SnrMarginal);
        var framingWeight = framing.Class switch
        {
            FramingClass.TooSmall or FramingClass.PointLike => c.TooSmallWeight,
            FramingClass.Small => c.SmallWeight,
            FramingClass.TooLarge => c.TooLargeWeight,
            _ => 1.0
        };
        var score = (int)Math.Round(Math.Clamp(snrScore * framingWeight, 0, 100));
        var verdict = subCount == 0 ? FeasibilityVerdict.NotPossible
            : score >= c.ExcellentScore ? FeasibilityVerdict.Excellent
            : score >= c.GoodScore ? FeasibilityVerdict.Good
            : score >= c.MarginalScore ? FeasibilityVerdict.Marginal
            : FeasibilityVerdict.Unlikely;
        if (framing.Class is FramingClass.TooSmall or FramingClass.PointLike && verdict > FeasibilityVerdict.Unlikely)
        {
            verdict = FeasibilityVerdict.Unlikely;
        }

        var reasons = Explain(report, framing, sub, subLimit, snr, sampling, assumed, surfaceBrightness, c, request);
        if (target.Star?.MainPair is { } pair)
        {
            var limit = ResolutionLimitArcsec(rig.ApertureMm, scale, c.SeeingArcsec, c);
            var impact = pair.SeparationArcsec >= limit * c.DoubleStarComfortFactor ? ReasonImpact.Positive
                : pair.SeparationArcsec >= limit ? ReasonImpact.Neutral
                : ReasonImpact.Negative;
            reasons.Insert(impact == ReasonImpact.Negative ? 0 : reasons.Count,
                new FeasibilityReason(FeasibilityFactor.Resolution, impact, pair.SeparationArcsec, limit, pair.CompanionMagnitude));
        }

        var total = TimeSpan.FromSeconds(subCount * sub);
        return report with
        {
            Verdict = verdict,
            Score = score,
            Reasons = reasons,
            SkyLimitedSeconds = skyLimited,
            SubSeconds = sub,
            SubLimit = subLimit,
            SubCount = subCount,
            ObjectElectronsPerSecond = objectRate,
            SkyElectronsPerSecond = skyRate,
            SignalToNoise = snr,
            Recommended = subCount == 0 ? null : new RecommendedSettings(
                sub, rig.Camera.RecommendedIso, rig.Camera.RecommendedGain, subCount, total,
                report.BestWindowStartLocal, report.BestWindowEndLocal)
        };

        void CloseRun(int last)
        {
            if (bestStart < 0 || night.TimesUtc[last] - night.TimesUtc[runStart] > night.TimesUtc[bestEnd] - night.TimesUtc[bestStart])
            {
                bestStart = runStart;
                bestEnd = last;
            }

            runStart = -1;
        }

        DateTime Local(DateTime utc) => TimeZoneConversion.ToLocal(utc, request.TimeZone);
    }

    /// <summary>
    /// The target sample by sample on an already sampled night: altitude, hour angle, whether it
    /// can be imaged, and how fast it gathers signal-to-noise there — what the session scheduler
    /// (<see cref="Planning.SessionScheduler"/>) needs to place it in the night.
    /// </summary>
    public TargetNightProfile Profile(CelestialObject target, FeasibilityRequest request, NightSkySamples night)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(night);

        var c = request.EffectiveConstants;
        var p = Prepare(target, request, night);
        var (tx, ty, tz) = NightSkySamples.UnitVector(p.OfDate);
        var track = new Track(p.OfDate, request, night);
        var includeTwilight = request.Darkness == DarknessDefinition.Nautical;
        var altitudes = new double[night.Count];
        var hourAngles = new double[night.Count];
        var usable = new bool[night.Count];
        var quality = new double[night.Count];
        for (var k = 0; k < night.Count; k++)
        {
            var (hourAngle, altitude, isUsable) = track.At(k);
            altitudes[k] = altitude;
            hourAngles[k] = Math.IEEERemainder(hourAngle / DegreesToRadians, 360.0);
            usable[k] = isUsable;
            if (!isUsable)
            {
                continue;
            }

            var separation = night.MoonSeparationDegrees(k, tx, ty, tz);
            var sky = SkyBackground.At(request.ZenithSqm, altitude, night.SunAltitudeDegrees[k], includeTwilight,
                night.MoonAltitudeDegrees[k], night.MoonPhaseAngleDegrees[k], separation, c.ExtinctionCoefficient, c.BrighteningPerAirmass);
            var extinction = c.ExtinctionCoefficient * Math.Min(Airmass.KastenYoung(altitude), 40);
            var signal = p.SurfaceRate * Math.Pow(10, -0.4 * (p.SurfaceBrightness + extinction));
            var background = p.SurfaceRate * Math.Pow(10, -0.4 * sky.TotalSqm);
            // SNR² gathered per second (shot-noise limited), scaled by the clear fraction.
            quality[k] = signal * signal / (signal + background) * (1 - night.CloudFraction[k]);
        }

        return new TargetNightProfile(altitudes, hourAngles, usable, quality);
    }

    /// <summary>Altitude and "can be imaged" of a fixed position, sample by sample.</summary>
    private sealed class Track
    {
        private readonly NightSkySamples _night;
        private readonly HorizonMask? _mask;
        private readonly double _minimumAltitude;
        private readonly double _raDegrees;
        private readonly double _sinDec;
        private readonly double _cosDec;

        public Track(EquatorialCoordinates ofDate, FeasibilityRequest request, NightSkySamples night)
        {
            _night = night;
            _mask = request.HorizonMask is { IsFlat: false } m ? m : null;
            _minimumAltitude = request.MinimumAltitudeDegrees;
            _raDegrees = ofDate.RightAscensionHours * 15.0;
            var declination = ofDate.DeclinationDegrees * DegreesToRadians;
            _sinDec = Math.Sin(declination);
            _cosDec = Math.Cos(declination);
        }

        /// <returns>Hour angle (radians, unnormalised), apparent altitude (°) and whether sample k is usable.</returns>
        public (double HourAngle, double Altitude, bool Usable) At(int k)
        {
            var night = _night;
            var hourAngle = (night.LocalSiderealTimeDegrees[k] - _raDegrees) * DegreesToRadians;
            var sinAltitude = Math.Clamp(night.SinLatitude * _sinDec + night.CosLatitude * _cosDec * Math.Cos(hourAngle), -1, 1);
            var altitude = Refraction.ApparentAltitude(Math.Asin(sinAltitude) / DegreesToRadians, night.RefractionScale);

            var threshold = _minimumAltitude;
            if (_mask is not null && altitude >= threshold - AzimuthMarginDegrees)
            {
                var azimuth = Math.Atan2(-_cosDec * Math.Sin(hourAngle),
                    _sinDec * night.CosLatitude - _cosDec * Math.Cos(hourAngle) * night.SinLatitude) / DegreesToRadians;
                threshold = Math.Max(threshold, _mask.AltitudeAt(AstronomyMath.NormalizeDegrees(azimuth)));
            }

            return (hourAngle, altitude, night.IsDark[k] && altitude >= threshold && night.DurationsSeconds[k] > 0);
        }
    }

    /// <summary>Position of date, photometry and size of a target: what does not change during the night.</summary>
    private sealed record Prepared(
        EquatorialCoordinates OfDate,
        double ScaleArcsecPerPixel,
        double SurfaceRate,
        Sampling Sampling,
        double MajorArcmin,
        double MinorArcmin,
        bool PointLike,
        bool Assumed,
        double SurfaceBrightness);

    private Prepared Prepare(CelestialObject target, FeasibilityRequest request, NightSkySamples night)
    {
        var c = request.EffectiveConstants;
        var rig = request.Rig;
        var middleUtc = night.TimesUtc.Length > 0 ? night.TimesUtc[night.TimesUtc.Length / 2] : DateTime.UtcNow;

        // --- Position of date ---------------------------------------------------------------
        var planet = target.IsPlanet ? _astronomy.GetPlanetPosition(target, middleUtc) : null;
        var smallBody = target.IsSmallBody ? _astronomy.GetSmallBodyPosition(target, middleUtc) : null;
        var ofDate = planet?.Equatorial ?? smallBody?.Equatorial
            ?? Precession.Precess(target.Coordinates, Precession.J2000JulianDate, night.MiddleJulianDate);

        // --- Photometry and optics (spec §8.5) ------------------------------------------------
        var scale = rig.ImageScaleArcsecPerPixel;
        var areaCm2 = Math.PI * Math.Pow(rig.ApertureMm / 20.0, 2);
        var band = rig.Camera.Color == SensorColor.Color ? c.ColorBandFactor : c.MonoBandFactor;
        var throughput = rig.Optic.Transmission * rig.Camera.QuantumEfficiency * band;
        // Electrons per second per pixel of a surface of 1 mag/arcsec² = 0 (scaled by 10^(−0.4·SB) below).
        var surfaceRate = c.PhotonZeroPoint * areaCm2 * throughput * scale * scale;
        var sampling = OpticsMath.SamplingFor(c.SeeingArcsec, scale);

        // Size: the catalogued axes, the planet's disc, a typical coma for comets; none = a point
        // (stars, asteroids).
        var majorArcmin = planet is not null ? planet.ApparentDiameterArcsec / 60
            : target.Type == CelestialObjectType.Comet ? c.CometComaArcmin
            : target.MajorAxisArcmin ?? 0;
        var minorArcmin = planet is not null || target.IsSmallBody ? majorArcmin
            : target.MinorAxisArcmin is > 0 ? target.MinorAxisArcmin.Value : majorArcmin;
        var pointLike = !(majorArcmin > 0) || target.Type is CelestialObjectType.Star or CelestialObjectType.DoubleStar;
        var magnitude = planet?.Magnitude ?? smallBody?.Magnitude ?? target.ApparentMagnitude;
        var assumed = false;
        double surfaceBrightness;
        if (pointLike)
        {
            if (!double.IsFinite(magnitude))
            {
                magnitude = 12;
                assumed = true;
            }

            // Half the light in the seeing core of π/4·FWHM² pixels, expressed as an equivalent
            // surface brightness so that both cases share the per-pixel rate below.
            var fwhmPixels = Math.Max(1, sampling.FwhmPixels);
            var corePixels = Math.Max(1, Math.PI / 4 * fwhmPixels * fwhmPixels);
            surfaceBrightness = magnitude + 2.5 * Math.Log10(2 * corePixels * scale * scale);
        }
        else
        {
            var major = majorArcmin;
            var minor = minorArcmin;
            double mean;
            if (double.IsFinite(magnitude))
            {
                // Mean V surface brightness over the catalogued ellipse.
                mean = magnitude + 2.5 * Math.Log10(Math.PI / 4 * major * minor * 3600);
            }
            else if (target.SurfaceBrightness is { } catalogued)
            {
                mean = catalogued;
            }
            else
            {
                mean = c.AssumedSurfaceBrightness;
                assumed = true;
            }

            // A planet's disc is evenly lit; nebulae and galaxies are brighter inside.
            surfaceBrightness = planet is not null ? mean : mean - c.CoreBrighteningMagnitudes;
        }

        return new Prepared(ofDate, scale, surfaceRate, sampling, majorArcmin, minorArcmin, pointLike, assumed, surfaceBrightness);
    }

    private static FramingAssessment Frame(double majorArcmin, double minorArcmin, RigSetup rig, double scale, bool pointLike, FeasibilityConstants c)
    {
        if (pointLike)
        {
            return new FramingAssessment(FramingClass.PointLike, 0, 0, 0, 1);
        }

        var majorPixels = OpticsMath.Pixels(majorArcmin, scale);
        var minorPixels = OpticsMath.Pixels(minorArcmin, scale);
        double longSide = Math.Max(rig.Camera.ResolutionWidthPx, rig.Camera.ResolutionHeightPx);
        double shortSide = Math.Min(rig.Camera.ResolutionWidthPx, rig.Camera.ResolutionHeightPx);
        var fill = Math.Max(majorPixels / longSide, minorPixels / shortSide);
        var panels = 1;
        var framing = majorPixels < c.TooSmallPixels ? FramingClass.TooSmall
            : fill < c.SmallFill ? FramingClass.Small
            : fill <= 1 ? FramingClass.Good
            : FramingClass.TooLarge;
        if (framing == FramingClass.TooLarge)
        {
            var step = 1 - c.MosaicOverlap;
            panels = (int)(Math.Max(1, Math.Ceiling((majorPixels / longSide - c.MosaicOverlap) / step))
                           * Math.Max(1, Math.Ceiling((minorPixels / shortSide - c.MosaicOverlap) / step)));
        }

        return new FramingAssessment(framing, majorPixels, minorPixels, fill, panels);
    }

    /// <summary>
    /// Smallest separation (″) at which the rig shows a double star as two: the largest of the
    /// Dawes limit of the aperture, <see cref="FeasibilityConstants.DoubleStarSplitPixels"/> pixels and the seeing.
    /// </summary>
    public static double ResolutionLimitArcsec(double apertureMm, double imageScaleArcsecPerPixel, double seeingArcsec, FeasibilityConstants constants)
    {
        ArgumentNullException.ThrowIfNull(constants);
        return Math.Max(Math.Max(constants.DawesArcsecMillimetres / apertureMm, constants.DoubleStarSplitPixels * imageScaleArcsecPerPixel), seeingArcsec);
    }

    private static List<FeasibilityReason> Explain(
        FeasibilityReport report,
        FramingAssessment framing,
        double sub,
        ExposureLimitSource subLimit,
        double snr,
        Sampling sampling,
        bool assumed,
        double surfaceBrightness,
        FeasibilityConstants c,
        FeasibilityRequest request)
    {
        var reasons = new List<FeasibilityReason>
        {
            new(FeasibilityFactor.SignalToNoise,
                snr < c.SnrMarginal ? ReasonImpact.Negative : snr < c.SnrGood ? ReasonImpact.Neutral : ReasonImpact.Positive, snr),
            new(FeasibilityFactor.UsableTime,
                report.UsableHours >= c.ComfortableHours ? ReasonImpact.Positive
                : report.UsableHours < c.ShortHours ? ReasonImpact.Negative : ReasonImpact.Neutral,
                report.UsableHours, report.GeometricHours)
        };

        if (report.HasForecast && report.MeanCloudPercent > 10)
        {
            reasons.Add(new(FeasibilityFactor.Clouds, report.MeanCloudPercent > 50 ? ReasonImpact.Negative : ReasonImpact.Neutral,
                report.MeanCloudPercent));
        }

        var moonBrightening = report.MeanSkySqmWithoutMoon - report.MeanSkySqm;
        if (moonBrightening > 0.05)
        {
            reasons.Add(new(FeasibilityFactor.Moonlight, moonBrightening > 0.3 ? ReasonImpact.Negative : ReasonImpact.Neutral,
                moonBrightening, report.MoonIlluminatedFraction, report.MoonSeparationDegrees));
        }

        reasons.Add(new(FeasibilityFactor.SkyBackground,
            report.MeanSkySqm < 19.5 ? ReasonImpact.Negative : report.MeanSkySqm > 21 ? ReasonImpact.Positive : ReasonImpact.Neutral,
            report.MeanSkySqm, report.ZenithSqm) { Source = report.ZenithSqmSource });

        {
            var impact = framing.Class switch
            {
                FramingClass.TooSmall or FramingClass.TooLarge or FramingClass.PointLike => ReasonImpact.Negative,
                FramingClass.Good => ReasonImpact.Positive,
                _ => ReasonImpact.Neutral
            };
            reasons.Add(new(FeasibilityFactor.Framing, impact, framing.MajorPixels, framing.Fill) { Framing = framing.Class });
            if (framing.MosaicPanels > 1)
            {
                reasons.Add(new(FeasibilityFactor.Mosaic, ReasonImpact.Neutral, framing.MosaicPanels));
            }
        }

        var trackingLimited = subLimit is ExposureLimitSource.Npf or ExposureLimitSource.Mount;
        reasons.Add(new(FeasibilityFactor.SubExposure, trackingLimited && sub < 10 ? ReasonImpact.Negative : ReasonImpact.Neutral, sub)
        {
            Limit = subLimit
        });

        if (sampling.Verdict != SamplingVerdict.Good)
        {
            reasons.Add(new(FeasibilityFactor.Sampling, ReasonImpact.Neutral, sampling.FwhmPixels, c.SeeingArcsec / sampling.FwhmPixels)
            {
                Sampling = sampling.Verdict
            });
        }

        if (assumed)
        {
            reasons.Add(new(FeasibilityFactor.AssumedBrightness, ReasonImpact.Neutral, surfaceBrightness));
        }

        if (request.Conditions is { SeeingArcsec: { } seeing })
        {
            reasons.Add(new(FeasibilityFactor.Seeing, seeing >= 2.25 ? ReasonImpact.Negative : ReasonImpact.Neutral, seeing));
        }

        if (request.Conditions is { TransparencyMagnitudesPerAirmass: { } transparency })
        {
            var haze = request.HazeExtinction;
            reasons.Add(new(FeasibilityFactor.Transparency,
                haze >= 0.25 ? ReasonImpact.Negative : haze <= 0.05 ? ReasonImpact.Positive : ReasonImpact.Neutral,
                transparency, haze));
        }

        // Blocking first, then what hurts, what helps, and the rest (stable within each group).
        return [.. reasons.Select((r, i) => (r, i)).OrderBy(p => p.r.Impact switch
        {
            ReasonImpact.Blocking => 0,
            ReasonImpact.Negative => 1,
            ReasonImpact.Positive => 2,
            _ => 3
        }).ThenBy(p => p.i).Select(p => p.r)];
    }
}
