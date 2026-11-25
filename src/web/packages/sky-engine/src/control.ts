// What the page can ask the sky directly, without the host (the v3 shell's shortcuts): camera
// motion that is the page's own state. The engine registers the handlers once it is mounted;
// before that every call does nothing; the last options are kept and applied once it is.
import type { SkyQuality } from './render/atmosphere';
import type { LightPollutionTint } from './render/skyStyle';

export type { LightPollutionTint, SkyQuality };

export interface SkyControlHandlers {
  /** Flies the dome back to the south, 30° up, 70° field. */
  readonly home: () => void;
  /** Starts (true) or stops tracking the selection; returns whether it is tracking now. */
  readonly setTracking: (on: boolean) => boolean;
  readonly isTracking: () => boolean;
  /** A drag leaves the view coasting (on by default). */
  readonly setInertia: (on: boolean) => void;
  /** Atmosphere quality (spec §8.2); the sky may lower it by itself when frames get slow. */
  readonly setQuality: (quality: SkyQuality) => void;
  readonly setLightPollutionTint: (tint: LightPollutionTint) => void;
  /** Twinkling stars (High quality only). */
  readonly setScintillation: (on: boolean) => void;
}

let handlers: SkyControlHandlers | null = null;

/** Options asked for before the sky was built. */
const pending: {
  inertia?: boolean;
  quality?: SkyQuality;
  tint?: LightPollutionTint;
  scintillation?: boolean;
} = {};

/** Called by the engine when the sky is built. */
export function registerSkyControl(next: SkyControlHandlers | null): void {
  handlers = next;
  if (next) {
    if (pending.inertia !== undefined) next.setInertia(pending.inertia);
    if (pending.quality !== undefined) next.setQuality(pending.quality);
    if (pending.tint !== undefined) next.setLightPollutionTint(pending.tint);
    if (pending.scintillation !== undefined) next.setScintillation(pending.scintillation);
  }
}

export const skyControl = {
  home(): void {
    handlers?.home();
  },
  setTracking(on: boolean): boolean {
    return handlers?.setTracking(on) ?? false;
  },
  get isTracking(): boolean {
    return handlers?.isTracking() ?? false;
  },
  setInertia(on: boolean): void {
    pending.inertia = on;
    handlers?.setInertia(on);
  },
  setQuality(quality: SkyQuality): void {
    pending.quality = quality;
    handlers?.setQuality(quality);
  },
  setLightPollutionTint(tint: LightPollutionTint): void {
    pending.tint = tint;
    handlers?.setLightPollutionTint(tint);
  },
  setScintillation(on: boolean): void {
    pending.scintillation = on;
    handlers?.setScintillation(on);
  },
};
