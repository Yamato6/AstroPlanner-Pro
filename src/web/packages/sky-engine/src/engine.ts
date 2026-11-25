// The sky engine: wires the bridge, the clock, the dome view and the input handling to a page.
import { createBridge } from './bridge/bridge';
import type {
  Fov,
  HostMessage,
  Layers,
  MovingBody,
  Observer,
  SkyAppearance,
  ViewMode,
} from './bridge/schema';
import { loadSkyData } from './data/loader';
import type { SkyQuality } from './render/atmosphere';
import { loadMediaManifest } from './render/media';
import type { FrameTransform, Mat3, RefractionModel, Vec3 } from './math/frame';
import { Inertia } from './render/camera';
import { SkyView, type Selection } from './render/sky';
import { registerSkyControl } from './control';
import { FrameStats, SlowFrameWatch } from './stats';
import { SkyClock } from './time/clock';

let mounted = false;

import { skyEvents, skyPoint } from './events';

export interface MountOptions {
  /**
   * Install the sky's own keyboard shortcuts (Space, 1, 2, F, /), sent to the host as
   * `shortcut` messages. Default true (the v2 Sky tab); the v3 shell has its own registry.
   */
  readonly keyboard?: boolean;
  /**
   * A right click selects what is under the pointer and raises `skyEvents` 'contextMenu' for the
   * page's menu. Default false (the v2 Sky tab ignores right clicks).
   */
  readonly contextMenu?: boolean;
}

/**
 * Builds the sky (WebGL canvas, 2D overlay, tooltip, night-vision filter) inside
 * `container` and connects it to the host. One sky per page: later calls do
 * nothing (React's development mode mounts components twice).
 */
