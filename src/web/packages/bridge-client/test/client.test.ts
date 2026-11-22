// The web side of the app channel against a fake WebView2 port.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createAppBridge, RpcError, type WebViewPort } from '../src/client';

interface Sent {
  v: number;
  kind: string;
  id: number;
  type?: string;
  payload?: unknown;
}

class FakeWebView implements WebViewPort {
  readonly sent: Sent[] = [];
  private listener: ((event: { readonly data: unknown }) => void) | null = null;

  postMessage(message: unknown): void {
    this.sent.push(message as Sent);
  }

  addEventListener(_type: 'message', listener: (event: { readonly data: unknown }) => void): void {
    this.listener = listener;
  }

  deliver(data: unknown): void {
    this.listener?.({ data });
  }
}

const info = { version: '2.0.0', theme: 'dark', skyProtocolVersion: 1, appProtocolVersion: 2 } as const;

describe('requests', () => {
  let webview: FakeWebView;

  beforeEach(() => {
    webview = new FakeWebView();
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('send a v2 request and resolve with the validated answer', async () => {
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {});

    expect(webview.sent).toEqual([{ v: 2, kind: 'req', id: 1, type: 'app.info', payload: {} }]);
    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'app.info', payload: info });

    await expect(answer).resolves.toEqual(info);
  });

  it('reject with the host error', async () => {
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {});
    webview.deliver({
      v: 2,
      kind: 'res',
      id: 1,
      type: 'app.info',
      error: { code: 'internal', message: 'The request failed.' },
    });

    await expect(answer).rejects.toMatchObject({ code: 'internal', message: 'The request failed.' });
  });

  it('reject an answer that breaks the contract', async () => {
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {});
    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'app.info', payload: { ...info, theme: 'sepia' } });

    await expect(answer).rejects.toMatchObject({ code: 'invalid-response' });
  });

  it('time out, and tell the host to cancel', async () => {
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {}, { timeoutMs: 1000 });
    const outcome = expect(answer).rejects.toMatchObject({ code: 'timeout' });

    vi.advanceTimersByTime(1000);
    await outcome;
    expect(webview.sent[1]).toEqual({ v: 2, kind: 'cancel', id: 1 });

    // A late answer is ignored.
    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'app.info', payload: info });
  });

  it('cancel when the signal aborts', async () => {
    const bridge = createAppBridge(webview);
    const controller = new AbortController();
    const answer = bridge.request('app.info', {}, { signal: controller.signal });

    controller.abort();

    await expect(answer).rejects.toBeInstanceOf(RpcError);
    await expect(answer).rejects.toMatchObject({ code: 'cancelled' });
    expect(webview.sent.at(-1)).toEqual({ v: 2, kind: 'cancel', id: 1 });
  });

  it('refuse parameters outside the contract without sending anything', async () => {
    const bridge = createAppBridge(webview);

    await expect(
      bridge.request('app.info', { extra: 1 } as unknown as Record<string, never>),
    ).rejects.toMatchObject({
      code: 'bad-request',
    });
    expect(webview.sent).toEqual([]);
  });

  it('match answers by id', async () => {
    const bridge = createAppBridge(webview);
    const first = bridge.request('app.info', {});
    const second = bridge.request('app.info', {});

    webview.deliver({ v: 2, kind: 'res', id: 2, type: 'app.info', payload: { ...info, version: '2' } });
    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'app.info', payload: { ...info, version: '1' } });

    await expect(first).resolves.toMatchObject({ version: '1' });
    await expect(second).resolves.toMatchObject({ version: '2' });
  });

  it('fail with no-host outside WebView2', async () => {
    const bridge = createAppBridge(undefined);

    expect(bridge.connected).toBe(false);
    await expect(bridge.request('app.info', {})).rejects.toMatchObject({ code: 'no-host' });
  });

  it('settle and clean up when the request cannot be posted', async () => {
    const bridge = createAppBridge({
      postMessage: () => {
        throw new Error('WebView2 is gone');
      },
      addEventListener: () => undefined,
    });

    await expect(bridge.request('app.info', {})).rejects.toMatchObject({ code: 'no-host' });
    expect(vi.getTimerCount()).toBe(0);
  });

  it('still reject on timeout when the cancel cannot be posted', async () => {
    let calls = 0;
    const bridge = createAppBridge({
      postMessage: () => {
        calls += 1;
        if (calls > 1) {
          throw new Error('WebView2 is gone');
        }
      },
      addEventListener: () => undefined,
    });
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const answer = bridge.request('app.info', {}, { timeoutMs: 100 });
    const outcome = expect(answer).rejects.toMatchObject({ code: 'timeout' });

    vi.advanceTimersByTime(100);

    await outcome;
    expect(calls).toBe(2);
    warn.mockRestore();
  });

  it('send nothing for an already aborted signal', async () => {
    const bridge = createAppBridge(webview);

    await expect(bridge.request('app.info', {}, { signal: AbortSignal.abort() })).rejects.toMatchObject({
      code: 'cancelled',
    });
    expect(webview.sent).toEqual([]);
    expect(vi.getTimerCount()).toBe(0);
  });

  it('ignore an answer of another method, and clear the timer once answered', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {});

    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'plan.schedule', payload: info });
    expect(warn).toHaveBeenCalledTimes(1);
    webview.deliver({ v: 2, kind: 'res', id: 1, type: 'app.info', payload: info });

    await expect(answer).resolves.toEqual(info);
    expect(vi.getTimerCount()).toBe(0);
    warn.mockRestore();
  });

  it('accept the busy error of the host', async () => {
    const bridge = createAppBridge(webview);
    const answer = bridge.request('app.info', {});
    webview.deliver({
      v: 2,
      kind: 'res',
      id: 1,
      type: 'app.info',
      error: { code: 'busy', message: 'Retry later.' },
    });

    await expect(answer).rejects.toMatchObject({ code: 'busy' });
  });
});

describe('events', () => {
  it('reach the subscribers until they unsubscribe', () => {
    const webview = new FakeWebView();
    const bridge = createAppBridge(webview);
    const themes: string[] = [];
    const unsubscribe = bridge.on('theme.changed', (event) => themes.push(event.theme));

    webview.deliver({ v: 2, kind: 'evt', type: 'theme.changed', payload: { theme: 'night' } });
    unsubscribe();
    webview.deliver({ v: 2, kind: 'evt', type: 'theme.changed', payload: { theme: 'dark' } });

    expect(themes).toEqual(['night']);
  });

  it('reach every subscriber even if one of them throws', () => {
    const webview = new FakeWebView();
    const bridge = createAppBridge(webview);
    const error = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const second = vi.fn();
    bridge.on('theme.changed', () => {
      throw new Error('broken subscriber');
    });
    bridge.on('theme.changed', second);

    webview.deliver({ v: 2, kind: 'evt', type: 'theme.changed', payload: { theme: 'night' } });

    expect(second).toHaveBeenCalledWith({ theme: 'night' });
    expect(error).toHaveBeenCalledTimes(1);
    error.mockRestore();
  });

  it('are dropped when they break the contract, and sky messages are ignored', () => {
    const webview = new FakeWebView();
    const bridge = createAppBridge(webview);
    const handler = vi.fn();
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    bridge.on('theme.changed', handler);

    webview.deliver({ v: 2, kind: 'evt', type: 'theme.changed', payload: { theme: 'sepia' } });
    webview.deliver({ v: 1, type: 'setTheme', payload: { theme: 'night' } });

    expect(handler).not.toHaveBeenCalled();
    warn.mockRestore();
  });
});
