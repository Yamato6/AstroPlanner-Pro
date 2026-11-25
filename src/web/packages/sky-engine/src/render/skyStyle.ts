// The sky view's look and limits: colours of the light (pollution, Moon, day ground), the display
// curve as GLSL, star size and the field-of-view limits of each projection.
import * as THREE from 'three';
import { COLOR_STOPS, displayLuminance } from './appearance';

export type Rgb = readonly [number, number, number];

/** Dome field of view, degrees: M57 (1.4′) fills a good part of the narrowest; 180° is a hemisphere. */
export const MIN_FOV = 0.25;
export const MAX_FOV = 180;
export const MIN_FINDER_FOV = 0.5;
export const MAX_FINDER_FOV = 60;

/** Light-pollution glow colour (spec §8.2): high-pressure sodium, white LED or neutral. */
export type LightPollutionTint = 'sodium' | 'led' | 'neutral';

export const TINTS: Readonly<Record<LightPollutionTint, Rgb>> = {
  sodium: [1.0, 0.66, 0.36],
  led: [0.86, 0.88, 0.95],
  neutral: [0.8, 0.8, 0.8],
};
/** Moonlight looks blue-grey to dark-adapted eyes (desaturated). */
export const MOON_CHROMA: Rgb = [0.66, 0.74, 0.88];
/** The ground lit by day (sRGB). */
export const DAY_GROUND: Rgb = [0.2, 0.22, 0.18];
export const DAY_HORIZON: Rgb = [0.42, 0.46, 0.44];

/** A colour scaled to luminance 1 (the sky pass multiplies it by the display level). */
export function unitChroma(rgb: Rgb): THREE.Vector3 {
  const l = displayLuminance(rgb) || 1;
  return new THREE.Vector3(rgb[0] / l, rgb[1] / l, rgb[2] / l);
}

/** The display curve as GLSL (piecewise linear over COLOR_STOPS' luminance). */
export function displayLevelGlsl(): string {
  const stops = COLOR_STOPS.map(([sqm, rgb]) => [sqm, displayLuminance(rgb)] as const);
  const first = stops[0] ?? [22, 0];
  const last = stops[stops.length - 1] ?? [4, 1];
  const lines = [`  if (sqm >= ${first[0].toFixed(3)}) return ${first[1].toFixed(5)};`];
  for (let k = 1; k < stops.length; k++) {
    const [upperSqm, upperY] = stops[k - 1] ?? first;
    const [lowerSqm, lowerY] = stops[k] ?? last;
    lines.push(
      `  if (sqm >= ${lowerSqm.toFixed(3)}) return mix(${upperY.toFixed(5)}, ${lowerY.toFixed(5)}, (${upperSqm.toFixed(3)} - sqm) / ${(upperSqm - lowerSqm).toFixed(3)});`,
    );
  }
  lines.push(`  return ${last[1].toFixed(5)};`);
  return `float displayLevel(float sqm) {\n${lines.join('\n')}\n}`;
}

export function color(rgb: Rgb): THREE.Color {
  return new THREE.Color(rgb[0], rgb[1], rgb[2]);
}

/** Dome star size: bright stars grow a little as the field narrows (×1 at 60°, ×1.5 at 1° and below). */
export function domeStarScale(fovDegrees: number): number {
  return 1 + Math.min(0.5, Math.max(0, 0.28 * Math.log10(60 / fovDegrees)));
}

export function clampFov(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