export function mountSky(container: HTMLElement, options: MountOptions = {}): void {
  if (mounted) {
    return;
  }
  mounted = true;

  const RENDERER_VERSION = __APP_VERSION__;
  const STATS_INTERVAL_MS = 5000;
  const BENCHMARK_MS = 10_000;

  const params = new URLSearchParams(window.location.search);
  const benchmark = params.get('bench') === '1';
  const starMultiplier = Math.min(8, Math.max(1, Number(params.get('stars') ?? '1') || 1));
  /** Benchmark: render at a fixed resolution ("1920x1080") and/or in a given mode. */
  const fixedSize = parseSize(params.get('res'));
  const benchmarkMode = ((): ViewMode | null => {
    const mode = params.get('mode');
    return mode === 'dome' || mode === 'allSky' || mode === 'finder' ? mode : null;
  })();

  /** Benchmark: the atmosphere quality and the landscape, whatever the preferences and the site. */
  const benchmarkQuality = ((): SkyQuality | null => {
    const quality = params.get('quality');
    return quality === 'low' || quality === 'medium' || quality === 'high' ? quality : null;
  })();
  const benchmarkLandscape = /^[a-z0-9-]{1,40}$/.test(params.get('landscape') ?? '')
    ? params.get('landscape')
    : null;

  function parseSize(value: string | null): { width: number; height: number } | null {
    const match = /^(\d{3,4})x(\d{3,4})$/.exec(value ?? '');
    if (!match) {
      return null;
    }
    const width = Number(match[1]);
    const height = Number(match[2]);
    return width <= 7680 && height <= 4320 ? { width, height } : null;
  }

  const { canvas, overlay, tooltip } = createElements(container);

  const bridge = createBridge();
  const clock = new SkyClock();
  const stats = new FrameStats();

  /** Everything received before the view exists is kept here and applied once it is built. */
  const state: {
    refraction: RefractionModel | null;
    layers: Layers | null;
    frame: FrameTransform | null;
    observer: Observer | null;
    bodies: MovingBody[];
    plan: string[];
    selection: Selection | null;
    showStats: boolean;
    pendingCenter: { vector: Vec3; fov: number | null } | null;
    appearance: SkyAppearance | null;
    fov: Fov | null;
    mode: ViewMode;
  } = {
    refraction: null,
    layers: null,
    frame: null,
    observer: null,
    bodies: [],
    plan: [],
    selection: null,
    showStats: false,
    pendingCenter: null,
    appearance: null,
    fov: null,
    mode: 'dome',
  };

  let view: SkyView | null = null;
  let firstFrameSent = false;

  function reportError(error: unknown): void {
    const message = error instanceof Error ? error.message : String(error);
    bridge.send('error', { message: (message || 'unknown error').slice(0, 1024) });
  }

  window.addEventListener('error', (event) => {
    reportError(event.error ?? event.message);
  });
  window.addEventListener('unhandledrejection', (event) => {
    reportError(event.reason);
  });
  // A blocked script, style or request (CSP, index.html) is reported to the host log.
  document.addEventListener('securitypolicyviolation', (event) => {
    reportError(`CSP blocked ${event.blockedURI || 'inline content'} (${event.effectiveDirective})`);
  });
  window.addEventListener('contextmenu', (event) => {
    event.preventDefault();
  });

  async function initialize(message: Extract<HostMessage, { type: 'init' }>): Promise<void> {
    if (view) {
      return;
    }
    state.refraction = message.payload.refraction;
    state.layers = message.payload.layers;
    state.showStats = message.payload.showStats || benchmark;
    state.mode = message.payload.viewMode;
    document.body.classList.toggle('night', message.payload.theme === 'night');

    const data = await loadSkyData(message.payload.dataBaseUrl);
    const created = new SkyView(canvas, overlay, data, message.payload.layers, starMultiplier, fixedSize);
    created.setRefraction(message.payload.refraction);
    created.showStats = state.showStats;
    created.onFovChanged = scheduleFovChanged;
    created.onTrackingChanged = (active, reason) => {
      skyEvents.emit('tracking', { active, reason });
    };
    if (state.frame) created.setFrame(state.frame);
    if (state.observer) created.setObserver(state.observer);
    created.setBodies(state.bodies);
    created.setHighlights(state.plan);
    created.setSelection(state.selection);
    if (state.appearance) created.setAppearance(state.appearance);
    if (state.fov) created.setFov(state.fov);
    view = created;
    resize();
    created.render(clock.now());
    created.setMode(benchmarkMode ?? state.mode);
    if (benchmarkQuality) {
      created.setQuality(benchmarkQuality);
    }
    if (state.pendingCenter) {
      created.centerOn(state.pendingCenter.vector, state.pendingCenter.fov);
      state.pendingCenter = null;
    }
    // The sky's images arrive after the first frame; without them the sky works as before.
    const mediaBaseUrl = message.payload.mediaBaseUrl;
    if (mediaBaseUrl) {
      created.onMediaError = reportError;
      loadMediaManifest(mediaBaseUrl)
        .then((manifest) => {
          if (manifest) {
            created.setMedia(mediaBaseUrl, manifest);
          } else {
            reportError('sky media: manifest.json is missing or not valid');
          }
        })
        .catch(reportError);
    }
    registerSkyControl({
      home: () => {
        inertia.stop();
        created.flyHome(performance.now());
      },
      setTracking: (on) => {
        if (on) {
          inertia.stop();
          return created.startTracking(performance.now(), clock.now());
        }
        created.stopTracking('manual');
        return false;
      },
      isTracking: () => created.isTracking,
      setInertia: (on) => {
        inertiaEnabled = on;
        if (!on) {
          inertia.stop();
        }
      },
      setQuality: (quality) => {
        created.setQuality(benchmarkQuality ?? quality);
        slowFrames.reset();
      },
      setLightPollutionTint: (tint) => {
        created.setLightPollutionTint(tint);
      },
      setScintillation: (on) => {
        created.setScintillation(on);
      },
    });
    requestAnimationFrame(loop);
  }

  function handle(message: HostMessage): void {
    switch (message.type) {
      case 'init':
        initialize(message).catch(reportError);
        break;
      case 'setObserver':
        state.observer = benchmarkLandscape
          ? { ...message.payload, landscape: benchmarkLandscape }
          : message.payload;
        view?.setObserver(state.observer);
        break;
      case 'setTime':
        clock.set(Date.parse(message.payload.utc), message.payload.rate);
        break;
      case 'setTimeRate':
        clock.setRate(message.payload.rate);
        break;
      case 'frameTransform': {
        const p = message.payload;
        state.frame = {
          epochMs: Date.parse(p.epochUtc),
          precession: p.precession as unknown as Mat3,
          latitudeDegrees: p.latitudeDegrees,
          lstDegrees: p.lstDegrees,
          lstRateDegreesPerSecond: p.lstRateDegreesPerSecond,
        };
        view?.setFrame(state.frame);
        break;
      }
      case 'movingBodies':
        state.bodies = message.payload.bodies;
        view?.setBodies(state.bodies);
        break;
      case 'setLayers':
        state.layers = message.payload;
        view?.setLayers(message.payload);
        break;
      case 'setHighlights':
        state.plan = message.payload.plan;
        view?.setHighlights(state.plan);
        break;
      case 'selectObject':
        state.selection =
          message.payload.id === null ? null : { kind: message.payload.kind, id: message.payload.id };
        view?.setSelection(state.selection);
        break;
      case 'centerOn': {
        const p = message.payload;
        const length = Math.hypot(p.x, p.y, p.z) || 1;
        const vector: Vec3 = [p.x / length, p.y / length, p.z / length];
        if (view) {
          // A flight; the view is reported once it has arrived.
          inertia.stop();
          view.flyTo(vector, p.fovDegrees, performance.now());
          scheduleViewChanged();
        } else {
          state.pendingCenter = { vector, fov: p.fovDegrees };
        }
        break;
      }
      case 'setViewMode':
        state.mode = message.payload.mode;
        if (!benchmarkMode) {
          view?.setMode(message.payload.mode);
        }
        break;
      case 'setFov':
        state.fov = message.payload;
        view?.setFov(message.payload);
        break;
      case 'setSkyAppearance':
        state.appearance = message.payload;
        view?.setAppearance(message.payload);
        break;
      case 'setTheme':
        document.body.classList.toggle('night', message.payload.theme === 'night');
        break;
    }
  }

  bridge.onMessage(handle);

  // ---- frame loop ----------------------------------------------------------------------------

  let lastStatsMs = performance.now();
  let benchmarkStart: number | null = null;
  let benchmarkDone = false;
  let lastError: string | null = null;
  const slowFrames = new SlowFrameWatch();
  let previousFrame = performance.now();

  function loop(now: number): void {
    const current = view;
    if (!current) {
      return;
    }
    // Keep animating even if one frame fails; report each distinct error once.
    requestAnimationFrame(loop);

    if (benchmark && !benchmarkDone) {
      benchmarkStart ??= now;
      const elapsed = now - benchmarkStart;
      const turn = ((elapsed / BENCHMARK_MS) * 2 * Math.PI) % (2 * Math.PI);
      current.azimuth = (turn * 180) / Math.PI;
      current.altitude = 30;
      current.fov = 90;
      current.finderCenter = [Math.cos(turn), Math.sin(turn), 0.3];
      if (elapsed >= BENCHMARK_MS) {
        sendStats(current, true);
        benchmarkDone = true;
      }
    }

    const coast = inertia.step(now);
    if (coast) {
      current.pan(
        coast.dx,
        coast.dy,
        current.renderer.domElement.clientWidth / 2,
        current.renderer.domElement.clientHeight / 2,
      );
      if (!inertia.active) {
        scheduleViewChanged();
      }
    }

    try {
      if (current.animate(now, clock.now())) {
        scheduleViewChanged();
      }
      current.render(clock.now());
      reportView(current);
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      if (message !== lastError) {
        lastError = message;
        reportError(error);
      }
    }
    stats.tick(now);
    // Too slow for five seconds: one quality level down (not while benchmarking).
    if (!benchmark && document.visibilityState === 'visible' && slowFrames.add(now - previousFrame, now)) {
      const lower = current.skyQuality === 'high' ? 'medium' : current.skyQuality === 'medium' ? 'low' : null;
      if (lower) {
        current.setQuality(lower);
        skyEvents.emit('quality', { quality: lower, reason: 'slow' });
      }
    }
    previousFrame = now;

    if (!firstFrameSent) {
      firstFrameSent = true;
      bridge.send('firstFrame', { milliseconds: Math.max(0, now) });
      if (benchmark) {
        stats.reset();
      }
    }

    if (state.showStats) {
      current.statsText = `${stats.fps.toFixed(0)} fps · p95 ${stats.p95FrameMs.toFixed(1)} ms · ${current.starsDrawn} stars · ${current.mode}`;
      if (now - lastStatsMs > STATS_INTERVAL_MS && !benchmark) {
        lastStatsMs = now;
        sendStats(current, false);
      }
    }
  }

  /** The page's HUD follows the field of view, the mode and the star limit (only when they change). */
  let lastView = '';
  function reportView(current: SkyView): void {
    const fovDegrees = Math.round(current.view().fovDegrees * 100) / 100;
    const starLimitMagnitude = Math.round(current.viewLimit * 10) / 10;
    const credits = current.mediaCredits;
    const key = `${fovDegrees}|${current.mode}|${starLimitMagnitude}|${credits.map((c) => c.kind + c.author).join(',')}`;
    if (key !== lastView) {
      lastView = key;
      skyEvents.emit('view', { fovDegrees, mode: current.mode, starLimitMagnitude, credits });
    }
  }

  /** The sky under the pointer for the page's HUD, at most once a frame. */
  let cursorFrame = 0;
  let cursorAt: { x: number; y: number } | null = null;
  function reportCursor(x: number, y: number): void {
    cursorAt = { x, y };
    if (cursorFrame) {
      return;
    }
    cursorFrame = requestAnimationFrame(() => {
      cursorFrame = 0;
      const at = cursorAt;
      const point = at && view ? view.pointAt(at.x, at.y) : null;
      skyEvents.emit('cursor', point ? skyPoint(point.j2000, point.horizontal) : null);
    });
  }

  function sendStats(current: SkyView, isBenchmark: boolean): void {
    bridge.send('stats', {
      fps: Math.round(stats.fps * 10) / 10,
      p95FrameMs: Math.round(stats.p95FrameMs * 100) / 100,
      frames: stats.frames,
      width: current.renderer.domElement.width,
      height: current.renderer.domElement.height,
      stars: current.starsDrawn,
      benchmark: isBenchmark,
    });
  }

  function resize(): void {
    view?.resize(window.innerWidth, window.innerHeight);
  }

  window.addEventListener('resize', resize);

  // ---- input ---------------------------------------------------------------------------------

  let viewTimer: number | undefined;
  function scheduleViewChanged(): void {
    window.clearTimeout(viewTimer);
    viewTimer = window.setTimeout(() => {
      if (view?.mode === 'dome') {
        const v = view.view();
        bridge.send('viewChanged', {
          mode: 'dome',
          azimuthDegrees: ((v.azimuthDegrees % 360) + 360) % 360,
          altitudeDegrees: v.altitudeDegrees,
          fovDegrees: v.fovDegrees,
        });
      }
    }, 600);
  }

  let fovTimer: number | undefined;
  function scheduleFovChanged(center: Vec3, rotationDegrees: number): void {
    window.clearTimeout(fovTimer);
    fovTimer = window.setTimeout(() => {
      const length = Math.hypot(center[0], center[1], center[2]) || 1;
      bridge.send('fovChanged', {
        x: center[0] / length,
        y: center[1] / length,
        z: center[2] / length,
        rotationDegrees: ((rotationDegrees % 360) + 360) % 360,
      });
    }, 250);
  }

  /** A drag pans the view, or moves the field-of-view rectangle when it starts inside it. */
  let drag: { x: number; y: number; moved: number; fov: boolean } | null = null;
  /** What a drag leaves when it is released fast (spec §8.1); the page may turn it off. */
  const inertia = new Inertia();
  let inertiaEnabled = true;

  overlay.addEventListener('pointerdown', (event) => {
    inertia.stop();
    drag = {
      x: event.clientX,
      y: event.clientY,
      moved: 0,
      fov: view?.fovContains(event.clientX, event.clientY) ?? false,
    };
    overlay.setPointerCapture(event.pointerId);
  });

  overlay.addEventListener('pointermove', (event) => {
    if (drag && view) {
      const dx = event.clientX - drag.x;
      const dy = event.clientY - drag.y;
      if (drag.fov) {
        view.moveFov(drag.x, drag.y, event.clientX, event.clientY);
      } else {
        if (dx !== 0 || dy !== 0) {
          view.interrupt();
        }
        view.pan(dx, dy, event.clientX, event.clientY);
        inertia.sample(event.clientX, event.clientY, event.timeStamp);
        scheduleViewChanged();
      }
      drag = { ...drag, x: event.clientX, y: event.clientY, moved: drag.moved + Math.abs(dx) + Math.abs(dy) };
      return;
    }
    reportCursor(event.clientX, event.clientY);
    scheduleHover(event.clientX, event.clientY);
    overlay.classList.toggle('over-fov', view?.fovContains(event.clientX, event.clientY) ?? false);
  });

  /** Selects what is under a screen point (or nothing) and tells the host; returns what was picked. */
  function selectAt(x: number, y: number): ReturnType<SkyView['pick']> {
    if (!view) {
      return null;
    }
    const picked = view.pick(x, y, clock.now());
    const selection: Selection | null = picked ? { kind: picked.kind, id: picked.id } : null;
    state.selection = selection;
    view.setSelection(selection);
    bridge.send(
      'selectObject',
      selection ? { kind: selection.kind, id: selection.id } : { kind: null, id: null },
    );
    return picked;
  }

  overlay.addEventListener('pointerup', (event) => {
    const wasClick = drag !== null && drag.moved < 4 && event.button === 0;
    if (drag && !drag.fov && !wasClick && inertiaEnabled) {
      inertia.release(event.timeStamp);
    } else {
      inertia.stop();
    }
    drag = null;
    if (wasClick) {
      selectAt(event.clientX, event.clientY);
    }
  });

  overlay.addEventListener('pointerleave', () => {
    cursorAt = null;
    skyEvents.emit('cursor', null);
  });

  // A right click selects what is under the pointer (like a click) and offers its actions: the
  // page draws the menu. Without an object, the menu acts on the point of the sky.
  overlay.addEventListener('contextmenu', (event) => {
    event.preventDefault();
    if (!view || options.contextMenu !== true) {
      return;
    }
    const picked = view.pick(event.clientX, event.clientY, clock.now());
    if (picked) {
      selectAt(event.clientX, event.clientY);
    }
    const point = view.pointAt(event.clientX, event.clientY);
    skyEvents.emit('contextMenu', {
      x: event.clientX,
      y: event.clientY,
      point: point ? skyPoint(point.j2000, point.horizontal) : null,
      picked: picked
        ? { kind: picked.kind, id: picked.id, label: picked.label?.slice(0, 128) ?? null }
        : null,
    });
  });

  overlay.addEventListener(
    'wheel',
    (event) => {
      event.preventDefault();
      if (event.shiftKey && view?.fovCenter) {
        // Shift + wheel rotates the field-of-view rectangle (1° per notch, 5° with Ctrl).
        view.rotateFov(Math.sign(event.deltaY || event.deltaX) * (event.ctrlKey ? 5 : 1));
        return;
      }
      view?.stopFlight();
      view?.zoom(Math.exp(event.deltaY * 0.0012));
      scheduleViewChanged();
    },
    { passive: false },
  );

  let hoverTimer: number | undefined;
  let hovered: string | null = null;
  function scheduleHover(x: number, y: number): void {
    window.clearTimeout(hoverTimer);
    hoverTimer = window.setTimeout(() => {
      const picked = view?.pick(x, y, clock.now()) ?? null;
      const key = picked ? `${picked.kind}:${picked.id}` : null;
      if (picked) {
        tooltip.textContent = picked.label ?? picked.id;
        tooltip.style.left = `${x + 14}px`;
        tooltip.style.top = `${y + 14}px`;
        tooltip.hidden = false;
      } else {
        tooltip.hidden = true;
      }
      if (key !== hovered) {
        hovered = key;
        bridge.send(
          'hover',
          picked
            ? { kind: picked.kind, id: picked.id, label: picked.label?.slice(0, 128) ?? null }
            : { kind: null, id: null, label: null },
        );
      }
    }, 90);
  }

  const shortcuts: Record<string, 'togglePause' | 'viewDome' | 'viewAllSky' | 'toggleFov' | 'focusSearch'> = {
    ' ': 'togglePause',
    '1': 'viewDome',
    '2': 'viewAllSky',
    f: 'toggleFov',
    F: 'toggleFov',
    '/': 'focusSearch',
  };

  window.addEventListener('keydown', (event) => {
    if (options.keyboard === false) {
      return;
    }
    // Camera keys the sky handles itself (the v3 shell has them in its registry).
    if (view && !event.ctrlKey && !event.altKey && !event.metaKey) {
      if (event.key === 'Home') {
        event.preventDefault();
        inertia.stop();
        view.flyHome(performance.now());
        return;
      }
      if (event.key === 't' || event.key === 'T') {
        event.preventDefault();
        if (view.isTracking) {
          view.stopTracking('manual');
        } else {
          view.startTracking(performance.now(), clock.now());
        }
        return;
      }
    }
    const action = shortcuts[event.key];
    if (action && !event.ctrlKey && !event.altKey && !event.metaKey) {
      event.preventDefault();
      bridge.send('shortcut', { action });
    }
  });

  // ---- start ---------------------------------------------------------------------------------

  const webGlVersion: 1 | 2 = document.createElement('canvas').getContext('webgl2') ? 2 : 1;
  bridge.send('ready', { rendererVersion: RENDERER_VERSION, webGlVersion });

  if (import.meta.env.DEV && !bridge.connected) {
    // `npm run dev` outside WebView2: replay the golden C# messages (tests/fixtures/bridge).
    import('./dev/demo')
      .then(({ demoMessages }) => {
        demoMessages().forEach(handle);
      })
      .catch(reportError);
  }
}

