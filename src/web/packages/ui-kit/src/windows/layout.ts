// The window manager's state and geometry (spec §3.3, §7.2), as pure functions: the page keeps a
// Layout in its store, and every user action is one of these transitions. Coordinates are CSS
// pixels of the viewport; `area` is the part of it windows may use (below the top bar, right of
// the rail).

export type Arrangement = 'floating' | 'maximized' | 'dockLeft' | 'dockRight';

export interface Rect {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface WindowState {
  readonly id: string;
  readonly isOpen: boolean;
  /** Open but hidden in the rail. */
  readonly isMinimized: boolean;
  readonly arrangement: Arrangement;
  /** The floating rectangle, kept while maximised or docked so that restoring puts it back. */
  readonly rect: Rect;
  /** Width as a side sheet. */
  readonly dockWidth: number;
  /** Higher is in front. */
  readonly zOrder: number;
}

export interface WindowSize {
  readonly width: number;
  readonly height: number;
  readonly minWidth: number;
  readonly minHeight: number;
}

export type Layout = Readonly<Record<string, WindowState>>;

/** A maximised window leaves this much of the sky around it (spec §3.3). */
export const MAXIMIZED_MARGIN = 24;
/** Dragging the pointer this close to an edge of the area snaps the window there. */
export const SNAP_EDGE = 16;
/** Window edges closer than this to an area or window edge stick to it. */
export const MAGNET = 10;
/** This much of a window always stays on screen, so it can be grabbed again. */
export const MIN_VISIBLE = 64;
/** Height of a window's title bar. */
export const TITLE_BAR = 36;
/** A side sheet takes at most this share of the area. */
export const DOCK_MAX_SHARE = 0.6;
/** Each window opened over others is offset by this much. */
const CASCADE = 28;
/** Stacking orders are renumbered past this (the contract allows up to 10000). */
const Z_LIMIT = 1000;

const clamp = (value: number, min: number, max: number): number =>
  Math.min(Math.max(value, min), Math.max(min, max));

function visible(layout: Layout): WindowState[] {
  return Object.values(layout).filter((w) => w.isOpen && !w.isMinimized);
}

/** The window in front (the one keyboard commands act on), if any is showing. */
export function topWindow(layout: Layout): string | null {
  let top: WindowState | null = null;
  for (const w of visible(layout)) {
    if (!top || w.zOrder > top.zOrder) {
      top = w;
    }
  }
  return top?.id ?? null;
}

/** Stacking orders 1…n in the same order, so they stay small however long the session. */
export function renumbered(layout: Layout): Layout {
  const ordered = Object.values(layout).sort((a, b) => a.zOrder - b.zOrder || a.id.localeCompare(b.id));
  const result: Record<string, WindowState> = {};
  ordered.forEach((w, index) => {
    result[w.id] = { ...w, zOrder: index + 1 };
  });
  return result;
}

function withFront(layout: Layout, id: string, change: Partial<WindowState>): Layout {
  const current = layout[id];
  if (!current) {
    return layout;
  }
  const z = Math.max(0, ...Object.values(layout).map((w) => w.zOrder)) + 1;
  const next = { ...layout, [id]: { ...current, ...change, zOrder: z } };
  return z > Z_LIMIT ? renumbered(next) : next;
}

/** A rectangle with at least `MIN_VISIBLE` px and the whole title bar inside the area, and a size that fits. */
export function clampRect(rect: Rect, area: Rect, size: WindowSize): Rect {
  const width = clamp(Math.round(rect.width), size.minWidth, area.width);
  const height = clamp(Math.round(rect.height), size.minHeight, area.height);
  return {
    x: clamp(Math.round(rect.x), area.x - width + MIN_VISIBLE, area.x + area.width - MIN_VISIBLE),
    y: clamp(Math.round(rect.y), area.y, area.y + area.height - TITLE_BAR),
    width,
    height,
  };
}

/** Where a window is drawn: its floating rectangle, or the area it is maximised or docked to. */
export function frameOf(window: WindowState, area: Rect, size: WindowSize): Rect {
  switch (window.arrangement) {
    case 'maximized':
      return {
        x: area.x + MAXIMIZED_MARGIN,
        y: area.y + MAXIMIZED_MARGIN,
        width: Math.max(size.minWidth, area.width - 2 * MAXIMIZED_MARGIN),
        height: Math.max(size.minHeight, area.height - 2 * MAXIMIZED_MARGIN),
      };
    case 'dockLeft':
    case 'dockRight': {
      const width = dockWidthIn(window.dockWidth, area, size);
      return {
        x: window.arrangement === 'dockLeft' ? area.x : area.x + area.width - width,
        y: area.y,
        width,
        height: area.height,
      };
    }
    default:
      return clampRect(window.rect, area, size);
  }
}

/** A side sheet's width, between the window's minimum and `DOCK_MAX_SHARE` of the area. */
export function dockWidthIn(width: number, area: Rect, size: WindowSize): number {
  return Math.round(clamp(width, size.minWidth, area.width * DOCK_MAX_SHARE));
}

/** Where a window dragged with the pointer here would snap: the left or right half, or maximised at the top. */
export function snapZone(
  pointer: { readonly x: number; readonly y: number },
  area: Rect,
): Arrangement | null {
  if (pointer.x <= area.x + SNAP_EDGE) {
    return 'dockLeft';
  }
  if (pointer.x >= area.x + area.width - SNAP_EDGE) {
    return 'dockRight';
  }
  if (pointer.y <= area.y + SNAP_EDGE) {
    return 'maximized';
  }
  return null;
}

/** Sticks a moving rectangle's edges to the area's edges and to other windows' edges when they are close. */
export function magnet(rect: Rect, area: Rect, others: readonly Rect[]): Rect {
  const xs = [area.x, area.x + area.width];
  const ys = [area.y, area.y + area.height];
  for (const other of others) {
    xs.push(other.x, other.x + other.width);
    ys.push(other.y, other.y + other.height);
  }
  const stick = (start: number, length: number, lines: number[]): number => {
    let best = start;
    let distance = MAGNET + 1;
    for (const line of lines) {
      for (const candidate of [line, line - length]) {
        const d = Math.abs(candidate - start);
        if (d < distance) {
          distance = d;
          best = candidate;
        }
      }
    }
    return distance <= MAGNET ? best : start;
  };
  return { ...rect, x: stick(rect.x, rect.width, xs), y: stick(rect.y, rect.height, ys) };
}

export interface Edges {
  readonly left?: boolean;
  readonly right?: boolean;
  readonly top?: boolean;
  readonly bottom?: boolean;
}

/** A rectangle resized by dragging these edges by (dx, dy), never below the minimum size or out of the area. */
export function resizeRect(
  rect: Rect,
  edges: Edges,
  dx: number,
  dy: number,
  area: Rect,
  size: WindowSize,
): Rect {
  let { x, y, width, height } = rect;
  if (edges.right) {
    width = clamp(width + dx, size.minWidth, area.x + area.width - x);
  }
  if (edges.bottom) {
    height = clamp(height + dy, size.minHeight, area.y + area.height - y);
  }
  if (edges.left) {
    const right = x + width;
    x = clamp(x + dx, area.x, right - size.minWidth);
    width = right - x;
  }
  if (edges.top) {
    const bottom = y + height;
    y = clamp(y + dy, area.y, bottom - size.minHeight);
    height = bottom - y;
  }
  return { x: Math.round(x), y: Math.round(y), width: Math.round(width), height: Math.round(height) };
}

/** Opens a window (or shows it again) in front. A new one is centred, cascading over those already open. */
export function openWindow(layout: Layout, id: string, size: WindowSize, area: Rect): Layout {
  if (layout[id]) {
    return withFront(layout, id, { isOpen: true, isMinimized: false });
  }
  const offset = visible(layout).length * CASCADE;
  const width = Math.min(size.width, area.width);
  const height = Math.min(size.height, area.height);
  const rect = clampRect(
    {
      x: area.x + (area.width - width) / 2 + offset,
      y: area.y + Math.max(0, (area.height - height) / 3) + offset,
      width,
      height,
    },
    area,
    size,
  );
  const created: WindowState = {
    id,
    isOpen: true,
    isMinimized: false,
    arrangement: 'floating',
    rect,
    dockWidth: size.width,
    zOrder: 0,
  };
  return withFront({ ...layout, [id]: created }, id, {});
}

export function closeWindow(layout: Layout, id: string): Layout {
  const current = layout[id];
  return current ? { ...layout, [id]: { ...current, isOpen: false, isMinimized: false } } : layout;
}

export function minimizeWindow(layout: Layout, id: string): Layout {
  const current = layout[id];
  return current?.isOpen ? { ...layout, [id]: { ...current, isMinimized: true } } : layout;
}

/** Brings a window to the front (nothing changes if it already is). */
export function focusWindow(layout: Layout, id: string): Layout {
  return topWindow(layout) === id ? layout : withFront(layout, id, {});
}

/**
 * A rail button: opens or restores its window; on the window in front, minimises it; on one
 * behind others, brings it to the front.
 */
export function railPress(layout: Layout, id: string, size: WindowSize, area: Rect): Layout {
  const current = layout[id];
  if (!current?.isOpen || current.isMinimized) {
    return openWindow(layout, id, size, area);
  }
  return topWindow(layout) === id ? minimizeWindow(layout, id) : focusWindow(layout, id);
}

/** Maximises, docks or restores a window. Docking (a snap to a half) makes it half the area wide. */
export function arrangeWindow(layout: Layout, id: string, arrangement: Arrangement, area: Rect): Layout {
  const current = layout[id];
  if (!current) {
    return layout;
  }
  const docking = arrangement === 'dockLeft' || arrangement === 'dockRight';
  return withFront(layout, id, {
    arrangement,
    ...(docking ? { dockWidth: Math.round(area.width / 2) } : {}),
  });
}

/** A floating window moved or resized to this rectangle. */
export function placeWindow(layout: Layout, id: string, rect: Rect): Layout {
  const current = layout[id];
  return current ? { ...layout, [id]: { ...current, arrangement: 'floating', rect } } : layout;
}

export function setDockWidth(layout: Layout, id: string, width: number): Layout {
  const current = layout[id];
  return current ? { ...layout, [id]: { ...current, dockWidth: Math.round(width) } } : layout;
}

/** Ctrl+Tab: the window at the back comes to the front; backwards, the front one goes to the back. */
export function cycleWindows(layout: Layout, direction: 1 | -1): Layout {
  const shown = visible(layout).sort((a, b) => a.zOrder - b.zOrder);
  const back = shown[0];
  const front = shown[shown.length - 1];
  if (shown.length < 2 || !back || !front) {
    return layout;
  }
  if (direction === 1) {
    return withFront(layout, back.id, {});
  }
  return renumbered({ ...layout, [front.id]: { ...front, zOrder: back.zOrder - 1 } });
}
