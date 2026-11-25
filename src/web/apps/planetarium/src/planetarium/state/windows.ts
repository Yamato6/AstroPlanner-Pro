// The floating windows (spec §3.3): the layout is page state (zustand) made of ui-kit's pure
// transitions; the host only stores it (layout.load at start, layout.save after changes).
import type { WindowLayoutState } from '@astroplanner/bridge-client';
import {
  type Arrangement,
  type Layout,
  type Rect,
  type WindowState,
  arrangeWindow,
  closeWindow,
  cycleWindows,
  focusWindow,
  minimizeWindow,
  openWindow,
  placeWindow,
  railPress,
  setDockWidth,
  topWindow,
} from '@astroplanner/ui-kit';
import { create } from 'zustand';
import { type WindowId, windowDefinitions } from '../windows/registry';

interface WindowsState {
  readonly layout: Layout;
  /** False until the saved layout arrived (or there is none): nothing is saved before. */
  readonly hydrated: boolean;
  /** The part of the viewport windows may use (set by the window layer from the viewport). */
  readonly area: Rect;
  /** The window that must take the keyboard focus, and a counter that changes with each request. */
  readonly focus: { readonly id: WindowId | null; readonly serial: number };
  readonly hydrate: (saved: readonly WindowLayoutState[]) => void;
  readonly setArea: (area: Rect) => void;
  readonly open: (id: WindowId) => void;
  readonly press: (id: WindowId) => void;
  readonly close: (id: WindowId) => void;
  readonly closeTop: () => void;
  readonly minimize: (id: WindowId) => void;
  readonly bringToFront: (id: WindowId) => void;
  readonly arrange: (id: WindowId, arrangement: Arrangement) => void;
  readonly place: (id: WindowId, rect: Rect) => void;
  readonly dock: (id: WindowId, width: number) => void;
  readonly cycle: (direction: 1 | -1) => void;
  readonly reset: () => void;
}

const isWindowId = (id: string): id is WindowId => Object.hasOwn(windowDefinitions, id);

/** The saved layout as the page's state (rows of other windows are dropped). */
export function fromSaved(saved: readonly WindowLayoutState[]): Layout {
  const layout: Record<string, WindowState> = {};
  for (const w of saved) {
    if (isWindowId(w.id)) {
      layout[w.id] = {
        id: w.id,
        isOpen: w.isOpen,
        isMinimized: w.isMinimized,
        arrangement: w.arrangement,
        rect: { x: w.x, y: w.y, width: w.width, height: w.height },
        dockWidth: w.dockWidth,
        zOrder: w.zOrder,
      };
    }
  }
  return layout;
}

const limit = (value: number, min: number, max: number): number =>
  Math.min(Math.max(Math.round(value), min), max);

/** The page's state as the layout.save contract. */
export function toSaved(layout: Layout): WindowLayoutState[] {
  return Object.values(layout)
    .filter((w): w is WindowState & { id: WindowId } => isWindowId(w.id))
    .map((w) => ({
      id: w.id,
      isOpen: w.isOpen,
      isMinimized: w.isMinimized,
      arrangement: w.arrangement,
      x: limit(w.rect.x, -20000, 20000),
      y: limit(w.rect.y, -20000, 20000),
      width: limit(w.rect.width, 1, 20000),
      height: limit(w.rect.height, 1, 20000),
      dockWidth: limit(w.dockWidth, 1, 20000),
      zOrder: limit(w.zOrder, 0, 10000),
    }));
}

export const useWindows = create<WindowsState>()((set, get) => {
  /** Applies a transition; `focus` asks the window in front afterwards to take the keyboard focus. */
  const apply = (next: (layout: Layout, area: Rect) => Layout, focus = false): void => {
    set((state) => {
      const layout = next(state.layout, state.area);
      if (layout === state.layout) {
        return state;
      }
      const top = topWindow(layout);
      return focus && top && isWindowId(top)
        ? { layout, focus: { id: top, serial: state.focus.serial + 1 } }
        : { layout };
    });
  };
  const size = (id: WindowId) => windowDefinitions[id].size;

  return {
    layout: {},
    hydrated: false,
    area: { x: 0, y: 0, width: 1280, height: 720 },
    focus: { id: null, serial: 0 },
    hydrate: (saved) => {
      // Windows opened before the saved layout arrived stay as they are.
      set((state) => ({ layout: { ...fromSaved(saved), ...state.layout }, hydrated: true }));
    },
    setArea: (area) => {
      const current = get().area;
      if (
        current.x !== area.x ||
        current.y !== area.y ||
        current.width !== area.width ||
        current.height !== area.height
      ) {
        set({ area });
      }
    },
    open: (id) => {
      apply((layout, area) => openWindow(layout, id, size(id), area), true);
    },
    press: (id) => {
      apply((layout, area) => railPress(layout, id, size(id), area), true);
    },
    close: (id) => {
      apply((layout) => closeWindow(layout, id), true);
    },
    closeTop: () => {
      const top = topWindow(get().layout);
      if (top && isWindowId(top)) {
        get().close(top);
      }
    },
    minimize: (id) => {
      apply((layout) => minimizeWindow(layout, id), true);
    },
    bringToFront: (id) => {
      apply((layout) => focusWindow(layout, id));
    },
    arrange: (id, arrangement) => {
      apply((layout, area) => arrangeWindow(layout, id, arrangement, area));
    },
    place: (id, rect) => {
      apply((layout) => placeWindow(layout, id, rect));
    },
    dock: (id, width) => {
      apply((layout) => setDockWidth(layout, id, width));
    },
    cycle: (direction) => {
      apply((layout) => cycleWindows(layout, direction), true);
    },
    reset: () => {
      set({ layout: {} });
    },
  };
});
