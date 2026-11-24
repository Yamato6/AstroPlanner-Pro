# Capture-feasibility model

AstroPlanner Pro answers "what can I **photograph** tonight, from here, with my equipment?" with an
**explainable estimate**, not a probability (spec §8). For every (target, rig, site, night) the
model returns a `FeasibilityReport` (`src/AstroPlanner.Core/Imaging/Feasibility`):

- a **verdict**: Excellent / Good / Marginal / Unlikely / Not possible;
- a **score** from 0 to 100;
- the **reasons**: each factor with its value and impact, worst first ("Why this verdict?");
- **recommended settings**: sub length, ISO or gain, number of subs, total integration and window;
- every intermediate value: usable hours, sky brightness with and without the Moon, image scale,
  framing, sub limits, electron rates and signal-to-noise.

The whole catalog (about 13,400 objects) is evaluated in the background in well under a second
(about 70 ms on a Ryzen 7 5700). Filtering never re-runs it. It is recalculated only when the
inputs below change: night, site, horizon, minimum altitude, sky quality, darkness setting,
extinction, active rig or forecast.

Every constant lives in one record, `FeasibilityConstants`. The defaults are listed at the end.

## 1. Inputs (§8.1)

| Input | Where it comes from |
|---|---|
| Target | Catalog: J2000 position, V magnitude, major/minor axes (arcmin), surface brightness. Planets: position, magnitude and disc diameter at mid-night (Core `PlanetCalculator`). |
| Rig | Equipment tab: sensor W×H (mm), resolution, pixel pitch (µm, or W / resolution), colour/mono, QE, read noise RN (e⁻), dark current (e⁻/s), recommended ISO/gain. Optic: focal length f and aperture D (or f-number), transmission. Mount: type and optional maximum unguided sub. Optional reducer or barlow factor. |
| Site | Latitude, longitude, elevation (refraction), horizon mask, zenith SQM (manual, else the light-pollution atlas, else 22.0). |
| Night | Sunset to sunrise, sampled every 5 minutes: Sun altitude, Moon altitude (topocentric), phase angle and direction, and hourly cloud cover when the forecast is on. |
| Constraints | Minimum altitude, darkness definition (astronomical −18° or nautical −12°), extinction coefficient k. |

## 2. Geometry and framing (§8.2)

- Image scale `s = 206.265 · p[µm] / f[mm]` arcsec/px. Field `2·atan(W / 2f)` (and H).
- The effective focal length and focal ratio include the reducer/barlow factor.
- Object size in pixels: `major·60 / s` and `minor·60 / s`. Planets use their apparent disc.
- `fill = max(major / long side, minor / short side)`. The framing class follows from it:

| Class | Condition | Score weight |
|---|---|---|
| too small | major < 20 px | × 0.3, verdict capped at *Unlikely* |
| point of light | no size (stars) | × 0.3, verdict capped at *Unlikely* |
| small | fill < 10 % | × 0.9 |
| good | 10 % ≤ fill ≤ 100 % | × 1 |
| too large | fill > 100 % | × 0.85, plus a mosaic of ⌈(r − 0.2) / 0.8⌉ panels per axis (20 % overlap) |

- Sampling: `FWHM_px = seeing / s` with a seeing of 2.5″ until M4 brings forecasts. Below 1.5 px
  the image is undersampled (normal for wide fields); above 4 px it is oversampled. These are
  rule-of-thumb thresholds and the result is only reported, it is not scored.

## 3. Sub-exposure (§8.3)

1. **Mount limit.**
   - **Fixed tripod:** NPF rule (F. Michaud), `t = (35·N + 30·p) / f`. It is divided by `cos δ`
     for the target's declination, with δ clamped to ±75°.
   - **Tracking mounts:** the user's maximum. Without one, the default for the mount type:

     | Mount | Default maximum sub |
     |---|---|
     | Star tracker | 120 s up to 50 mm, 60 s up to 135 mm, 30 s up to 300 mm, then 15 s |
     | Motorised alt-az | 30 s (field rotation) |
     | Unguided equatorial | 120 s up to 300 mm, else 60 s |
     | Guided equatorial | 300 s |

   - The 500 rule is shown in the Equipment tab for reference only.
