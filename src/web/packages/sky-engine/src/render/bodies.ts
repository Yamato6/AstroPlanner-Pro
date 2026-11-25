// Moving bodies (Sun, Moon, planets, comets, asteroids, satellites) and catalog positions: where
// they are at an instant, from the tracks and buffers C# sends (no astronomy here).
import type { MovingBody } from '../bridge/schema';
import { Dso, Star } from '../data/buffers';
import type { SkyData } from '../data/loader';
import { interpolateTrack, type Vec3 } from '../math/frame';

export interface BodyTrack {
  readonly body: MovingBody;
  readonly startMs: number;
}

export function toBodyTrack(body: MovingBody): BodyTrack {
  return { body, startMs: Date.parse(body.track.startUtc) };
}

/** J2000 direction of a body at the instant (interpolated along its track). */
export function bodyPosition(track: BodyTrack, utcMs: number): Vec3 {
  return interpolateTrack(track.body.track.vectors, track.startMs, track.body.track.stepSeconds, utcMs);
}

/** Whether a satellite's short track covers the instant (outside it the satellite is not drawn). */
export function inTrack(track: BodyTrack, utcMs: number): boolean {
  const count = Math.floor(track.body.track.vectors.length / 3);
  const span = (count - 1) * track.body.track.stepSeconds * 1000;
  return utcMs >= track.startMs && utcMs <= track.startMs + span;
}

/** Whether the Sun lights a satellite at the instant (nearest track sample). */
export function isSunlit(track: BodyTrack, utcMs: number): boolean {
  const index = Math.round((utcMs - track.startMs) / (track.body.track.stepSeconds * 1000));
  return (track.body.sunlit?.[index] ?? 1) === 1;
}

/** J2000 direction of catalog star `i`. */
export function starVector(data: SkyData, i: number): Vec3 {
  const base = i * Star.stride;
  const f = data.stars.float32;
  return [f[base + Star.x] ?? 0, f[base + Star.y] ?? 0, f[base + Star.z] ?? 0];
}

/** J2000 direction of catalog deep-sky object `i`. */
export function dsoVector(data: SkyData, i: number): Vec3 {
  const base = i * Dso.stride;
  const f = data.dsos.float32;
  return [f[base + Dso.x] ?? 0, f[base + Dso.y] ?? 0, f[base + Dso.z] ?? 0];
}
