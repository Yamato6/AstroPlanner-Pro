// Screen geometry of the 2D charts (spec §4.2) and of the camera field-of-view overlay. Pure
// functions, mirrored by the vertex shader (shaders.ts, skyClip) and tested in projection2d.test.ts.
//
// - All-sky: zenithal azimuthal equidistant, north up, east left (the v1 convention): the
//   horizon is a circle of radius R, the zenith its center.
// - Finder: gnomonic projection centred on a J2000 direction, celestial north up, east left.
import { transform, transpose, type Horizontal, type Mat3, type Vec3 } from '../math/frame';

const DEG = Math.PI / 180;

export type ViewMode = 'dome' | 'allSky' | 'finder';

export interface ScreenPoint {
  readonly x: number;
  readonly y: number;
}

/** Center and scale of a 2D chart: all-sky = horizon radius (px); finder = px per unit of tan(angle). */
export interface ChartGeometry {
  readonly cx: number;
  readonly cy: number;
  readonly scale: number;
}

/** Room left around the all-sky circle for the cardinal points. */
const ALL_SKY_MARGIN = 30;

export function allSkyGeometry(width: number, height: number): ChartGeometry {
  return { cx: width / 2, cy: height / 2, scale: Math.max(10, Math.min(width, height) / 2 - ALL_SKY_MARGIN) };
}

export function finderGeometry(width: number, height: number, fovDegrees: number): ChartGeometry {
  return { cx: width / 2, cy: height / 2, scale: height / 2 / Math.tan((fovDegrees / 2) * DEG) };
}

/** All-sky position of an apparent altitude/azimuth: r = (90° − alt) / 90° · R, azimuth 0 up, 90° left. */
export function allSkyPoint(altitudeDegrees: number, azimuthDegrees: number, g: ChartGeometry): ScreenPoint {
  const r = ((90 - altitudeDegrees) / 90) * g.scale;
  const az = azimuthDegrees * DEG;
  return { x: g.cx - r * Math.sin(az), y: g.cy - r * Math.cos(az) };
}

/** Inverse of {@link allSkyPoint} (altitudes below 0 outside the circle). */
export function allSkyInverse(x: number, y: number, g: ChartGeometry): Horizontal {
  const dx = x - g.cx;
  const dy = y - g.cy;
  const r = Math.hypot(dx, dy) / g.scale;
  const azimuth = r < 1e-12 ? 0 : Math.atan2(-dx, -dy) / DEG;
  return { altitudeDegrees: 90 * (1 - r), azimuthDegrees: ((azimuth % 360) + 360) % 360 };
}

function normalize(v: Vec3): Vec3 {
  const length = Math.hypot(v[0], v[1], v[2]) || 1;
  return [v[0] / length, v[1] / length, v[2] / length];
}

function cross(a: Vec3, b: Vec3): Vec3 {
  return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
}

