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
    expect(parseHostMessage({ v: 1, type: 'eval', payload: {} })).toBeNull();
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
