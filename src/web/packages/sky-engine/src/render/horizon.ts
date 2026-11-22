// The site's horizon profile (trees, buildings) drawn as a ground silhouette. The points come from
// C# (setObserver, Core HorizonMask) sorted by azimuth; between them the altitude is interpolated
// linearly, wrapping around north, exactly like HorizonMask.AltitudeAt.
import { fromHorizontal } from '../math/frame';

export interface HorizonPoint {
  readonly azimuth: number;
  readonly altitude: number;
}

/** Altitude of the local horizon towards `azimuthDegrees` (0 for an empty profile). */
export function maskAltitude(points: readonly HorizonPoint[], azimuthDegrees: number): number {
  const first = points[0];
  const last = points[points.length - 1];
  if (!first || !last) {
    return 0;
  }
  if (points.length === 1) {
    return first.altitude;
  }
  const azimuth = ((azimuthDegrees % 360) + 360) % 360;
  const nextIndex = points.findIndex((p) => p.azimuth >= azimuth);
  let previous: HorizonPoint;
  let next: HorizonPoint;
  let span: number;
  let offset: number;
  if (nextIndex <= 0) {
    previous = last;
    next = first;
    span = next.azimuth + 360 - previous.azimuth;
    offset = azimuth >= previous.azimuth ? azimuth - previous.azimuth : azimuth + 360 - previous.azimuth;
  } else {
    previous = points[nextIndex - 1] ?? last;
    next = points[nextIndex] ?? first;
    span = next.azimuth - previous.azimuth;
    offset = azimuth - previous.azimuth;
  }
  return span <= 0
    ? previous.altitude
    : previous.altitude + (next.altitude - previous.altitude) * (offset / span);
}

/** True when some part of the profile rises above the flat horizon. */
export function hasObstruction(points: readonly HorizonPoint[]): boolean {
  return points.some((p) => p.altitude > 0);
}

/**
 * Triangles (north, east, up vertices) filling the sky between the flat horizon (slightly below,
 * so that no gap shows) and the profile, every `stepDegrees` of azimuth.
 */
export function silhouetteTriangles(points: readonly HorizonPoint[], stepDegrees = 1): Float32Array {
  const out: number[] = [];
  const base = -0.5;
  for (let az = 0; az < 360; az += stepDegrees) {
    const next = az + stepDegrees;
    const h0 = maskAltitude(points, az);
    const h1 = maskAltitude(points, next);
    if (h0 <= 0 && h1 <= 0) {
      continue;
    }
    const a = fromHorizontal(base, az);
    const b = fromHorizontal(base, next);
    const c = fromHorizontal(Math.max(base, h1), next);
    const d = fromHorizontal(Math.max(base, h0), az);
    out.push(...a, ...b, ...c, ...a, ...c, ...d);
  }
  return new Float32Array(out);
}