2. **Sky-limited sub:** the shortest `t` for which the sky gives `10·RN²` electrons per pixel
   (read noise swamped). Longer subs add little.
3. **Sub length:** `min(mount limit, sky-limited, 600 s)`, rounded down to a value a camera
   offers (… 8, 10, 13, 15, 20, 25, 30 s …). The report records which of the three limits won.

## 4. Sky background at the target (§8.4)

All terms are added in linear units (`SkyBrightness.Combine`).

1. **Zenith baseline (site glow).** The baseline comes from, in order:
   - the SQM typed for the location;
   - the World Atlas 2015 grid: `SQM = −2.5·log10((L_artificial + 0.171) / 1.08·10⁸)`, with
     luminances in mcd/m²;
   - a natural 22.0.

   The atlas reports artificial brightness only and was calibrated for about 1 a.m. Studies found
   it can overestimate in city centres, which is why the manual override exists. The grids are
   described in [data-pipeline.md](data-pipeline.md).
2. **Toward the horizon** (documented heuristic): `L(h) = L_zenith · (1 + a·(X − 1))`, with X the
   Kasten & Young air mass (capped at 5.6) and `a = 0.5`. This makes the sky about 0.44 mag
   brighter at 30° and about 0.7 mag brighter at 20°.
3. **Moonlight:** Krisciunas & Schaefer (1991), V band, using:
   - the phase angle α;
   - the Moon–target separation ρ;
   - the zenith distances of the Moon and the target;
   - the extinction k.

   The model in nanolamberts:
   `B = f(ρ) · I* · 10^(−0.4 k X(Zm)) · (1 − 10^(−0.4 k X(Z)))`, where:
   - `f(ρ) = 10^5.36 (1.06 + cos²ρ) + 10^(6.15 − ρ/40)`;
   - `I* = 10^(−0.4 (3.84 + 0.026|α| + 4·10⁻⁹ α⁴))`;
   - `X(Z) = (1 − 0.96 sin² Z)^−½`.

   It is converted with `V = (20.7233 − ln(B / 34.08)) / 0.92104`. The paper quotes 8–23 %
   accuracy away from full Moon. Later studies (ESO Paranal 2003, SAAO 2022) found real skies
   often brighter than the model, so treat moonlit verdicts as optimistic.
4. **Twilight:** only when nautical twilight is allowed. It uses the zenith twilight table of the
   sky renderer (`SkyBrightness.Twilight`), a display heuristic. With astronomical darkness,
   twilight samples are simply not usable.
5. **Bortle** is a display only, from an approximate SQM table.

## 5. Signal-to-noise (§8.5)

- **Photon zero point.** A V = 0 source gives `F0 = 8.8·10⁵` photons s⁻¹ cm⁻² in a V-wide band
  (≈ 1000 photons s⁻¹ cm⁻² Å⁻¹ × 880 Å).
- **Band factors.** A colour (Bayer) pixel sees about one band: factor 1. A mono pixel without a
  filter sees about 2.5 V bands.
- **Throughput.** Optic transmission × QE × band factor. Collecting area `π (D/2)²`.
- **Per-pixel rate** of a surface of `m` mag/arcsec²:
  `F0 · 10^(−0.4 m) · area · throughput · s²` e⁻/s.
- **Extended objects.**
  - The mean V surface brightness over the catalogued ellipse is
    `m_V + 2.5·log10(π/4 · a · b · 3600)`. The catalog surface brightness is used only when there
    is no V magnitude; OpenNGC's value is not V-band.
  - The target is evaluated 1 mag brighter than that mean: inner regions, where the detail is, are
    brighter than the average.
  - Planets use their mean disc brightness.
  - Without photometry, 22.5 mag/arcsec² is assumed and the report says so.
- **Point sources.** Half the light falls in a core of `π/4 · FWHM_px²` pixels.
- **Extinction.** The object is dimmed by `k·X(h)` in each sample (Kasten & Young air mass). The
  sky is not: the site glow is already as measured from the ground.
- **Usable time.** Samples where all of these hold:
  - the Sun is below the darkness limit;
  - the target is above `max(minimum altitude, horizon mask)`.

  Each one is weighted by `1 − cloud cover` when a forecast exists.
  `T_use = Σ dt · (1 − clouds)`.
