// What the sky tells the page it lives in (the v3 shell's HUD and context menu), next to the
// messages it sends the host. One sky per page, so one set of listeners.
import type { Horizontal, Vec3 } from './math/frame';

export type SkyViewMode = 'dome' | 'allSky' | 'finder';

/** A direction on the sky: J2000 equatorial and apparent horizontal coordinates. */
export interface SkyPoint {
  readonly raHours: number;
  readonly decDegrees: number;
  readonly altitudeDegrees: number;
  readonly azimuthDegrees: number;
}

export interface PickedObject {
  readonly kind: string;
  readonly id: string;
  readonly label: string | null;
}

export interface SkyEventMap {
  /** The view changed (zoom, mode); also sent once the sky is built. */
  readonly view: {
    readonly fovDegrees: number;
    readonly mode: SkyViewMode;
    readonly starLimitMagnitude: number;
  };
  /** The point under the pointer, or null when it left the sky. */
  readonly cursor: SkyPoint | null;
  /** A right click: where, on what point of the sky and on which object (now selected), if any. */
  readonly contextMenu: {
    readonly x: number;
    readonly y: number;
    readonly point: SkyPoint | null;
    readonly picked: PickedObject | null;
  };
}

type Listener<K extends keyof SkyEventMap> = (event: SkyEventMap[K]) => void;

const listeners: { [K in keyof SkyEventMap]: Set<Listener<K>> } = {
  view: new Set(),
  cursor: new Set(),
  contextMenu: new Set(),
};

/** The sky's events for the page. `on` returns the function that stops listening. */
export const skyEvents = {
  on<K extends keyof SkyEventMap>(event: K, listener: Listener<K>): () => void {
    listeners[event].add(listener);
    return () => {
      listeners[event].delete(listener);
    };
  },
  emit<K extends keyof SkyEventMap>(event: K, payload: SkyEventMap[K]): void {
    for (const listener of listeners[event]) {
      listener(payload);
    }
  },
};

const DEG = 180 / Math.PI;

/** A J2000 unit vector and its horizontal position as a SkyPoint. */
export function skyPoint(j2000: Vec3, horizontal: Horizontal): SkyPoint {
  const [x, y, z] = j2000;
  const length = Math.hypot(x, y, z) || 1;
  const ra = ((((Math.atan2(y, x) * DEG) / 15) % 24) + 24) % 24;
  return {
    raHours: ra,
    decDegrees: Math.asin(Math.min(1, Math.max(-1, z / length))) * DEG,
    altitudeDegrees: horizontal.altitudeDegrees,
    azimuthDegrees: horizontal.azimuthDegrees,
  };
}
