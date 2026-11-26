// HEALPix in the NESTED scheme (Górski et al. 2005), as HiPS surveys use it (spec §8.3): the same
// algorithms as Core's Healpix (healpix_base pix2loc with continuous face coordinates), checked
// against it within 1″ (tests/fixtures/bridge/healpix.json). nside = 2^order; directions are
// unit vectors in the survey's frame (ICRS ≈ J2000 for DSS and Pan-STARRS).
import type { Vec3 } from './frame';

/** Highest order the survey layer uses (indices stay below 2^31: 12·4^13 ≈ 8·10^8). */
export const MAX_ORDER = 13;

const FACE_ROW = [2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4] as const;
const FACE_COLUMN = [1, 3, 5, 7, 0, 2, 4, 6, 1, 3, 5, 7] as const;

export function nside(order: number): number {
  return 2 ** order;
}

export function pixelCount(order: number): number {
  return 12 * 4 ** order;
}

/** Interleaves the bits of x (even bits) and y (odd bits) of a face pixel. */
function spread(value: number): number {
  let result = 0;
  for (let bit = 0; bit < 16; bit++) {
    result |= ((value >>> bit) & 1) << (2 * bit);
  }
  return result >>> 0;
}

function compress(value: number): number {
  let result = 0;
  for (let bit = 0; bit < 16; bit++) {
    result |= ((value >>> (2 * bit)) & 1) << bit;
  }
  return result;
}

function checkOrder(order: number): void {
  if (!Number.isInteger(order) || order < 0 || order > MAX_ORDER) {
    throw new RangeError(`HEALPix order must be 0–${MAX_ORDER}`);
  }
}

/** Face and face pixel (x, y) of a NESTED index. */
export function nestedToFaceXy(order: number, nested: number): { face: number; x: number; y: number } {
  checkOrder(order);
  const perFace = 4 ** order;
  const face = Math.floor(nested / perFace);
  const inFace = nested - face * perFace;
  return { face, x: compress(inFace), y: compress(inFace >>> 1) };
}

/** NESTED index of face pixel (x, y). */
export function faceXyToNested(order: number, face: number, x: number, y: number): number {
  checkOrder(order);
  return face * 4 ** order + spread(x) + 2 * spread(y);
}

/**
 * Direction of a continuous face point (x, y in [0, nside]) as longitude/latitude in degrees
 * (longitude in [0, 360)).
 */
export function faceToLonLat(
  order: number,
  face: number,
  x: number,
  y: number,
): { lon: number; lat: number } {
  const { z, phi } = facePointZPhi(order, face, x, y);
  const lon = ((((phi * 180) / Math.PI) % 360) + 360) % 360;
  return { lon, lat: (Math.asin(Math.min(1, Math.max(-1, z))) * 180) / Math.PI };
}

/** Unit vector of a continuous face point (x toward +RA at 0°, z toward the north pole). */
export function faceToVector(order: number, face: number, x: number, y: number): Vec3 {
  const { z, phi } = facePointZPhi(order, face, x, y);
  const s = Math.sqrt(Math.max(0, (1 - z) * (1 + z)));
  return [s * Math.cos(phi), s * Math.sin(phi), z];
}

function facePointZPhi(order: number, face: number, x: number, y: number): { z: number; phi: number } {
  const n = nside(order);
  const row = FACE_ROW[face] ?? 2;
  const column = FACE_COLUMN[face] ?? 0;
  const jr = row * n - x - y; // ring number, continuous
  let z: number;
  let nr: number;
  if (jr < n) {
    nr = jr;
    z = 1 - (nr * nr) / (3 * n * n);
  } else if (jr > 3 * n) {
    nr = 4 * n - jr;
    z = (nr * nr) / (3 * n * n) - 1;
  } else {
    nr = n;
    z = ((2 * n - jr) * 2) / (3 * n);
  }
  const position = column * nr + x - y;
  const phi = nr <= 0 ? 0 : ((Math.PI / 4) * position) / nr;
  return { z, phi };
}

/** A tile's bounding cap: its centre and the angle (degrees) that contains it. */
export interface TileCap {
  readonly center: Vec3;
  readonly radiusDegrees: number;
}

/** Bounding cap of NESTED pixel `nested` at `order` (its corners and edge midpoints, with a margin). */
export function tileCap(order: number, nested: number): TileCap {
  const { face, x, y } = nestedToFaceXy(order, nested);
  const center = faceToVector(order, face, x + 0.5, y + 0.5);
  let cosMin = 1;
  for (const [fx, fy] of EDGE_POINTS) {
    const v = faceToVector(order, face, x + fx, y + fy);
    cosMin = Math.min(cosMin, v[0] * center[0] + v[1] * center[1] + v[2] * center[2]);
  }
  const radius = (Math.acos(Math.min(1, Math.max(-1, cosMin))) * 180) / Math.PI;
  return { center, radiusDegrees: radius * 1.1 + 1e-6 };
}

const EDGE_POINTS: readonly (readonly [number, number])[] = [
  [0, 0],
  [1, 0],
  [0, 1],
  [1, 1],
  [0.5, 0],
  [0.5, 1],
  [0, 0.5],
  [1, 0.5],
];
