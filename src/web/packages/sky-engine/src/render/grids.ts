// Reference lines as vertex pairs. Alt-az lines are in (north, east, up) and drawn with an
// identity frame; equatorial lines are built in the equator of date and rotated back to J2000
// with the transpose of the precession matrix that C# sent; the ecliptic is the J2000 ecliptic.
import { fromHorizontal, transform, transpose, type Mat3, type Vec3 } from '../math/frame';

const DEG = Math.PI / 180;
/** Mean obliquity of the ecliptic at J2000 (IAU 1976), degrees: defines the J2000 ecliptic plane. */
const OBLIQUITY_J2000 = 23.4392911;

function circle(points: (t: number) => Vec3, steps: number, out: number[]): void {
  let previous = points(0);
  for (let i = 1; i <= steps; i++) {
    const next = points(i / steps);
    out.push(...previous, ...next);
    previous = next;
  }
}

/** Altitude circles every 10° and azimuth lines every 15°, in (north, east, up). */
export function altAzGrid(): Float32Array {
  const out: number[] = [];
  for (let alt = 0; alt < 90; alt += 10) {
    circle((t) => fromHorizontal(alt, t * 360), 180, out);
  }
  for (let az = 0; az < 360; az += 15) {
    circle((t) => fromHorizontal(t * (az % 90 === 0 ? 90 : 80), az), 45, out);
  }
  return new Float32Array(out);
}

/** The local meridian (north – zenith – south) in (north, east, up). */
export function meridian(): Float32Array {
  const out: number[] = [];
  circle((t) => [Math.cos(t * Math.PI), 0, Math.sin(t * Math.PI)], 90, out);
  return new Float32Array(out);
}

function equatorial(raDegrees: number, decDegrees: number): Vec3 {
  const ra = raDegrees * DEG;
  const dec = decDegrees * DEG;
  return [Math.cos(dec) * Math.cos(ra), Math.cos(dec) * Math.sin(ra), Math.sin(dec)];
}

/** Declination circles every 10° and hour circles every 1 h, equator of date → J2000. */
export function equatorialGrid(precession: Mat3): Float32Array {
  const back = transpose(precession);
  const out: number[] = [];
  const add = (v: Vec3): Vec3 => transform(back, v);
  for (let dec = -80; dec <= 80; dec += 10) {
    circle((t) => add(equatorial(t * 360, dec)), 180, out);
  }
  for (let hour = 0; hour < 24; hour++) {
    circle((t) => add(equatorial(hour * 15, -90 + t * 180)), 90, out);
  }
  return new Float32Array(out);
}

/** The ecliptic of J2000 (its motion over decades is far below what is drawn). */
export function ecliptic(): Float32Array {
  const e = OBLIQUITY_J2000 * DEG;
  const out: number[] = [];
  circle(
    (t) => {
      const l = t * 2 * Math.PI;
      return [Math.cos(l), Math.sin(l) * Math.cos(e), Math.sin(l) * Math.sin(e)];
    },
    360,
    out,
  );
  return new Float32Array(out);
}

/** Longest piece a segment table line is drawn with, degrees (the wide dome projection curves). */
export const MAX_SEGMENT_DEGREES = 2;

/**
 * Vertex pairs of the segment tables (stride 7 floats: x1 y1 z1 x2 y2 z2 group). Lines longer
 * than `maxDegrees` are split along their great circle: the dome's stereographic projection
 * curves them, and a piece whose end is culled is dropped whole.
 */
export function segmentVertices(
  table: { count: number; float32: Float32Array; uint32: Uint32Array },
  maxDegrees = MAX_SEGMENT_DEGREES,
): {
  positions: Float32Array;
  groups: Float32Array;
} {
  const positions: number[] = [];
  const groups: number[] = [];
  for (let i = 0; i < table.count; i++) {
    const a: Vec3 = [table.float32[i * 7] ?? 0, table.float32[i * 7 + 1] ?? 0, table.float32[i * 7 + 2] ?? 0];
    const b: Vec3 = [
      table.float32[i * 7 + 3] ?? 0,
      table.float32[i * 7 + 4] ?? 0,
      table.float32[i * 7 + 5] ?? 0,
    ];
    const group = table.uint32[i * 7 + 6] ?? 0;
    const cosine = Math.min(1, Math.max(-1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]));
    const angle = Math.acos(cosine);
    const pieces = Math.max(1, Math.ceil(angle / DEG / maxDegrees));
    let previous = a;
    for (let k = 1; k <= pieces; k++) {
      const next = k === pieces ? b : slerp(a, b, angle, k / pieces);
      positions.push(...previous, ...next);
      groups.push(group, group);
      previous = next;
    }
  }
  return { positions: new Float32Array(positions), groups: new Float32Array(groups) };
}

/** Point at fraction `t` of the great-circle arc from `a` to `b` (`angle` apart, radians). */
function slerp(a: Vec3, b: Vec3, angle: number, t: number): Vec3 {
  const sine = Math.sin(angle);
  if (sine < 1e-9) {
    return a;
  }
  const wa = Math.sin((1 - t) * angle) / sine;
  const wb = Math.sin(t * angle) / sine;
  return [a[0] * wa + b[0] * wb, a[1] * wa + b[1] * wb, a[2] * wa + b[2] * wb];
}
