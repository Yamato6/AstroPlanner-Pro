// Applies the frame transform computed by C# (Core SkyFrame). The renderer never computes
// astronomy: it only composes the matrices it receives and advances the sidereal time linearly
// at the rate C# sends (spec §5.3). The Vitest cross-check (test/projection.test.ts) proves this
// reproduces Core's GetHorizontal within 0.01°.

/** Row-major 3×3 matrix. */
export type Mat3 = readonly [number, number, number, number, number, number, number, number, number];
export type Vec3 = readonly [number, number, number];

export interface FrameTransform {
  /** Epoch of the frame, milliseconds since the Unix epoch (UTC). */
  readonly epochMs: number;
  readonly precession: Mat3;
  readonly latitudeDegrees: number;
  readonly lstDegrees: number;
  readonly lstRateDegreesPerSecond: number;
}

export interface RefractionModel {
  readonly a: number;
  readonly b: number;
  readonly c: number;
  readonly d: number;
  readonly minimumAltitudeDegrees: number;
}

export interface Horizontal {
  readonly altitudeDegrees: number;
  readonly azimuthDegrees: number;
}

const DEG = Math.PI / 180;

export function multiply(a: Mat3, b: Mat3): Mat3 {
  const [a11, a12, a13, a21, a22, a23, a31, a32, a33] = a;
  const [b11, b12, b13, b21, b22, b23, b31, b32, b33] = b;
  return [
    a11 * b11 + a12 * b21 + a13 * b31,
    a11 * b12 + a12 * b22 + a13 * b32,
    a11 * b13 + a12 * b23 + a13 * b33,
    a21 * b11 + a22 * b21 + a23 * b31,
    a21 * b12 + a22 * b22 + a23 * b32,
    a21 * b13 + a22 * b23 + a23 * b33,
    a31 * b11 + a32 * b21 + a33 * b31,
    a31 * b12 + a32 * b22 + a33 * b32,
    a31 * b13 + a32 * b23 + a33 * b33,
  ];
}

export function transform(m: Mat3, v: Vec3): Vec3 {
  return [
    m[0] * v[0] + m[1] * v[1] + m[2] * v[2],
    m[3] * v[0] + m[4] * v[1] + m[5] * v[2],
    m[6] * v[0] + m[7] * v[1] + m[8] * v[2],
  ];
}

export function transpose(m: Mat3): Mat3 {
  return [m[0], m[3], m[6], m[1], m[4], m[7], m[2], m[5], m[8]];
}

/** Active rotation about z (same convention as Core Matrix3.RotationZ). */
export function rotationZ(degrees: number): Mat3 {
  const s = Math.sin(degrees * DEG);
  const c = Math.cos(degrees * DEG);
  return [c, -s, 0, s, c, 0, 0, 0, 1];
}

/** Hour-angle coordinates → (north, east, up); Core SkyFrame.HorizonMatrix. */
export function horizonMatrix(latitudeDegrees: number): Mat3 {
  const s = Math.sin(latitudeDegrees * DEG);
  const c = Math.cos(latitudeDegrees * DEG);
  return [-s, 0, c, 0, 1, 0, c, 0, s];
}

/** Local sidereal time at `utcMs`, advanced linearly from the frame epoch. */
export function lstAt(frame: FrameTransform, utcMs: number): number {
  const lst = frame.lstDegrees + (frame.lstRateDegreesPerSecond * (utcMs - frame.epochMs)) / 1000;
  return ((lst % 360) + 360) % 360;
}

/** J2000 → (north, east, up) matrix at `utcMs`: H = L(φ)·Rz(−LST)·P. */
export function j2000ToHorizontal(frame: FrameTransform, utcMs: number): Mat3 {
  return multiply(
    multiply(horizonMatrix(frame.latitudeDegrees), rotationZ(-lstAt(frame, utcMs))),
    frame.precession,
  );
}

/** Saemundsson refraction: apparent altitude from the true one (Core Refraction.ApparentAltitude). */
export function refract(trueAltitudeDegrees: number, model: RefractionModel): number {
  if (!(trueAltitudeDegrees >= model.minimumAltitudeDegrees)) {
    return trueAltitudeDegrees;
  }
  const argument = (trueAltitudeDegrees + model.b / (trueAltitudeDegrees + model.c)) * DEG;
  return trueAltitudeDegrees + Math.max(0, (model.a / Math.tan(argument) + model.d) / 60);
}

/** (north, east, up) vector → geometric altitude/azimuth (azimuth from north through east). */
export function toHorizontal(neu: Vec3): Horizontal {
  const length = Math.hypot(neu[0], neu[1], neu[2]);
  const z = Math.min(1, Math.max(-1, neu[2] / length));
  const azimuth = Math.abs(neu[0]) < 1e-12 && Math.abs(neu[1]) < 1e-12 ? 0 : Math.atan2(neu[1], neu[0]) / DEG;
  return { altitudeDegrees: Math.asin(z) / DEG, azimuthDegrees: ((azimuth % 360) + 360) % 360 };
}

export function fromHorizontal(altitudeDegrees: number, azimuthDegrees: number): Vec3 {
  const alt = altitudeDegrees * DEG;
  const az = azimuthDegrees * DEG;
  return [Math.cos(alt) * Math.cos(az), Math.cos(alt) * Math.sin(az), Math.sin(alt)];
}

/** Apparent (refracted) horizontal position of a J2000 unit vector. */
export function apparentHorizontal(h: Mat3, j2000: Vec3, refraction: RefractionModel): Horizontal {
  const geometric = toHorizontal(transform(h, j2000));
  return {
    altitudeDegrees: refract(geometric.altitudeDegrees, refraction),
    azimuthDegrees: geometric.azimuthDegrees,
  };
}

/**
 * (north, east, up) → three.js world coordinates: x = east, y = up, z = −north, so that a camera
 * at the origin looking along −z faces north.
 */
export function neuToWorld(neu: Vec3): Vec3 {
  return [neu[1], neu[2], -neu[0]];
}

export function worldToNeu(world: Vec3): Vec3 {
  return [-world[2], world[0], world[1]];
}

/** Linear interpolation of a sampled unit-vector track, normalized (clamped to the ends). */
export function interpolateTrack(
  vectors: readonly number[] | Float64Array,
  startMs: number,
  stepSeconds: number,
  utcMs: number,
): Vec3 {
  const count = Math.floor(vectors.length / 3);
  const position = Math.min(Math.max((utcMs - startMs) / (stepSeconds * 1000), 0), count - 1);
  const i = Math.min(Math.floor(position), count - 2 < 0 ? 0 : count - 2);
  const t = count < 2 ? 0 : position - i;
  const at = (k: number): number => vectors[k] ?? 0;
  const x = at(3 * i) * (1 - t) + at(3 * (i + 1)) * t;
  const y = at(3 * i + 1) * (1 - t) + at(3 * (i + 1) + 1) * t;
  const z = at(3 * i + 2) * (1 - t) + at(3 * (i + 1) + 2) * t;
  const length = Math.hypot(x, y, z) || 1;
  return [x / length, y / length, z / length];
}

/** Smallest difference between two azimuths, degrees. */
export function azimuthDifference(a: number, b: number): number {
  const d = Math.abs(a - b) % 360;
  return d > 180 ? 360 - d : d;
}
