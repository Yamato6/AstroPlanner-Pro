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

/** Vertex pairs of the segment tables (stride 7 floats: x1 y1 z1 x2 y2 z2 group). */
export function segmentVertices(table: { count: number; float32: Float32Array; uint32: Uint32Array }): {
  positions: Float32Array;
  groups: Float32Array;
} {
  const positions = new Float32Array(table.count * 6);
  const groups = new Float32Array(table.count * 2);
  for (let i = 0; i < table.count; i++) {
    for (let k = 0; k < 6; k++) {
      positions[i * 6 + k] = table.float32[i * 7 + k] ?? 0;
    }
    const group = table.uint32[i * 7 + 6] ?? 0;
    groups[i * 2] = group;
    groups[i * 2 + 1] = group;
  }
  return { positions, groups };
}
