// A fake C# host for the planetarium page (spec §12): it stands in for window.chrome.webview,
// validates every request against the contracts generated from C#, and answers from the golden
// files the C# tests write (tests/fixtures/bridge/app/outgoing), so the mock cannot drift from
// the real host. Sky commands change its state the way SkyEndpoints does.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import {
  appEvents,
  rpcMethods,
  type EventName,
  type EventOf,
  type MethodName,
  aboutResponseSchema,
  layoutSnapshotSchema,
  observerSnapshotSchema,
  skySnapshotSchema,
  uiSnapshotSchema,
  type LayoutSnapshot,
  type ObserverSnapshot,
  type SkySnapshot,
  type UiSnapshot,
} from '@astroplanner/bridge-client';
import type { Page } from '@playwright/test';

const fixtures = fileURLToPath(
  new URL('../../../../../tests/fixtures/bridge/app/outgoing/', import.meta.url),
);

/** The payload of a golden message (checked by the caller against its contract). */
function goldenPayload(name: string): unknown {
  return (JSON.parse(readFileSync(`${fixtures}${name}.json`, 'utf8')) as { payload: unknown }).payload;
}

/** What the page sees before it asks anything: the WebView2 surface, recording into the host. */
const installScript = () => {
  const listeners: ((event: { data: unknown }) => void)[] = [];
  const violations: string[] = [];
  document.addEventListener('securitypolicyviolation', (event) => {
    violations.push(`${event.violatedDirective} ${event.blockedURI}`);
  });
  Object.assign(window, {
    chrome: {
      webview: {
        postMessage: (message: unknown) => {
          void (window as unknown as { __toHost: (json: string) => Promise<void> }).__toHost(
            JSON.stringify(message),
          );
        },
        addEventListener: (_type: 'message', listener: (event: { data: unknown }) => void) => {
          listeners.push(listener);
        },
        removeEventListener: () => undefined,
      },
    },
    __fromHost: (json: string) => {
      const data: unknown = JSON.parse(json);
      for (const listener of listeners) {
        listener({ data });
      }
    },
    __cspViolations: violations,
  });
};

export interface Request {
  readonly method: MethodName;
  readonly payload: unknown;
}

export class MockHost {
  readonly requests: Request[] = [];
  readonly rejected: string[] = [];
  observer: ObserverSnapshot = observerSnapshotSchema.parse(goldenPayload('observer.get.response'));
  sky: SkySnapshot = skySnapshotSchema.parse(goldenPayload('sky.get.response'));
  ui: UiSnapshot = uiSnapshotSchema.parse(goldenPayload('ui.get.response'));
  /** The saved window layout: empty, like a first start (the golden file has a sample). */
  layout: LayoutSnapshot = { windows: [] };
  readonly openedLinks: string[] = [];
  /** While set, answers wait (the page's loading state); `release()` sends them. */
  private held: (() => Promise<void>)[] | null = null;

  private constructor(private readonly page: Page) {}

  static async attach(page: Page): Promise<MockHost> {
    const host = new MockHost(page);
    await page.exposeFunction('__toHost', (json: string) => host.receive(json));
    await page.addInitScript(installScript);
    return host;
  }

  /** Holds every answer until `release()`. */
  hold(): void {
    this.held ??= [];
  }

  async release(): Promise<void> {
    const replies = this.held ?? [];
    this.held = null;
    for (const reply of replies) {
      await reply();
    }
  }

  /** Requests of one method, in order. */
  calls(method: MethodName): unknown[] {
    return this.requests.filter((r) => r.method === method).map((r) => r.payload);
  }

  async emit<E extends EventName>(event: E, payload: EventOf<E>): Promise<void> {
    appEvents[event].parse(payload);
    await this.send({ v: 2, kind: 'evt', type: event, payload });
  }

  async cspViolations(): Promise<string[]> {
    return this.page.evaluate(() => (window as unknown as { __cspViolations: string[] }).__cspViolations);
  }

