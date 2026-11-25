// What the page can ask the sky directly, without the host (the v3 shell's shortcuts): camera
// motion that is the page's own state. The engine registers the handlers once it is mounted;
// before that every call does nothing.

export interface SkyControlHandlers {
  /** Flies the dome back to the south, 30° up, 70° field. */
  readonly home: () => void;
  /** Starts (true) or stops tracking the selection; returns whether it is tracking now. */
  readonly setTracking: (on: boolean) => boolean;
  readonly isTracking: () => boolean;
  /** A drag leaves the view coasting (on by default). */
  readonly setInertia: (on: boolean) => void;
}

let handlers: SkyControlHandlers | null = null;

/** Called by the engine when the sky is built. */
export function registerSkyControl(next: SkyControlHandlers | null): void {
  handlers = next;
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
    handlers?.setInertia(on);
  },
};
