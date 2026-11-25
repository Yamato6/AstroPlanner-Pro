// Sky brightness over the sky (spec §8.2): Core sends the levels at the zenith (the site's glow,
// Krisciunas & Schaefer moonlight, twilight — SkyAppearancePayload); these functions spread them
// over the sky with the same angular laws as Core's SkyBackground, so the sky the renderer draws is
// the sky the feasibility engine uses. Cross-checked against Core by test/photometry.test.ts
// (tests/fixtures/bridge/photometry.json, ≤ 0.05 mag) and mirrored in GLSL (shaders.ts,
// `photometry`). Brightness is in mag/arcsec² (SQM); "flux" is 10^(−0.4·SQM).
import { NO_LIGHT_SQM } from '../bridge/schema';

const DEG = Math.PI / 180;

/** The site glow's air mass is capped here (≈ 10° altitude), like SkyBackground.SiteGlowAt. */
export const MAX_GLOW_AIRMASS = 5.6;
/** Star extinction uses the air mass up to here (as the feasibility engine). */
export const MAX_EXTINCTION_AIRMASS = 40;

/** Linear flux of a brightness (0 for an absent part). */
export function fluxOf(sqm: number): number {
  return sqm >= NO_LIGHT_SQM ? 0 : 10 ** (-0.4 * sqm);
}

/** Brightness of a flux (infinity-like {@link NO_LIGHT_SQM} for none). */
export function sqmOf(flux: number): number {
  return flux > 0 ? -2.5 * Math.log10(flux) : NO_LIGHT_SQM;
}

/** Kasten & Young (1989) relative air mass; infinity well below the horizon (Core Airmass.KastenYoung). */
export function kastenYoung(altitudeDegrees: number): number {
  const shifted = altitudeDegrees + 6.07995;
  if (Number.isNaN(altitudeDegrees) || shifted <= 0) {
    return Number.POSITIVE_INFINITY;
  }
  const denominator = Math.sin(altitudeDegrees * DEG) + 0.50572 * shifted ** -1.6364;
  return denominator <= 0 ? Number.POSITIVE_INFINITY : 1 / denominator;
}

/** Brightening of the site's glow toward an altitude: 1 + a·(X − 1), X capped (SkyBackground.SiteGlowAt). */
export function siteGlowFactor(altitudeDegrees: number, brighteningPerAirmass: number): number {
  const airmass = Math.min(kastenYoung(Math.max(altitudeDegrees, 0)), MAX_GLOW_AIRMASS);
  return 1 + Math.max(0, brighteningPerAirmass) * (airmass - 1);
}

/** Krisciunas & Schaefer's air mass for scattering paths: (1 − 0.96 sin² Z)^−½. */
export function scatteringAirmass(zenithDistanceDegrees: number): number {
  const sine = Math.sin(zenithDistanceDegrees * DEG);
  return 1 / Math.sqrt(1 - 0.96 * sine * sine);
}

/** Krisciunas & Schaefer's scattering function f(ρ): Rayleigh + Mie. */
export function scatteringFunction(separationDegrees: number): number {
  const cosine = Math.cos(separationDegrees * DEG);
  return 10 ** 5.36 * (1.06 + cosine * cosine) + 10 ** (6.15 - separationDegrees / 40);
}

/**
 * Moonlight toward a direction relative to the zenith: the parts of the K&S formula that depend
 * on the direction (f(ρ) and the sky's air mass); the Moon's own terms cancel.
 */
export function moonlightRatio(
  separationDegrees: number,
  zenithSeparationDegrees: number,
  skyZenithDistanceDegrees: number,
  extinction: number,
): number {
  const sky = Math.min(skyZenithDistanceDegrees, 90);
  const toward = 1 - 10 ** (-0.4 * extinction * scatteringAirmass(sky));
  const atZenith = 1 - 10 ** (-0.4 * extinction);
  if (atZenith <= 0) {
    return 0;
  }
  return (
    (scatteringFunction(separationDegrees) / scatteringFunction(zenithSeparationDegrees)) *
    (toward / atZenith)
  );
}

/** Naked-eye limiting magnitude of a sky (Schaefer 1990, Core SkyBrightness.NakedEyeLimitingMagnitude). */
export function nakedEyeLimit(sqm: number): number {
  return 7.93 - 5 * Math.log10(10 ** (4.316 - sqm / 5) + 1);
}

/** The zenith levels at an instant (interpolated from the Core samples). */
export interface ZenithLevels {
  /** The site's own sky (natural + artificial). */
  readonly siteSqm: number;
  readonly twilightSqm: number;
  readonly moonlightSqm: number;
  readonly extinction: number;
  readonly brighteningPerAirmass: number;
}

/**
 * Sky brightness toward an apparent altitude, given the Moon's altitude and its angular distance.
 * `twilightShape` is the twilight relative to the zenith in that direction (1 = as at the zenith;
 * the shader takes it from the scattering table).
 */
export function skyBrightnessAt(
  levels: ZenithLevels,
  altitudeDegrees: number,
  moonAltitudeDegrees: number,
  moonSeparationDegrees: number,
  twilightShape = 1,
): number {
  const site = fluxOf(levels.siteSqm) * siteGlowFactor(altitudeDegrees, levels.brighteningPerAirmass);
  const moon =
    moonAltitudeDegrees > 0
      ? fluxOf(levels.moonlightSqm) *
        moonlightRatio(
          moonSeparationDegrees,
          90 - moonAltitudeDegrees,
          90 - Math.max(altitudeDegrees, 0),
          levels.extinction,
        )
      : 0;
  const twilight = fluxOf(levels.twilightSqm) * twilightShape;
  return sqmOf(site + moon + twilight);
}

/** Extinction of a star at an apparent altitude, magnitudes (k·X, X capped as in the feasibility engine). */
export function extinctionAt(altitudeDegrees: number, extinction: number): number {
  return extinction * Math.min(kastenYoung(altitudeDegrees), MAX_EXTINCTION_AIRMASS);
}