- **Stack.**
  - `N = ⌊T_use / (t_sub + overhead)⌋`, with a 2 s overhead;
  - `duty = t_sub / (t_sub + overhead)`;
  - `S = Σ S_o·dt·w · duty`, `B = Σ (S_sky + D)·dt·w · duty`;
  - **SNR = S / √(S + B + N·RN²)** per pixel.

## 6. Score and verdict

- **Score from SNR.** On a log scale, 3 maps to 30 and 30 maps to 80 (10 maps to about 56):
  - SNR below 3: `30 · SNR / 3`;
  - otherwise: `30 + 50 · log10(SNR / 3)`, capped at 100.
- The SNR score is multiplied by the framing weight.

| Verdict | Score |
|---|---|
| Excellent | ≥ 75 |
| Good | ≥ 55 |
| Marginal | ≥ 30 |
| Unlikely | below 30, or too small / a point (cap) |
| Not possible | no darkness, never above the minimum altitude / horizon in darkness, all usable time clouded, or not even one sub fits |

## 7. Explainability

Each reason is a `FeasibilityReason`: a factor, an impact (blocking, negative, neutral, positive)
and its numbers. The UI turns it into text (`Application/Services/FeasibilityText`, `Strings.resx`). The
factors are:

- usable time;
- clouds;
- Moon (Δmag, illumination, separation);
- sky at the target (with the zenith SQM and its source);
- framing (pixels, fill) and mosaic;
- sub length and what limits it;
- SNR;
- sampling;
- assumed brightness.

They are ordered blocking → negative → positive → neutral.

## 8. Tests (§8.6)

`tests/AstroPlanner.Core.Tests/FeasibilityTests.cs` is run in Aguascalientes (21.88° N, 1888 m)
unless noted. It covers sanity scenarios, monotonicity and edge cases.

**Sanity scenarios**

| Scenario | Expected result |
|---|---|
| M31 from Bortle 7 (SQM 18.6), T7 + 16 mm f/2 on a fixed tripod, new Moon | feasible as a wide field: Marginal or better, framing *small*, sub limited by NPF or the sky |
| M57 with 16 mm | *too small*, at most Unlikely |
| M42, T7 + 650 mm f/5 on an equatorial mount, dark winter night (SQM 21.6) | Excellent, good framing, 60 s subs (mount limit) |

**Monotonicity**

- A brighter sky gives a lower SNR and score.
- More usable time (a lower minimum altitude) gives a higher one.
- A larger aperture gives a higher one.
- A full Moon nearby brightens the sky and adds a negative Moon reason.

**Edge cases**

- A target that never rises.
- Polar day (Svalbard in June).
- An overcast forecast (zero clear hours), and a half-cloudy one that halves the usable time.
- A horizon mask.
- Nautical versus astronomical darkness.
- Mosaics.
- Objects without photometry.
- Planets framed by their disc, and stars as points.

**Formula checks**

- The Krisciunas & Schaefer case for a full Moon (α = 0, ρ = 90°, Z = Zm = 0, k = 0.172) gives
  912 nL, or 18.93 V mag/arcsec². The expected values were evaluated by hand from the published
  equations; the scanned paper's comparison table could not be extracted, so this checks the
  implementation of the equations, not new observations.
- The NPF, 500-rule, sky-limited sub and atmospheric pressure scale have unit tests.

## 9. Assumptions and limits

- It is a broadband V-like model: no filters (narrowband, L-eXtreme…), no colour terms, and no
  emission-line advantage. Nebulae imaged through narrowband filters will do better than
  predicted from a city.
- One "characteristic" surface brightness per object. Real objects have bright cores and faint
  halos.
- Without a forecast the seeing is 2.5″ and the extinction is the site's k. With the 7Timer!
  forecast both follow the night (§11).
- No star trailing beyond the NPF limit, no field rotation for alt-az beyond the default limit,
  no dew or wind.
