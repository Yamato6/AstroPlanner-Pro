// How washed-out the sky looks (spec §4.2 "sky brightness rendering"). Core samples the zenith
// brightness and limiting magnitude (SkyAppearanceCalculator); this module only interpolates them
// and turns them into presentation: background colors, star and deep-sky limits, Milky Way opacity.
import type { SkyAppearance } from '../bridge/schema';

export interface AppearanceTrack {
  readonly startMs: number;
  readonly stepSeconds: number;
  readonly zenithSqm: readonly number[];
  readonly limitingMagnitude: readonly number[];
}

export interface AppearanceNow {
  /** Zenith brightness, mag/arcsec² (larger = darker). */
  readonly zenithSqm: number;
  /** Naked-eye limiting magnitude. */
  readonly limitingMagnitude: number;
}

/** A pristine sky: what is drawn without samples or with the layer off. */
export const DARK_SKY: AppearanceNow = { zenithSqm: 22, limitingMagnitude: 6.62 };

export function toTrack(payload: SkyAppearance): AppearanceTrack {
  return {
    startMs: Date.parse(payload.startUtc),
    stepSeconds: payload.stepSeconds,
    zenithSqm: payload.zenithSqm,
    limitingMagnitude: payload.limitingMagnitude,
  };
}

/** Linear interpolation of the samples at `utcMs` (clamped to the ends). */
export function appearanceAt(track: AppearanceTrack | null, utcMs: number): AppearanceNow {
  if (!track || track.zenithSqm.length === 0) {
    return DARK_SKY;
  }
  const count = track.zenithSqm.length;
  const position = Math.min(Math.max((utcMs - track.startMs) / (track.stepSeconds * 1000), 0), count - 1);
  const i = Math.min(Math.floor(position), Math.max(0, count - 2));
  const t = count < 2 ? 0 : position - i;
  const lerp = (values: readonly number[]): number =>
    (values[i] ?? 0) * (1 - t) + (values[Math.min(i + 1, count - 1)] ?? 0) * t;
  return { zenithSqm: lerp(track.zenithSqm), limitingMagnitude: lerp(track.limitingMagnitude) };
}

type Rgb = readonly [number, number, number];

/** Background color against zenith brightness: night navy → moonlit/twilight blue → day blue. */
const COLOR_STOPS: readonly (readonly [number, Rgb])[] = [
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

/** Toward the horizon the sky is brighter (more air, light domes): drawn ~1.2 mag brighter. */
export function horizonColor(zenithSqm: number): Rgb {
  return skyColor(zenithSqm - 1.2);
}

/**
 * Faintest star drawn: the renderer is a planning view, a little deeper than the naked eye
 * (+2 mag: dark site ≈ 8.6, the default 8.5 limit), but a bright sky still hides the faint ones.
 */
export function starCap(now: AppearanceNow): number {
  return now.limitingMagnitude + 2;
}

/** How much fainter-than-dark the deep-sky limit gets (magnitudes lost to sky brightness). */
export function deepSkyLoss(now: AppearanceNow): number {
  return Math.max(0, DARK_SKY.limitingMagnitude - now.limitingMagnitude);
}

/** Milky Way outline opacity factor: full at SQM ≥ 21, gone at ≤ 19. */
export function milkyWayFactor(now: AppearanceNow): number {
  return Math.min(1, Math.max(0, (now.zenithSqm - 19) / 2));
}
