// Transport over WebView2 (window.chrome.webview). Incoming messages are validated with zod and
// dropped when invalid; outgoing ones are validated too so that a bug never reaches C#.
import {
  hostMessageSchema,
  PROTOCOL_VERSION,
  rendererMessageSchema,
  type HostMessage,
  type RendererMessage,
} from './schema';

interface WebViewMessageEvent {
  readonly data: unknown;
}

interface WebView {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: WebViewMessageEvent) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView };
  }
}

export type HostMessageHandler = (message: HostMessage) => void;

type Payload<T extends RendererMessage['type']> = Extract<RendererMessage, { type: T }>['payload'];

export interface Bridge {
  readonly connected: boolean;
  onMessage(handler: HostMessageHandler): void;
  send<T extends RendererMessage['type']>(type: T, payload: Payload<T>): void;
}

/** True for messages of the app channel (bridge v2, `v: 2`), which the sky does not handle. */
export function isAppChannelMessage(data: unknown): boolean {
  return typeof data === 'object' && data !== null && (data as { v?: unknown }).v === 2;
}

/** Parses and validates a host message; returns null (and logs) when it is not acceptable. */
export function parseHostMessage(data: unknown): HostMessage | null {
  const result = hostMessageSchema.safeParse(data);
  if (!result.success) {
    console.warn('sky bridge: rejected message', result.error.issues[0]?.message);
    return null;
  }
  return result.data;
}

export function createBridge(webview: WebView | undefined = window.chrome?.webview): Bridge {
  const handlers: HostMessageHandler[] = [];
  webview?.addEventListener('message', (event) => {
    // The app channel (v2) shares the pipe; its own client handles those messages.
    if (isAppChannelMessage(event.data)) {
      return;
    }
    const message = parseHostMessage(event.data);
    if (message) {
      for (const handler of handlers) {
        handler(message);
      }
    }
  });

  return {
    connected: webview !== undefined,
    onMessage(handler) {
      handlers.push(handler);
    },
    send(type, payload) {
      const message = { v: PROTOCOL_VERSION, type, payload };
      const checked = rendererMessageSchema.safeParse(message);
      if (!checked.success) {
        console.error('sky bridge: refusing to send an invalid message', type, checked.error.issues);
        return;
      }
      webview?.postMessage(checked.data);
    },
  };
}
