import { describe, expect, it } from 'vitest';
import {
  type Layout,
  type Rect,
  type WindowState,
  type WindowSize,
  MAXIMIZED_MARGIN,
  arrangeWindow,
  clampRect,
  closeWindow,
  cycleWindows,
  focusWindow,
  frameOf,
  magnet,
  minimizeWindow,
  openWindow,
  placeWindow,
  railPress,
  renumbered,
  resizeRect,
  snapZone,
  topWindow,
} from '../src/windows/layout';

// A 1440 × 900 viewport less the top bar (60 px) and the rail (64 px).
const area: Rect = { x: 64, y: 60, width: 1368, height: 832 };
const size: WindowSize = { width: 480, height: 560, minWidth: 320, minHeight: 240 };

/** A window of the layout (the test fails if it is missing). */
function win(layout: Layout, id: string): WindowState {
  const w = layout[id];
  if (!w) {
    throw new Error(`no window ${id}`);
  }
  return w;
}

const open = (...ids: string[]): Layout =>
  ids.reduce<Layout>((layout, id) => openWindow(layout, id, size, area), {});

describe('opening, focus and the rail', () => {
  it('opens a window centred and in front, the next ones cascading', () => {
    const layout = open('settings', 'help');

    expect(layout.settings?.rect).toEqual({ x: 508, y: 151, width: 480, height: 560 });
    expect(layout.help?.rect.x).toBe(508 + 28);
    expect(topWindow(layout)).toBe('help');
  });

  it('brings a window behind others to the front, and leaves one in front alone', () => {
    const layout = open('settings', 'help');

    expect(topWindow(focusWindow(layout, 'settings'))).toBe('settings');
    expect(focusWindow(layout, 'help')).toBe(layout);
  });

  it('a rail button opens, focuses, minimises and restores its window', () => {
    let layout = open('settings', 'help');

    layout = railPress(layout, 'settings', size, area); // behind help → front
    expect(topWindow(layout)).toBe('settings');
    layout = railPress(layout, 'settings', size, area); // in front → minimised
    expect(layout.settings?.isMinimized).toBe(true);
    expect(topWindow(layout)).toBe('help');
    layout = railPress(layout, 'settings', size, area); // minimised → restored in front
    expect(layout.settings).toMatchObject({ isOpen: true, isMinimized: false });
    expect(topWindow(layout)).toBe('settings');
  });

  it('closing keeps where the window was, and reopening puts it back there', () => {
    let layout = placeWindow(open('help'), 'help', { x: 100, y: 120, width: 400, height: 300 });

    layout = closeWindow(layout, 'help');
    expect(topWindow(layout)).toBeNull();
    layout = openWindow(layout, 'help', size, area);

    expect(layout.help?.rect).toEqual({ x: 100, y: 120, width: 400, height: 300 });
  });

  it('minimising a closed window does nothing', () => {
    const layout = closeWindow(open('help'), 'help');
    expect(minimizeWindow(layout, 'help')).toBe(layout);
  });

  it('Ctrl+Tab brings the back window forward; backwards sends the front one back', () => {
    const layout = open('a', 'b', 'c');

    expect(topWindow(cycleWindows(layout, 1))).toBe('a');
    expect(topWindow(cycleWindows(layout, -1))).toBe('b');
    expect(cycleWindows(open('a'), 1)).toEqual(open('a'));
  });

  it('keeps stacking orders small', () => {
    let layout = open('a', 'b');
    for (let i = 0; i < 2000; i++) {
      layout = focusWindow(layout, i % 2 ? 'a' : 'b');
    }

    expect(Math.max(...Object.values(layout).map((w) => w.zOrder))).toBeLessThanOrEqual(1001);
    expect(
      Object.values(renumbered(layout))
        .map((w) => w.zOrder)
        .sort(),
    ).toEqual([1, 2]);
  });
});

describe('geometry', () => {
  it('keeps the title bar reachable and the size within the area and above the minimum', () => {
    expect(clampRect({ x: -5000, y: -50, width: 100, height: 5000 }, area, size)).toEqual({
      x: area.x - 320 + 64,
      y: area.y,
      width: 320,
      height: 832,
    });
    expect(clampRect({ x: 5000, y: 5000, width: 400, height: 300 }, area, size)).toEqual({
      x: area.x + area.width - 64,
      y: area.y + area.height - 36,
      width: 400,
      height: 300,
    });
  });

  it('maximised leaves a margin of sky; docked is a full-height side sheet', () => {
    let layout = arrangeWindow(open('help'), 'help', 'maximized', area);
    expect(frameOf(win(layout, 'help'), area, size)).toEqual({
      x: area.x + MAXIMIZED_MARGIN,
      y: area.y + MAXIMIZED_MARGIN,
      width: area.width - 2 * MAXIMIZED_MARGIN,
      height: area.height - 2 * MAXIMIZED_MARGIN,
    });

    layout = arrangeWindow(layout, 'help', 'dockRight', area);
    expect(frameOf(win(layout, 'help'), area, size)).toEqual({
      x: area.x + area.width - 684,
      y: area.y,
      width: 684,
      height: area.height,
    });

    layout = arrangeWindow(layout, 'help', 'floating', area);
    expect(frameOf(win(layout, 'help'), area, size)).toEqual(layout.help?.rect);
  });

  it('snaps to the left or right half near the side edges and maximises at the top', () => {
    expect(snapZone({ x: area.x + 3, y: 400 }, area)).toBe('dockLeft');
    expect(snapZone({ x: area.x + area.width - 3, y: 400 }, area)).toBe('dockRight');
    expect(snapZone({ x: 700, y: area.y + 2 }, area)).toBe('maximized');
    expect(snapZone({ x: 700, y: 400 }, area)).toBeNull();
  });

  it('sticks edges to the area and to other windows when they are close, not when they are far', () => {
    const other: Rect = { x: 600, y: 200, width: 300, height: 300 };

    expect(magnet({ x: 70, y: 300, width: 200, height: 200 }, area, [])).toMatchObject({ x: area.x });
    expect(magnet({ x: 905, y: 300, width: 200, height: 200 }, area, [other])).toMatchObject({ x: 900 });
    expect(magnet({ x: 395, y: 300, width: 200, height: 200 }, area, [other])).toMatchObject({ x: 400 });
    expect(magnet({ x: 300, y: 300, width: 200, height: 200 }, area, [other])).toMatchObject({ x: 300 });
  });

  it('resizes from any edge without going under the minimum or out of the area', () => {
    const rect: Rect = { x: 400, y: 200, width: 480, height: 400 };

    expect(resizeRect(rect, { right: true, bottom: true }, 40, 20, area, size)).toEqual({
      ...rect,
      width: 520,
      height: 420,
    });
    expect(resizeRect(rect, { left: true }, 300, 0, area, size)).toEqual({ ...rect, x: 560, width: 320 });
    expect(resizeRect(rect, { top: true }, 0, -500, area, size)).toEqual({ ...rect, y: area.y, height: 540 });
  });
});
