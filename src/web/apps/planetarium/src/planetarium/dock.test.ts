// @vitest-environment jsdom
import type { NightTimeline } from '@astroplanner/bridge-client';
import { describe, expect, it } from 'vitest';
import { formatDec, formatDegrees, formatRa, formatRaDec, siteDate } from './app/format';
import { isOwnedByWidget, match, type Shortcut } from './app/shortcuts';
import {
  covers,
  darknessOf,
  darknessRuns,
  hourTicks,
  instantAt,
  minutesAt,
  moonRuns,
  runs,
  spanMinutes,
} from './shell/darkness';
import { stepMinutes } from './shell/Dock';

// 18:30 in Mexico City (UTC−6) on 2026-01-15, every 10 min.
const start = Date.UTC(2026, 0, 16, 0, 30);
const timeline = (sun: number[], moon: number[] = sun.map(() => -10)): NightTimeline => ({
  revision: 1,
  startUtcMilliseconds: start,
  stepMinutes: 10,
  sunAltitudeDegrees: sun,
  moonAltitudeDegrees: moon,
  moonIlluminatedPercent: 40,
  cloudPercent: [],
});

describe('the darkness bar', () => {
  it('names the darkness of every Sun altitude', () => {
    expect([5, 0, -3, -6, -9, -12, -15, -18, -30].map(darknessOf)).toEqual([
      'day',
      'civil',
      'civil',
      'nautical',
      'nautical',
      'astronomical',
      'astronomical',
      'night',
      'night',
    ]);
  });

  it('merges equal neighbours into runs', () => {
    expect(runs(['a', 'a', 'b', 'a'])).toEqual([
      { start: 0, end: 2, value: 'a' },
      { start: 2, end: 3, value: 'b' },
      { start: 3, end: 4, value: 'a' },
    ]);
    expect(runs([])).toEqual([]);
  });

  it('draws daylight, twilights and night, and the Moon while it is up', () => {
    const night = timeline([4, -3, -8, -14, -20, -20, -14, -3, 4], [-5, -1, 2, 6, 9, 3, -2, -6, -9]);

    expect(darknessRuns(night).map((r) => r.value)).toEqual([
      'day',
      'civil',
      'nautical',
      'astronomical',
      'night',
      'astronomical',
      'civil',
      'day',
    ]);
    expect(moonRuns(night)).toEqual([{ start: 2, end: 6, value: true }]);
  });

  it('maps instants to minutes on the bar and back, kept on it', () => {
    const night = timeline(Array.from({ length: 80 }, () => -20));

    expect(spanMinutes(night)).toBe(790);
    expect(minutesAt(night, start + 210 * 60_000)).toBe(210);
    expect(minutesAt(night, start - 3_600_000)).toBe(0);
    expect(minutesAt(night, start + 10_000 * 60_000)).toBe(790);
    expect(instantAt(night, 220)).toBe(start + 220 * 60_000);
    expect(covers(night, start + 100 * 60_000)).toBe(true);
    expect(covers(night, start + 1000 * 60_000)).toBe(false);
  });

  it('marks every other whole hour of the site clock', () => {
    const night = timeline(Array.from({ length: 80 }, () => -20)); // 18:30 → 07:40

    expect(hourTicks(night, 'America/Mexico_City').map((t) => t.label)).toEqual([
      '20:00',
      '22:00',
      '00:00',
      '02:00',
      '04:00',
      '06:00',
    ]);
    expect(hourTicks(night, 'America/Mexico_City')[0]?.minutes).toBe(90);
  });
});

describe('the dock and the HUD', () => {
  it('steps 10 min, an hour with Shift, a day with Ctrl and a minute with Alt', () => {
    expect(stepMinutes({ shiftKey: false, ctrlKey: false, altKey: false })).toBe(10);
    expect(stepMinutes({ shiftKey: true, ctrlKey: false, altKey: false })).toBe(60);
    expect(stepMinutes({ shiftKey: false, ctrlKey: true, altKey: false })).toBe(1440);
    expect(stepMinutes({ shiftKey: false, ctrlKey: false, altKey: true })).toBe(1);
  });

  it('formats coordinates and angles', () => {
    expect(formatRa(5.5881)).toBe('05h 35m 17s');
    expect(formatRa(23.99999)).toBe('00h 00m 00s');
    expect(formatDec(-5.391)).toBe('−05° 23′ 28″');
    expect(formatDec(-0.00001)).toBe('+00° 00′ 00″');
    expect(formatDegrees(-1.25)).toBe('−1.3°');
    expect(formatRaDec(5.5881, -5.391)).toBe('RA 05h 35m 17s, Dec −05° 23′ 28″');
    expect(siteDate(start + 210 * 60_000, 'America/Mexico_City')).toBe('Thu 15 Jan');
  });
});

describe('shortcut ownership', () => {
  const run = (): void => undefined;
  const shortcuts: Shortcut[] = [
    { group: 'interface', id: 'search', key: '/', display: '/', run },
    { group: 'sky', id: 'names', key: 'n', display: 'N', run },
  ];

  it('takes a symbol whatever Shift is (Spanish "/" is Shift+7), but not a letter with Shift', () => {
    expect(match(shortcuts, new KeyboardEvent('keydown', { key: '/', shiftKey: true }))?.id).toBe('search');
    expect(match(shortcuts, new KeyboardEvent('keydown', { key: 'N', shiftKey: true }))).toBeUndefined();
  });

  it('leaves keys to menus, lists and modal dialogs', () => {
    const menu = document.createElement('div');
    menu.setAttribute('role', 'menu');
    const item = document.createElement('div');
    menu.append(item);
    document.body.append(menu);
    const event = new KeyboardEvent('keydown', { key: 'n', bubbles: true });
    let matched: Shortcut | undefined;
    document.body.addEventListener('keydown', (e) => (matched = match(shortcuts, e)), { once: true });

    item.dispatchEvent(event);

    expect(isOwnedByWidget(item)).toBe(true);
    expect(matched).toBeUndefined();
    menu.remove();
  });

  it('leaves a key something else already handled', () => {
    const event = new KeyboardEvent('keydown', { key: 'n', cancelable: true });
    event.preventDefault();
    expect(match(shortcuts, event)).toBeUndefined();
  });
});