  private async receive(json: string): Promise<void> {
    const message = JSON.parse(json) as {
      v?: number;
      kind?: string;
      id?: number;
      type?: string;
      payload?: unknown;
    };
    if (message.v !== 2 || message.kind !== 'req' || typeof message.type !== 'string') {
      return; // sky protocol (v1) and cancels: not this mock's business
    }
    const method = message.type as MethodName;
    if (!Object.hasOwn(rpcMethods, method)) {
      this.rejected.push(`unknown method ${method}`);
      return;
    }
    const checked = rpcMethods[method].request.safeParse(message.payload);
    if (!checked.success) {
      this.rejected.push(`${method}: ${checked.error.message}`);
      return;
    }
    this.requests.push({ method, payload: checked.data });
    const answer = this.answer(method, checked.data);
    rpcMethods[method].response.parse(answer); // the mock must keep the contract too
    const reply = () => this.send({ v: 2, kind: 'res', id: message.id, type: method, payload: answer });
    if (this.held) {
      this.held.push(reply);
    } else {
      await reply();
    }
  }

  private answer(method: MethodName, payload: unknown): unknown {
    switch (method) {
      case 'app.info':
        return { version: '3.0.0-dev', theme: this.ui.theme, skyProtocolVersion: 1, appProtocolVersion: 2 };
      case 'app.about':
        return aboutResponseSchema.parse(goldenPayload('app.about.response'));
      case 'app.openLink':
        this.openedLinks.push((payload as { url: string }).url);
        return { opened: true };
      case 'app.openDataFolder':
        return {};
      case 'ui.get':
        return this.ui;
      case 'ui.setTheme':
        return this.updateUi({ theme: (payload as { theme: UiSnapshot['theme'] }).theme });
      case 'ui.setReduceTransparency':
        return this.updateUi({ reduceTransparency: (payload as { enabled: boolean }).enabled });
      case 'ui.setFullScreen':
        return this.updateUi({ isFullScreen: (payload as { enabled: boolean }).enabled });
      case 'layout.load':
        return this.layout;
      case 'layout.save':
        this.layout = layoutSnapshotSchema.parse(payload);
        return {};
      case 'observer.get':
        return this.observer;
      case 'sky.get':
        return this.sky;
      case 'sky.play':
        return this.update({ isPlaying: true, rate: this.sky.rate || 60 });
      case 'sky.pause':
        return this.update({ isPlaying: false, rate: 0 });
      case 'sky.now':
        return this.update({ isPlaying: false, rate: 0 });
      case 'sky.step': {
        const { minutes } = payload as { minutes: number };
        return this.update({ clockUtcMilliseconds: this.sky.clockUtcMilliseconds + minutes * 60_000 });
      }
      case 'sky.setRate':
        return this.update({ rate: (payload as { rate: number }).rate });
      case 'sky.setLayer': {
        const { key, enabled } = payload as { key: string; enabled: boolean };
        return this.update({ layers: this.sky.layers.map((l) => (l.key === key ? { ...l, enabled } : l)) });
      }
      case 'sky.setViewMode':
        return this.update({ viewMode: (payload as { mode: SkySnapshot['viewMode'] }).mode });
    }
  }

  /** A layout as the golden file has it (Help docked right, Settings minimised). */
  static goldenLayout(): LayoutSnapshot {
    return layoutSnapshotSchema.parse(goldenPayload('layout.load.response'));
  }

  private updateUi(change: Partial<UiSnapshot>): UiSnapshot {
    this.ui = { ...this.ui, ...change, revision: this.ui.revision + 1 };
    return this.ui;
  }

  private update(change: Partial<SkySnapshot>): SkySnapshot {
    this.sky = { ...this.sky, ...change, revision: this.sky.revision + 1 };
    return this.sky;
  }

  private async send(message: unknown): Promise<void> {
    await this.page.evaluate((json) => {
      (window as unknown as { __fromHost: (json: string) => void }).__fromHost(json);
    }, JSON.stringify(message));
  }
}