- The thresholds and weights are deliberately simple and tunable. **Settings › Advanced** exposes
  the ones a user can calibrate (`FeasibilityTuning`): seeing without a forecast, core
  brightening, overhead between subs, longest sub, the three stack-SNR thresholds and the pixels
  needed to split a double. Only changed values are stored, so the others follow future defaults.
- **Predictions vs results** (spec §8.7): a photographed logbook entry keeps the verdict, score and
  stack SNR of tonight's report. The Logbook compares the score with the user's 1–5 rating mapped
  to 0–100 (1★ = 0, 5★ = 100): the mean difference per verdict and overall, and the Pearson
  correlation from 3 entries (`PredictionComparison`). Results always better than predicted
  suggest lower SNR thresholds or more core brightening; always worse, the opposite.

## 10. Session planner (spec §4.6)

`Core/Planning/SessionScheduler` places the targets of *My plan* in the night. It works on the
same 5-minute samples as the model (`NightSkySamples`). Each target comes with its report and
its profile (`FeasibilityCalculator.Profile`), which gives per sample:

- the apparent altitude and the hour angle;
- whether it is *usable*: dark, above the minimum altitude and the horizon mask;
- the rate at which it gathers SNR²: `S² / (S + B) × (1 − clouds)`. Here `S` is the object's
  electrons per pixel per second after extinction and `B` the sky's, with moonlight, twilight and
  the site glow toward the horizon. In a shot-noise-limited stack this rate is what a minute of
  exposure is worth.

The steps:

1. **Locks.** Locked targets keep their block as the user left it.
2. **Best stretch.** The others go in priority order (the list order). Each takes the contiguous
   run of free, usable samples with the highest summed SNR² rate. The run is capped at a fair share
   of the free dark time (dark time ÷ targets, never under 20 minutes). Targets visible for less
   than 20 minutes take what they have. A target the model calls *Not possible*, or never usable,
   is reported as such. One whose time was all taken by higher priorities gets *No free time*.
3. **Growth.** In rounds, each block grows by one sample at either end into free time where its
   target is still usable, until nothing changes. Neighbours split the gaps between them, and no
   usable dark time is left idle.
4. **Settings per block.** The model's sub length and the rig's ISO/gain apply. The number of subs
   is ⌊cloud-weighted usable time ÷ (sub + overhead)⌋. The SNR is the night's estimate scaled by
   √(block time ÷ night time). For equatorial mounts the meridian flip is the upper transit (hour
   angle crossing 0) inside the block, interpolated between samples.

Same input, same schedule. *Auto-prioritize* orders the list by visible time (shortest first) and
then by score, which lets every target fit when their windows overlap. The UI keeps the list in
priority order; the exports list the targets by start time.

Tests (`SessionSchedulerTests`):

- a single target takes all its usable time;
- overlapping targets never share time and fill the night;
- the higher priority gets the best stretch;
- locked blocks are kept;
- not-possible and no-free-time cases are reported;
- very short visibility is still scheduled;
- the sub count includes the overhead;
- the meridian flip is given only for equatorial mounts;
- real targets come out in a sensible order (M31 before M42 on a winter evening, M42's flip
  ≈ 01:10);
- the profile matches the report.

## 11. Forecast seeing and transparency (spec §9.2)

When the 7Timer! ASTRO forecast covers the dark night, `NightConditions` (Core/Weather) averages
its 3-hour points over it. `FeasibilityRequest.EffectiveConstants` then applies them:

- **Seeing:** the mean forecast FWHM replaces `SeeingArcsec`. It changes the sampling verdict and
  the seeing core used for point sources. A *Seeing* reason is added, negative from 2.25″.
- **Transparency:** 7Timer!'s scale is in magnitudes per air mass and starts at "< 0.3" for its
  clearest class, above the clean-air V extinction of a good site. So it does not replace k. Only
  the excess over `ClearTransparency` (0.3) is added to the site's k, as haze:
  k = k_site + max(0, T − 0.3). A *Transparency* reason gives T and the added extinction. It is
  positive up to +0.05 and negative from +0.25.

The classes are converted to numbers at the middle of each documented range
(`AstroForecastScales`):

| Class | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Seeing (″) | 0.4 | 0.625 | 0.875 | 1.125 | 1.375 | 1.75 | 2.25 | 2.75 |
| Transparency (mag/air mass) | 0.25 | 0.35 | 0.45 | 0.55 | 0.65 | 0.775 | 0.925 | 1.1 |