const SVG = 'http://www.w3.org/2000/svg';

/** The elements index.html used to declare; style.css styles them by id. */
function createElements(container: HTMLElement): {
  canvas: HTMLCanvasElement;
  overlay: HTMLCanvasElement;
  tooltip: HTMLDivElement;
} {
  const canvas = document.createElement('canvas');
  canvas.id = 'sky';
  const overlay = document.createElement('canvas');
  overlay.id = 'overlay';
  const tooltip = document.createElement('div');
  tooltip.id = 'tooltip';
  tooltip.hidden = true;

  // Night-vision theme: the luminance of every pixel, drawn in red only (no green or blue light).
  const filters = document.createElementNS(SVG, 'svg');
  filters.id = 'filters';
  filters.setAttribute('width', '0');
  filters.setAttribute('height', '0');
  filters.setAttribute('aria-hidden', 'true');
  const filter = document.createElementNS(SVG, 'filter');
  filter.id = 'night-red';
  filter.setAttribute('color-interpolation-filters', 'sRGB');
  const matrix = document.createElementNS(SVG, 'feColorMatrix');
  matrix.setAttribute('type', 'matrix');
  matrix.setAttribute('values', '0.30 0.59 0.11 0 0  0 0 0 0 0  0 0 0 0 0  0 0 0 1 0');
  filter.append(matrix);
  filters.append(filter);

  container.append(canvas, overlay, tooltip, filters);
  return { canvas, overlay, tooltip };
}
