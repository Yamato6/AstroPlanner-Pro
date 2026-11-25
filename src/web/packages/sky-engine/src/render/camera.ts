// Dome camera motion (spec §8.1, §9.2): smooth flights to a direction and field, and the inertia
// a drag leaves. Pure functions of time, so the engine drives them from its frame loop and the
// tests from numbers.
import { fromHorizontal, toHorizontal, type Vec3 } from '../math/frame';
import { separationDegrees } from './projection';

/** Where the dome looks: apparent azimuth/altitude of the view center and the field, degrees. */
export interface DomeView {
  readonly azimuth: number;
  readonly altitude: number;
  readonly fov: number;
}

/** First run and "Home": facing south, 30° up, a 70° field (spec §8.1). */
export const HOME_VIEW: DomeView = { azimuth: 180, altitude: 30, fov: 70 };

/** The view center may look this far below the horizon. */
export const MIN_ALTITUDE = -30;
/** …and this close to the zenith (the "up" direction is undefined at 90°). */
export const MAX_ALTITUDE = 89.9;

const MIN_FLIGHT_MS = 300;
const MAX_FLIGHT_MS = 1500;

/**
 * How long a flight takes: longer for a bigger turn or zoom, from 300 ms (a small hop) to 1.5 s
 * (across the sky).
 */
export function flightDuration(angleDegrees: number, fromFov: number, toFov: number): number {
  const zoom = Math.abs(Math.log2(toFov / fromFov));
  const ms = MIN_FLIGHT_MS + angleDegrees * 7 + zoom * 120;
  return Math.min(MAX_FLIGHT_MS, Math.max(MIN_FLIGHT_MS, ms));
}

/** Smooth start and stop. */
export function easeInOut(t: number): number {
  const x = Math.min(1, Math.max(0, t));
  return x < 0.5 ? 4 * x * x * x : 1 - (-2 * x + 2) ** 3 / 2;
}

/** Point at fraction `t` of the great circle from `a` to `b` (unit vectors). */
export function slerp(a: Vec3, b: Vec3, t: number): Vec3 {
  const cosine = Math.min(1, Math.max(-1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2]));
  const angle = Math.acos(cosine);
  if (angle < 1e-9) {
    return b;
  }
  const sine = Math.sin(angle);
  const wa = Math.sin((1 - t) * angle) / sine;
  const wb = Math.sin(t * angle) / sine;
  return [a[0] * wa + b[0] * wb, a[1] * wa + b[1] * wb, a[2] * wa + b[2] * wb];
}

function clampAltitude(altitude: number): number {
  return Math.min(MAX_ALTITUDE, Math.max(MIN_ALTITUDE, altitude));
}

/**
 * A flight from the view at its start to a target that may move while it runs (the sky turns
 * with time): each frame passes the target's current apparent position. The direction follows
 * the great circle, the field changes on a log scale and, for a long turn, opens a little in
 * the middle so that the way is visible.
 */
export class Flight {
  private readonly from: Vec3;
  readonly durationMs: number;
  private readonly hop: number;

  constructor(
    private readonly start: DomeView,
    target: DomeView,
    private readonly startMs: number,
  ) {
    this.from = fromHorizontal(start.altitude, start.azimuth);
    const angle = separationDegrees(this.from, fromHorizontal(target.altitude, target.azimuth));
    this.durationMs = flightDuration(angle, start.fov, target.fov);
    const widest = Math.max(start.fov, target.fov);
    this.hop = angle > 2 * widest ? Math.log(Math.min(90, angle) / widest) * 0.6 : 0;
  }

  /** The view at `nowMs` toward the target's current position; `done` once it has arrived. */
  at(nowMs: number, target: DomeView): { view: DomeView; done: boolean } {
    const t = this.durationMs <= 0 ? 1 : (nowMs - this.startMs) / this.durationMs;
    if (t >= 1) {
      return { view: { ...target, altitude: clampAltitude(target.altitude) }, done: true };
    }
    const e = easeInOut(t);
    const direction = toHorizontal(slerp(this.from, fromHorizontal(target.altitude, target.azimuth), e));
    const logFov =
      Math.log(this.start.fov) * (1 - e) + Math.log(target.fov) * e + this.hop * Math.sin(Math.PI * e);
    return {
      view: {
        azimuth: direction.azimuthDegrees,
        altitude: clampAltitude(direction.altitudeDegrees),
        fov: Math.exp(logFov),
      },
      done: false,
    };
  }
}

/** Speed below which a drag leaves no inertia, CSS px per ms. */
const MIN_FLING_SPEED = 0.25;
/** Inertia halves in about 170 ms. */
const INERTIA_TIME_MS = 250;
/** Samples of the pointer kept to measure the release speed. */
const VELOCITY_WINDOW_MS = 80;

/**
 * The drag speed at release (pointer samples of the last 80 ms) and the decaying pan it leaves.
 * `step` returns the pixels to pan this frame, or null once it has stopped.
 */
export class Inertia {
  private samples: { x: number; y: number; t: number }[] = [];
  private velocity: { x: number; y: number } | null = null;
  private lastMs = 0;

  /** A pointer position while dragging. */
  sample(x: number, y: number, t: number): void {
    this.samples.push({ x, y, t });
    while (this.samples.length > 2 && (this.samples[0]?.t ?? t) < t - VELOCITY_WINDOW_MS) {
      this.samples.shift();
    }
  }

  /** The pointer was released at `t`: starts coasting if it was moving fast enough. */
  release(t: number): boolean {
    const first = this.samples[0];
    const last = this.samples[this.samples.length - 1];
    this.samples = [];
    if (!first || !last || last.t - first.t < 8 || t - last.t > 50) {
      return false;
    }
    const vx = (last.x - first.x) / (last.t - first.t);
    const vy = (last.y - first.y) / (last.t - first.t);
    if (Math.hypot(vx, vy) < MIN_FLING_SPEED) {
      return false;
    }
    this.velocity = { x: vx, y: vy };
    this.lastMs = t;
    return true;
  }

  /** Stops coasting (a new drag, a flight, tracking). */
  stop(): void {
    this.velocity = null;
    this.samples = [];
  }

  get active(): boolean {
    return this.velocity !== null;
  }

  step(nowMs: number): { dx: number; dy: number } | null {
    const v = this.velocity;
    if (!v) {
      return null;
    }
    const dt = Math.min(50, Math.max(0, nowMs - this.lastMs));
    this.lastMs = nowMs;
    const decay = Math.exp(-dt / INERTIA_TIME_MS);
    const next = { x: v.x * decay, y: v.y * decay };
    if (Math.hypot(next.x, next.y) < 0.02) {
      this.velocity = null;
      return null;
    }
    this.velocity = next;
    return { dx: next.x * dt, dy: next.y * dt };
  }
}