**Dew and wind** (`NightConditions`, from Open-Meteo, not part of the score) are warnings in
Tonight:

- *dew likely* when the air comes within 2 °C of its dew point, *possible* within 4 °C. Optics
  radiate to the sky and cool below the air, so they fog before the air saturates;
- *wind* when gusts exceed 30 km/h for a camera on a tripod or tracker, or 20 km/h for a telescope
  (alt-az or equatorial).

The thresholds are in `ConditionThresholds`. Without a forecast nothing changes, and the verdicts
are exactly those of §1–§9. Tests: `NightConditionsTests`.

## 12. Comets and asteroids (M5)

Their position, distances and magnitude come from `OrbitCalculator` at the middle of the night
(the MPC formulae: comets H + 5 log Δ + 2.5 K log r, asteroids H-G). Asteroids are point sources.
A comet's total magnitude is spread over an assumed coma of `CometComaArcmin` (5′), since the
catalogues give no size; a tail is not modelled. The Milky Way core is an extended target of
30° × 20° with no magnitude: its verdict says mostly whether the rig's field can frame it.

## 13. Stars and double stars (M6)

Stars are point sources: their light is spread over the seeing core, the framing is
`PointLike` and the verdict is never better than Unlikely (a star alone is not an
astrophotography target). For a double star the report adds a **Resolution** reason about its
main pair (the brightest companion at least 1″ away, not inside an unresolved component):

    limit = max(Dawes 116″ / D[mm], DoubleStarSplitPixels (2) × image scale, seeing FWHM)

The pair is split comfortably when its separation is at least `DoubleStarComfortFactor` (1.5) ×
the limit, split when above the limit, and not split below it. Example: Albireo (34.3″) is split
by the T7 on the 650 mm telescope (limit 2.5″, the seeing) and not by the 16 mm lens (limit 96″,
two pixels). The magnitude difference of the pair is not modelled. Separations and angles are the
WDS's last measures; orbits are not propagated.

## Default constants (`FeasibilityConstants.Default`)

| Constant | Value |
|---|---|
| PhotonZeroPoint | 8.8·10⁵ photons s⁻¹ cm⁻² |
| ColorBandFactor / MonoBandFactor | 1 / 2.5 |
| ExtinctionCoefficient | 0.25 mag/air mass (Settings) |
| BrighteningPerAirmass | 0.5 |
| CoreBrighteningMagnitudes | 1.0 |
| AssumedSurfaceBrightness | 22.5 mag/arcsec² |
| SeeingArcsec | 2.5 |
| OverheadSeconds | 2 |
| MaximumSubSeconds | 600 |
| SnrMarginal / SnrGood / SnrExcellent | 3 / 10 / 30 |
| TooSmallPixels / SmallFill / MosaicOverlap | 20 px / 0.1 / 0.2 |
| TooSmallWeight / SmallWeight / TooLargeWeight | 0.3 / 0.9 / 0.85 |
| ExcellentScore / GoodScore / MarginalScore | 75 / 55 / 30 |
| Step | 5 min |
| SessionSchedulerOptions.MinimumBlock | 20 min |
| ClearTransparency | 0.3 mag/air mass |
| CometComaArcmin | 5′ |
| ConditionThresholds (dew high / moderate; gusts camera / telescope) | 2 / 4 °C; 30 / 20 km/h |

## References

- Krisciunas, K. & Schaefer, B. E. (1991). A model of the brightness of moonlight. *PASP* 103, 1033.
- Falchi, F. et al. (2016). The new world atlas of artificial night sky brightness. *Science
  Advances* 2, e1600377. Data: doi:10.5880/GFZ.1.4.2016.001 (CC BY-NC 4.0).
- Kasten, F. & Young, A. T. (1989). Revised optical air mass tables and approximation formula.
  *Applied Optics* 28, 4735.
- Michaud, F. NPF rule, Société Astronomique du Havre.
- Patat, F. (2003). UBVRI night sky brightness during sunspot maximum at ESO-Paranal. *A&A* 400, 1183.