function dot(a: Vec3, b: Vec3): number {
  return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

/**
 * Local frame at a J2000 direction: rows are east, north and the direction itself (a rotation
 * matrix, so its transpose is its inverse). At the poles "east" falls back to +y.
 */
export function tangentBasis(center: Vec3): Mat3 {
  const c = normalize(center);
  let east = cross([0, 0, 1], c);
  if (Math.hypot(east[0], east[1], east[2]) < 1e-9) {
    east = [0, 1, 0];
  }
  east = normalize(east);
  const north = cross(c, east);
  return [east[0], east[1], east[2], north[0], north[1], north[2], c[0], c[1], c[2]];
}

/** Gnomonic (finder) position of a J2000 direction, or null when it is 90° or more from the center. */
export function gnomonic(basis: Mat3, v: Vec3, g: ChartGeometry): ScreenPoint | null {
  const p = transform(basis, v);
  if (p[2] <= 1e-6) {
    return null;
  }
  return { x: g.cx - (p[0] / p[2]) * g.scale, y: g.cy - (p[1] / p[2]) * g.scale };
}

/** J2000 direction under a finder screen point. */
export function gnomonicInverse(basis: Mat3, x: number, y: number, g: ChartGeometry): Vec3 {
  return normalize(transform(transpose(basis), [-(x - g.cx) / g.scale, -(y - g.cy) / g.scale, 1]));
}

/** Rotates `v` by the rotation that takes the unit vector `from` onto `to` (Rodrigues). */
export function rotateTowards(v: Vec3, from: Vec3, to: Vec3): Vec3 {
  const axis = cross(from, to);
  const s = Math.hypot(axis[0], axis[1], axis[2]);
  const c = dot(from, to);
  if (s < 1e-12) {
    return v;
  }
  const k: Vec3 = [axis[0] / s, axis[1] / s, axis[2] / s];
  const kxv = cross(k, v);
  const kv = dot(k, v);
  return normalize([
    v[0] * c + kxv[0] * s + k[0] * kv * (1 - c),
    v[1] * c + kxv[1] * s + k[1] * kv * (1 - c),
    v[2] * c + kxv[2] * s + k[2] * kv * (1 - c),
  ]);
}

/** Angle between two unit vectors, degrees. */
export function separationDegrees(a: Vec3, b: Vec3): number {
  return Math.atan2(Math.hypot(...cross(a, b)), dot(a, b)) / DEG;
}

/**
 * The sensor rectangle on the sky as a closed outline of J2000 directions: a flat rectangle of
 * tan(w/2) × tan(h/2) in the tangent plane (what a camera images), its "up" side at position
 * angle `rotationDegrees` from north through east. `steps` points per side, so that the edges
 * curve correctly in the dome and all-sky projections.
 */
export function fovOutline(
  center: Vec3,
  widthDegrees: number,
  heightDegrees: number,
  rotationDegrees: number,
  steps = 12,
): Vec3[] {
  const basis = tangentBasis(center);
  const east: Vec3 = [basis[0], basis[1], basis[2]];
  const north: Vec3 = [basis[3], basis[4], basis[5]];
  const c: Vec3 = [basis[6], basis[7], basis[8]];
  const theta = rotationDegrees * DEG;
  const up: Vec3 = [
    Math.cos(theta) * north[0] + Math.sin(theta) * east[0],
    Math.cos(theta) * north[1] + Math.sin(theta) * east[1],
    Math.cos(theta) * north[2] + Math.sin(theta) * east[2],
  ];
  const side: Vec3 = [
    -Math.sin(theta) * north[0] + Math.cos(theta) * east[0],
    -Math.sin(theta) * north[1] + Math.cos(theta) * east[1],
    -Math.sin(theta) * north[2] + Math.cos(theta) * east[2],
  ];
  const a = Math.tan((widthDegrees / 2) * DEG);
  const b = Math.tan((heightDegrees / 2) * DEG);
  const at = (s: number, t: number): Vec3 =>
    normalize([
      c[0] + s * a * side[0] + t * b * up[0],
      c[1] + s * a * side[1] + t * b * up[1],
      c[2] + s * a * side[2] + t * b * up[2],
    ]);
  const corners: [number, number][] = [
    [-1, 1],
    [1, 1],
    [1, -1],
    [-1, -1],
  ];
  const outline: Vec3[] = [];
  for (let k = 0; k < 4; k++) {
    const [s0, t0] = corners[k] ?? [0, 0];
    const [s1, t1] = corners[(k + 1) % 4] ?? [0, 0];
    for (let i = 0; i < steps; i++) {
      const f = i / steps;
      outline.push(at(s0 + (s1 - s0) * f, t0 + (t1 - t0) * f));
    }
  }
  outline.push(outline[0] ?? c);
  return outline;
}

/** Direction just beyond the middle of the "up" side (orientation tick of the overlay). */
export function fovUpMarker(center: Vec3, heightDegrees: number, rotationDegrees: number): Vec3 {
  const basis = tangentBasis(center);
  const theta = rotationDegrees * DEG;
  const t = Math.tan((heightDegrees / 2) * DEG) * 1.12;
  return normalize([
    basis[6] + t * (Math.cos(theta) * basis[3] + Math.sin(theta) * basis[0]),
    basis[7] + t * (Math.cos(theta) * basis[4] + Math.sin(theta) * basis[1]),
    basis[8] + t * (Math.cos(theta) * basis[5] + Math.sin(theta) * basis[2]),
  ]);
}

/** Point-in-polygon (even-odd rule). */
export function insidePolygon(points: readonly ScreenPoint[], x: number, y: number): boolean {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const a = points[i];
    const b = points[j];
    if (a && b && a.y > y !== b.y > y && x < ((b.x - a.x) * (y - a.y)) / (b.y - a.y) + a.x) {
      inside = !inside;
    }
  }
  return inside;
}

/** Right ascension (hours) and declination (degrees) of a J2000 unit vector, for display. */
export function raDec(v: Vec3): { raHours: number; decDegrees: number } {
  const ra = Math.atan2(v[1], v[0]) / DEG / 15;
  return { raHours: ((ra % 24) + 24) % 24, decDegrees: Math.asin(Math.max(-1, Math.min(1, v[2]))) / DEG };
}

