// Entry point: wires the bridge, the clock, the dome view and the input handling.
import { createBridge } from './bridge/bridge';
import type { HostMessage, Layers, MovingBody, Observer } from './bridge/schema';
import { loadSkyData } from './data/loader';
import type { FrameTransform, Mat3, RefractionModel, Vec3 } from './math/frame';
import { SkyView, type Selection } from './render/sky';
import { FrameStats } from './stats';
import { SkyClock } from './time/clock';
import './style.css';

const RENDERER_VERSION = __APP_VERSION__;
const STATS_INTERVAL_MS = 5000;
const BENCHMARK_MS = 10_000;

const params = new URLSearchParams(window.location.search);
const benchmark = params.get('bench') === '1';
const starMultiplier = Math.min(8, Math.max(1, Number(params.get('stars') ?? '1') || 1));

function element<T extends HTMLElement>(selector: string, type: new () => T): T {
  const found = document.querySelector(selector);
  if (!(found instanceof type)) {
    throw new Error(`index.html is missing ${selector}`);
  }
  return found;
}

const canvas = element('#sky', HTMLCanvasElement);
const overlay = element('#overlay', HTMLCanvasElement);
const tooltip = element('#tooltip', HTMLDivElement);

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
  document.body.classList.toggle('night', message.payload.theme === 'night');

  const data = await loadSkyData(message.payload.dataBaseUrl);
  const created = new SkyView(canvas, overlay, data, message.payload.layers, starMultiplier);
  created.setRefraction(message.payload.refraction);
  created.showStats = state.showStats;
  if (state.frame) created.setFrame(state.frame);
  if (state.observer) created.setObserver(state.observer);
  created.setBodies(state.bodies);
  created.setHighlights(state.plan);
  created.setSelection(state.selection);
  view = created;
  resize();
  if (state.pendingCenter) {
    created.render(clock.now());
    created.centerOn(state.pendingCenter.vector, state.pendingCenter.fov);
    state.pendingCenter = null;
  }
  requestAnimationFrame(loop);
}

function handle(message: HostMessage): void {
  switch (message.type) {
    case 'init':
      initialize(message).catch(reportError);
      break;
    case 'setObserver':
      state.observer = message.payload;
      view?.setObserver(message.payload);
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
        view.centerOn(vector, p.fovDegrees);
        scheduleViewChanged();
      } else {
        state.pendingCenter = { vector, fov: p.fovDegrees };
      }
      break;
    }
    case 'setViewMode':
      // The all-sky and finder charts arrive in M2b; the dome is the only mode for now.
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
    current.azimuth = ((elapsed / BENCHMARK_MS) * 360) % 360;
    current.altitude = 30;
    current.fov = 90;
    if (elapsed >= BENCHMARK_MS) {
      sendStats(current, true);
      benchmarkDone = true;
    }
  }

  try {
    current.render(clock.now());
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (message !== lastError) {
      lastError = message;
      reportError(error);
    }
  }
  stats.tick(now);

  if (!firstFrameSent) {
    firstFrameSent = true;
    bridge.send('firstFrame', { milliseconds: Math.max(0, now) });
    if (benchmark) {
      stats.reset();
    }
  }

  if (state.showStats) {
    current.statsText = `${stats.fps.toFixed(0)} fps · p95 ${stats.p95FrameMs.toFixed(1)} ms · ${current.starsDrawn} stars`;
    if (now - lastStatsMs > STATS_INTERVAL_MS && !benchmark) {
      lastStatsMs = now;
      sendStats(current, false);
    }
  }
}

function sendStats(current: SkyView, isBenchmark: boolean): void {
  bridge.send('stats', {
    fps: Math.round(stats.fps * 10) / 10,
    p95FrameMs: Math.round(stats.p95FrameMs * 100) / 100,
    frames: stats.frames,
    width: Math.round(window.innerWidth * window.devicePixelRatio),
    height: Math.round(window.innerHeight * window.devicePixelRatio),
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
    if (view) {
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

let drag: { x: number; y: number; moved: number } | null = null;

overlay.addEventListener('pointerdown', (event) => {
  drag = { x: event.clientX, y: event.clientY, moved: 0 };
  overlay.setPointerCapture(event.pointerId);
});

overlay.addEventListener('pointermove', (event) => {
  if (drag && view) {
    const dx = event.clientX - drag.x;
    const dy = event.clientY - drag.y;
    drag = { x: event.clientX, y: event.clientY, moved: drag.moved + Math.abs(dx) + Math.abs(dy) };
    view.pan(dx, dy);
    scheduleViewChanged();
    return;
  }
  scheduleHover(event.clientX, event.clientY);
});

overlay.addEventListener('pointerup', (event) => {
  const wasClick = drag !== null && drag.moved < 4;
  drag = null;
  if (wasClick && view) {
    const picked = view.pick(event.clientX, event.clientY, clock.now());
    const selection: Selection | null = picked ? { kind: picked.kind, id: picked.id } : null;
    state.selection = selection;
    view.setSelection(selection);
    bridge.send(
      'selectObject',
      selection ? { kind: selection.kind, id: selection.id } : { kind: null, id: null },
    );
  }
});

overlay.addEventListener(
  'wheel',
  (event) => {
    event.preventDefault();
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
