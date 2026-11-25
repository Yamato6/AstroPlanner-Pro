// How bright the sky is (spec §8.2). Core samples the feasibility model at the zenith
// (SkyAppearanceCalculator: the total, the limiting magnitude, and the twilight and moonlight
// parts); this module interpolates the samples and turns brightness into what is drawn: the
// display curve, star and deep-sky limits, Milky Way opacity. The spread over the sky is
// photometry.ts; the colour of twilight and daylight is atmosphere.ts.
import { NO_LIGHT_SQM, type SkyAppearance } from '../bridge/schema';
import { fluxOf, nakedEyeLimit, sqmOf, type ZenithLevels } from './photometry';

export interface AppearanceTrack {
  readonly startMs: number;
  readonly stepSeconds: number;
  readonly zenithSqm: readonly number[];
  readonly limitingMagnitude: readonly number[];
  readonly twilightSqm: readonly number[];
  readonly moonlightSqm: readonly number[];
  readonly siteSqm: number;
  readonly extinction: number;
  readonly brighteningPerAirmass: number;
}

/** The sky at an instant: the zenith total and its parts. */
export interface AppearanceNow extends ZenithLevels {
  /** Zenith brightness, mag/arcsec² (larger = darker). */
  readonly zenithSqm: number;
  /** Naked-eye limiting magnitude at the zenith. */
  readonly limitingMagnitude: number;
}

/** Feasibility defaults (FeasibilityConstants): V extinction and site-glow brightening. */
export const DEFAULT_EXTINCTION = 0.25;
export const DEFAULT_BRIGHTENING = 0.5;

/** A pristine night sky: what is drawn without samples or with the layer off. */
export const DARK_SKY: AppearanceNow = {
  zenithSqm: 22,
  limitingMagnitude: nakedEyeLimit(22),
  siteSqm: 22,
  twilightSqm: NO_LIGHT_SQM,
  moonlightSqm: NO_LIGHT_SQM,
  extinction: DEFAULT_EXTINCTION,
  brighteningPerAirmass: DEFAULT_BRIGHTENING,
};

export function toTrack(payload: SkyAppearance): AppearanceTrack {
  return {
    startMs: Date.parse(payload.startUtc),
    stepSeconds: payload.stepSeconds,
    zenithSqm: payload.zenithSqm,
    limitingMagnitude: payload.limitingMagnitude,
    twilightSqm: payload.twilightSqm,
    moonlightSqm: payload.moonlightSqm,
    siteSqm: payload.siteSqm,
    extinction: payload.extinction,
    brighteningPerAirmass: payload.brighteningPerAirmass,
  };
}

/** Interpolation of the samples at `utcMs` (clamped to the ends); the parts in linear flux. */
export function appearanceAt(track: AppearanceTrack | null, utcMs: number): AppearanceNow {
  if (!track || track.zenithSqm.length === 0) {
    return DARK_SKY;
  }
  const count = track.zenithSqm.length;
  const position = Math.min(Math.max((utcMs - track.startMs) / (track.stepSeconds * 1000), 0), count - 1);
  const i = Math.min(Math.floor(position), Math.max(0, count - 2));
  const t = count < 2 ? 0 : position - i;
  const next = Math.min(i + 1, count - 1);
  const lerp = (values: readonly number[]): number => (values[i] ?? 0) * (1 - t) + (values[next] ?? 0) * t;
  const lerpPart = (values: readonly number[]): number =>
    sqmOf(fluxOf(values[i] ?? NO_LIGHT_SQM) * (1 - t) + fluxOf(values[next] ?? NO_LIGHT_SQM) * t);
  return {
    zenithSqm: lerp(track.zenithSqm),
    limitingMagnitude: lerp(track.limitingMagnitude),
    twilightSqm: lerpPart(track.twilightSqm),
    moonlightSqm: lerpPart(track.moonlightSqm),
    siteSqm: track.siteSqm,
    extinction: track.extinction,
    brighteningPerAirmass: track.brighteningPerAirmass,
  };
}

type Rgb = readonly [number, number, number];

/**
 * Display colour against brightness (sRGB, as the shaders output it): night navy → moonlit or
 * twilight blue → day blue. Its luminance is the display curve of the whole sky (shaders.ts
 * `displayLevel`); the hue there comes from what lights the sky.
 */
export const COLOR_STOPS: readonly (readonly [number, Rgb])[] = [
  [22.0, [0.02, 0.043, 0.086]],
  [20.5, [0.035, 0.07, 0.13]],
  [19.0, [0.07, 0.12, 0.2]],
  [17.0, [0.12, 0.2, 0.32]],
  [14.0, [0.2, 0.32, 0.48]],
  [10.0, [0.3, 0.45, 0.64]],
  [7.0, [0.42, 0.58, 0.77]],
  [4.0, [0.55, 0.71, 0.9]],
];

export function skyColor(zenithSqm: number): Rgb {
  const first = COLOR_STOPS[0];
  const last = COLOR_STOPS[COLOR_STOPS.length - 1];
  if (!first || !last) {
    return [0, 0, 0];
  }
  if (zenithSqm >= first[0]) {
    return first[1];
  }
  for (let k = 1; k < COLOR_STOPS.length; k++) {
    const upper = COLOR_STOPS[k - 1];
    const lower = COLOR_STOPS[k];
    if (upper && lower && zenithSqm >= lower[0]) {
      const t = (upper[0] - zenithSqm) / (upper[0] - lower[0]);
      return [
        upper[1][0] + (lower[1][0] - upper[1][0]) * t,
        upper[1][1] + (lower[1][1] - upper[1][1]) * t,
        upper[1][2] + (lower[1][2] - upper[1][2]) * t,
      ];
    }
  }
  return last[1];
}

/** Rec. 709 luminance of an sRGB triple (as the shaders weigh it). */
export function displayLuminance(rgb: Rgb): number {
  return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
}

/** The display curve: how light a sky of `sqm` is drawn (the luminance of its stop colour). */
export function displayLevel(sqm: number): number {
  return displayLuminance(skyColor(sqm));
}

/**
 * Faintest star drawn for a sky: the renderer is a planning view, a little deeper than the naked
 * eye (+2 mag: dark site ≈ 8.6, the default 8.5 limit), but a bright sky still hides the faint ones.
 */
export function starCap(now: Pick<AppearanceNow, 'zenithSqm' | 'limitingMagnitude'>): number {
  return now.limitingMagnitude + 2;
}

/** How much fainter-than-dark the deep-sky limit gets (magnitudes lost to sky brightness). */
export function deepSkyLoss(now: Pick<AppearanceNow, 'zenithSqm' | 'limitingMagnitude'>): number {
  return Math.max(0, DARK_SKY.limitingMagnitude - now.limitingMagnitude);
}

/** Milky Way outline opacity factor: full at SQM ≥ 21, gone at ≤ 19. */
export function milkyWayFactor(now: Pick<AppearanceNow, 'zenithSqm' | 'limitingMagnitude'>): number {
  return Math.min(1, Math.max(0, (now.zenithSqm - 19) / 2));
}
