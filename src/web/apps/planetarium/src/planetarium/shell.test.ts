// @vitest-environment jsdom
import type { SkySnapshot } from '@astroplanner/bridge-client';
import { describe, expect, it, vi } from 'vitest';
import { isTextEntry, match, type Shortcut } from './app/shortcuts';
import { neighbourRate } from './app/useSkyShortcuts';
import { siteTime } from './app/format';
import { wallClock } from './shell/TopBar';
import { skyTimeAt } from './shell/useSkyTime';

const sky: SkySnapshot = {
  revision: 1,
  clockUtcMilliseconds: Date.UTC(2026, 0, 16, 4, 0, 0),
  rate: 0,
  isPlaying: false,
  rates: [1, 60, 600, 3600],
  viewMode: 'dome',
  layers: [],
  starLimitMagnitude: 6.5,
  selection: null,
};

function key(init: KeyboardEventInit, target?: EventTarget): KeyboardEvent {
  const event = new KeyboardEvent('keydown', init);
  if (target) {
    Object.defineProperty(event, 'target', { value: target });
  }
  return event;
}

describe('shortcuts', () => {
  const run = vi.fn();
  const shortcuts: Shortcut[] = [
    { group: 'sky', id: 'togglePlay', key: ' ', display: 'Space', run },
    { group: 'sky', id: 'lines', key: 'c', display: 'C', run },
    { group: 'sky', id: 'night', key: 'n', display: 'Ctrl+Shift+N', ctrl: true, shift: true, run },
  ];

  it('match letters in either case, with the exact modifiers', () => {
    expect(match(shortcuts, key({ key: 'C' }))?.id).toBe('lines');
    expect(match(shortcuts, key({ key: 'c', ctrlKey: true }))).toBeUndefined();
    expect(match(shortcuts, key({ key: 'N', ctrlKey: true, shiftKey: true }))?.id).toBe('night');
    expect(match(shortcuts, key({ key: ' ', repeat: true }))).toBeUndefined();
  });

  it('never fire while typing', () => {
    const input = document.createElement('input');
    const checkbox = Object.assign(document.createElement('input'), { type: 'checkbox' });
    const editable = document.createElement('div');
    editable.contentEditable = 'true';
    Object.defineProperty(editable, 'isContentEditable', { value: true });

    expect(isTextEntry(input)).toBe(true);
    expect(isTextEntry(document.createElement('textarea'))).toBe(true);
    expect(isTextEntry(editable)).toBe(true);
    expect(isTextEntry(checkbox)).toBe(false);
    expect(isTextEntry(document.createElement('button'))).toBe(false);
    expect(match(shortcuts, key({ key: 'c' }, input))).toBeUndefined();
  });
});

describe('time', () => {
  it('steps through the rates and stops at both ends', () => {
    expect(neighbourRate(sky, 1)).toBe(600); // paused: from the default ×60
    expect(neighbourRate({ ...sky, isPlaying: true, rate: 3600 }, 1)).toBe(3600);
    expect(neighbourRate({ ...sky, isPlaying: true, rate: 1 }, -1)).toBe(1);
    expect(neighbourRate({ ...sky, isPlaying: true, rate: 600 }, -1)).toBe(60);
  });

  it('advances the clock by the rate only while playing', () => {
    expect(skyTimeAt(sky, 1000, 5000)).toBe(sky.clockUtcMilliseconds);
    expect(skyTimeAt({ ...sky, isPlaying: true, rate: 60 }, 1000, 2000)).toBe(
      sky.clockUtcMilliseconds + 60_000,
    );
  });

  it("shows times in the site's zone", () => {
    expect(siteTime(sky.clockUtcMilliseconds, 'America/Mexico_City')).toBe('22:00');
    expect(siteTime(sky.clockUtcMilliseconds, 'Not/AZone')).toBe('04:00');
    expect(wallClock('2026-01-15T19:39:00')).toBe('19:39');
    expect(wallClock(null)).toBeNull();
  });
});