// ---- dome ------------------------------------------------------------------------------------
//
// The dome view uses a generalized zenithal projection around the view direction:
// r(θ) = k·tan(θ/k), where θ is the angle from the view center. k = 1 is the gnomonic
// (perspective) projection, exactly what a pinhole camera shows; k = 2 is the stereographic one,
// which stays finite up to 180°. k is 1 up to a 60° field, rises smoothly to 2 at 120° and stays
// there, so narrow fields look as before and the field can open to 180°. Mirrored by the vertex
// shader (shaders.ts, domeClip).

/** Below this field of view (degrees) the dome is a plain perspective (k = 1). */
export const PERSPECTIVE_FOV = 60;
/** From this field of view (degrees) the dome is stereographic (k = 2). */
export const STEREOGRAPHIC_FOV = 120;

/** Projection parameter k for a field of view (degrees): 1 → 2 with a smoothstep between. */
export function domeK(fovDegrees: number): number {
  const t = Math.min(1, Math.max(0, (fovDegrees - PERSPECTIVE_FOV) / (STEREOGRAPHIC_FOV - PERSPECTIVE_FOV)));
  return 1 + t * t * (3 - 2 * t);
}

/**
 * Largest angle from the view center that is drawn (radians): just short of where r(θ) runs to
 * infinity (k·90°), and never the point straight behind the camera.
 */
export function domeMaxAngle(k: number): number {
  return Math.min(k * (Math.PI / 2) - 0.02, Math.PI - 0.02);
}

/** A dome camera: world-space axes (three.js: x = east, y = up, z = −north) and the field. */
export interface DomeCamera {
  /** Rows: right, up and forward (the view direction), unit vectors in world space. */
  readonly basis: Mat3;
  readonly fovDegrees: number;
  readonly width: number;
  readonly height: number;
}

/** Camera looking at an apparent altitude/azimuth, its "up" toward the zenith. */
export function domeCamera(
  altitudeDegrees: number,
  azimuthDegrees: number,
  fovDegrees: number,
  width: number,
  height: number,
): DomeCamera {
  const alt = altitudeDegrees * DEG;
  const az = azimuthDegrees * DEG;
  // World direction of (alt, az): north = −z, east = +x, up = +y.
  const forward: Vec3 = [Math.cos(alt) * Math.sin(az), Math.sin(alt), -Math.cos(alt) * Math.cos(az)];
  let right = cross(forward, [0, 1, 0]);
  if (Math.hypot(right[0], right[1], right[2]) < 1e-9) {
    right = [Math.cos(az), 0, Math.sin(az)];
  }
  right = normalize(right);
  const up = cross(right, forward);
  return {
    basis: [right[0], right[1], right[2], up[0], up[1], up[2], forward[0], forward[1], forward[2]],
    fovDegrees,
    width,
    height,
  };
}

/** Pixels per unit of r(θ): the top of the screen is at θ = fov / 2. */
export function domeScale(camera: DomeCamera): number {
  const k = domeK(camera.fovDegrees);
  return camera.height / 2 / (k * Math.tan(((camera.fovDegrees / 2) * DEG) / k));
}

/** Pixels per degree at the view center (r'(0) = 1 for every k). */
export function domePixelsPerDegree(camera: DomeCamera): number {
  return domeScale(camera) * DEG;
}

/** Screen position (CSS px) of a world direction, or null beyond the drawn angle. */
export function domeProject(camera: DomeCamera, world: Vec3): ScreenPoint | null {
  const d = transform(camera.basis, normalize(world));
  const perpendicular = Math.hypot(d[0], d[1]);
  const theta = Math.atan2(perpendicular, d[2]);
  const k = domeK(camera.fovDegrees);
  if (theta > domeMaxAngle(k)) {
    return null;
  }
  const r = k * Math.tan(theta / k) * domeScale(camera);
  const ux = perpendicular > 1e-12 ? d[0] / perpendicular : 0;
  const uy = perpendicular > 1e-12 ? d[1] / perpendicular : 0;
  return { x: camera.width / 2 + ux * r, y: camera.height / 2 - uy * r };
}

/** World direction under a screen point (the inverse of {@link domeProject}). */
export function domeInverse(camera: DomeCamera, x: number, y: number): Vec3 {
  const k = domeK(camera.fovDegrees);
  const px = (x - camera.width / 2) / domeScale(camera);
  const py = -(y - camera.height / 2) / domeScale(camera);
  const r = Math.hypot(px, py);
  const theta = k * Math.atan(r / k);
  const s = Math.sin(theta);
  const ux = r > 1e-12 ? px / r : 0;
  const uy = r > 1e-12 ? py / r : 0;
  return normalize(transform(transpose(camera.basis), [ux * s, uy * s, Math.cos(theta)]));
}
