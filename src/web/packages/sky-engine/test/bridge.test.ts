import { describe, expect, it, vi } from 'vitest';
import { createBridge, parseHostMessage } from '../src/bridge/bridge';

describe('bridge', () => {
  it('delivers valid host messages and drops invalid ones', () => {
    let listener: ((event: { data: unknown }) => void) | undefined;
    const webview = {
      postMessage: vi.fn(),
      addEventListener: (_: 'message', l: (event: { data: unknown }) => void) => {
        listener = l;
      },
    };
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const bridge = createBridge(webview);
    const received: string[] = [];
    bridge.onMessage((m) => received.push(m.type));

    listener?.({ data: { v: 1, type: 'setTheme', payload: { theme: 'night' } } });
    listener?.({ data: { v: 1, type: 'setTheme', payload: { theme: 'evil' } } });
    listener?.({ data: '{"v":1}' });

    expect(received).toEqual(['setTheme']);
    expect(console.warn).toHaveBeenCalledTimes(2);

    // App channel messages (v2) are left to the app client, without a warning.
    listener?.({ data: { v: 2, kind: 'evt', type: 'theme.changed', payload: { theme: 'night' } } });
    expect(received).toEqual(['setTheme']);
    expect(console.warn).toHaveBeenCalledTimes(2);
    expect(parseHostMessage({ v: 1, type: 'eval', payload: {} })).toBeNull();
  });

  it('accepts comets, asteroids and satellites as moving bodies (M5)', () => {
    const body = (kind: string, sunlit: number[] | null = null) => ({
      id: kind === 'comet' ? 'C/2026 Z9' : '(4)',
      kind,
      name: kind,
      magnitude: 6.2,
      diameterArcsec: 0,
      phaseAngleDegrees: 30,
      illuminatedFraction: 0.93,
      brightLimbAngleDegrees: 250,
      track: { startUtc: '2026-01-16T03:00:00Z', stepSeconds: 600, vectors: [1, 0, 0, 0, 1, 0] },
      sunlit,
    });
    const message = (b: object) => ({ v: 1, type: 'movingBodies', payload: { bodies: [b] } });

    expect(parseHostMessage(message(body('comet')))).not.toBeNull();
    expect(parseHostMessage(message(body('asteroid')))).not.toBeNull();
    expect(parseHostMessage(message(body('satellite', [1, 0])))).not.toBeNull();
    expect(parseHostMessage(message(body('satellite', [1, 2])))).toBeNull();
    expect(parseHostMessage(message(body('rocket')))).toBeNull();
  });

  it('validates outgoing messages before posting', () => {
    const postMessage = vi.fn();
    const bridge = createBridge({ postMessage, addEventListener: () => undefined });
    vi.spyOn(console, 'error').mockImplementation(() => undefined);

    bridge.send('selectObject', { kind: 'dso', id: 'M42' });
    bridge.send('selectObject', { kind: 'dso', id: '' });

    expect(postMessage).toHaveBeenCalledTimes(1);
    expect(postMessage).toHaveBeenCalledWith({
      v: 1,
      type: 'selectObject',
      payload: { kind: 'dso', id: 'M42' },
    });
    expect(bridge.connected).toBe(true);
  });
});
