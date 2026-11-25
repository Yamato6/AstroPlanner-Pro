import { layoutSnapshotSchema } from '@astroplanner/bridge-client';
import { beforeEach, describe, expect, it } from 'vitest';
import { fromSaved, toSaved, useWindows } from './state/windows';
import { windowArea } from './windows/WindowLayer';

describe('window layout', () => {
  beforeEach(() => {
    useWindows.setState({ layout: {}, hydrated: false, area: windowArea(1440, 900) });
  });

  it('round-trips through the layout.save contract', () => {
    const { open, arrange, minimize } = useWindows.getState();
    open('settings');
    open('help');
    arrange('help', 'dockRight');
    minimize('settings');

    const saved = toSaved(useWindows.getState().layout);

    expect(layoutSnapshotSchema.safeParse({ windows: saved }).success).toBe(true);
    expect(fromSaved(saved)).toEqual(useWindows.getState().layout);
    expect(saved.find((w) => w.id === 'help')).toMatchObject({ arrangement: 'dockRight', dockWidth: 684 });
  });

  it('keeps values inside the contract whatever the page computed', () => {
    useWindows.setState({
      layout: {
        help: {
          id: 'help',
          isOpen: true,
          isMinimized: false,
          arrangement: 'floating',
          rect: { x: -1e6, y: 12.6, width: 0, height: 1e9 },
          dockWidth: 0.4,
          zOrder: 20000,
        },
      },
    });

    const [help] = toSaved(useWindows.getState().layout);

    expect(help).toMatchObject({ x: -20000, y: 13, width: 1, height: 20000, dockWidth: 1, zOrder: 10000 });
  });

  it('drops saved windows this page does not have, and keeps windows opened before the layout arrived', () => {
    useWindows.getState().open('settings');

    useWindows.getState().hydrate([
      {
        id: 'help',
        isOpen: true,
        isMinimized: false,
        arrangement: 'floating',
        x: 100,
        y: 100,
        width: 400,
        height: 300,
        dockWidth: 360,
        zOrder: 1,
      },
      {
        id: 'settings',
        isOpen: false,
        isMinimized: false,
        arrangement: 'floating',
        x: 0,
        y: 0,
        width: 400,
        height: 300,
        dockWidth: 360,
        zOrder: 2,
      },
    ]);

    const { layout, hydrated } = useWindows.getState();
    expect(hydrated).toBe(true);
    expect(layout.help?.rect).toEqual({ x: 100, y: 100, width: 400, height: 300 });
    expect(layout.settings?.isOpen).toBe(true);
    const unknown = toSaved(layout).map((w) => ({ ...w, id: 'telescope' as 'help' }));
    expect(fromSaved(unknown)).toEqual({});
  });

  it('gives windows the viewport less the top bar and the rail', () => {
    expect(windowArea(1440, 900)).toEqual({ x: 64, y: 60, width: 1368, height: 832 });
    expect(windowArea(40, 40)).toEqual({ x: 64, y: 60, width: 0, height: 0 });
  });
});
