// Web side of the app channel (bridge v2, docs/bridge.md): typed requests with timeouts and
// cancellation, and typed events. Every answer and event is validated against the contracts
// generated from C# before it reaches the UI; anything else is dropped.
import type { z } from 'zod';
import { parseHostAppMessage, type HostEvent, type HostResponse } from './envelope';
import {
  APP_LIMITS,
  APP_PROTOCOL_VERSION,
  appEvents,
  rpcMethods,
  type RPC_ERROR_CODES,
} from './generated/contracts';

export type MethodName = keyof typeof rpcMethods;
export type EventName = keyof typeof appEvents;
export type RequestOf<M extends MethodName> = z.input<(typeof rpcMethods)[M]['request']>;
export type ResponseOf<M extends MethodName> = z.output<(typeof rpcMethods)[M]['response']>;
export type EventOf<E extends EventName> = z.output<(typeof appEvents)[E]>;

/** Host error codes, plus the failures detected on this side. */
export type RpcErrorCode = (typeof RPC_ERROR_CODES)[number] | 'no-host' | 'invalid-response';

/** A request that did not succeed. */
export class RpcError extends Error {
  constructor(
    readonly code: RpcErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'RpcError';
  }
}

/** The part of `window.chrome.webview` the channel uses. */
export interface WebViewPort {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { readonly data: unknown }) => void): void;
}

export interface RequestOptions {
  /** Give up (and tell the host to cancel) after this long. Default 15 s. */
  readonly timeoutMs?: number;
  /** Aborting cancels the request on both sides. */
  readonly signal?: AbortSignal;
}

export interface AppBridge {
  /** False outside WebView2 (`npm run dev` in a browser): requests then fail with `no-host`. */
  readonly connected: boolean;
  request<M extends MethodName>(
    method: M,
    params: RequestOf<M>,
    options?: RequestOptions,
  ): Promise<ResponseOf<M>>;
  /** Subscribes to an event; returns the unsubscribe function. */
  on<E extends EventName>(event: E, handler: (payload: EventOf<E>) => void): () => void;
}

const DEFAULT_TIMEOUT_MS = 15_000;

interface Pending {
  readonly method: MethodName;
  readonly resolve: (value: unknown) => void;
  readonly reject: (error: RpcError) => void;
  readonly cleanup: () => void;
}

function hostWebView(): WebViewPort | undefined {
  return (globalThis as { chrome?: { webview?: WebViewPort } }).chrome?.webview;
}

export function createAppBridge(webview: WebViewPort | undefined = hostWebView()): AppBridge {
  const pending = new Map<number, Pending>();
  const handlers = new Map<EventName, Set<(payload: unknown) => void>>();
  let nextId = 1;

  function onResponse(message: HostResponse): void {
    const call = pending.get(message.id);
    if (!call || call.method !== message.type) {
      return;
    }
    pending.delete(message.id);
    call.cleanup();
    if ('error' in message) {
      call.reject(new RpcError(message.error.code, message.error.message));
      return;
    }
    const result = rpcMethods[call.method].response.safeParse(message.payload);
    if (result.success) {
      call.resolve(result.data);
    } else {
      call.reject(new RpcError('invalid-response', `The answer to '${call.method}' breaks its contract.`));
    }
  }

  function onEvent(message: HostEvent): void {
    if (!(message.type in appEvents)) {
      console.warn('app bridge: unknown event', message.type);
      return;
    }
    const name = message.type as EventName;
    const result = appEvents[name].safeParse(message.payload);
    if (!result.success) {
      console.warn('app bridge: rejected event', name, result.error.issues[0]?.message);
      return;
    }
    for (const handler of handlers.get(name) ?? []) {
      handler(result.data);
    }
  }

  webview?.addEventListener('message', (event) => {
    const message = parseHostAppMessage(event.data);
    if (message?.kind === 'res') {
      onResponse(message);
    } else if (message?.kind === 'evt') {
      onEvent(message);
    }
  });

  function request<M extends MethodName>(
    method: M,
    params: RequestOf<M>,
    options: RequestOptions = {},
  ): Promise<ResponseOf<M>> {
    if (!webview) {
      return Promise.reject(new RpcError('no-host', 'Not running inside the app.'));
    }
    const checked = rpcMethods[method].request.safeParse(params);
    if (!checked.success) {
      return Promise.reject(new RpcError('bad-request', `The parameters of '${method}' break its contract.`));
    }
    const id = nextId++;
    const message = { v: APP_PROTOCOL_VERSION, kind: 'req', id, type: method, payload: checked.data };
    if (JSON.stringify(message).length > APP_LIMITS.maxRequestLength) {
      return Promise.reject(new RpcError('too-large', `The request '${method}' is too large.`));
    }

    return new Promise<ResponseOf<M>>((resolve, reject) => {
      const giveUp = (code: 'timeout' | 'cancelled', text: string): void => {
        if (pending.delete(id)) {
          cleanup();
          webview.postMessage({ v: APP_PROTOCOL_VERSION, kind: 'cancel', id });
          reject(new RpcError(code, text));
        }
      };
      const timer = setTimeout(() => {
        giveUp('timeout', `'${method}' did not answer in time.`);
      }, options.timeoutMs ?? DEFAULT_TIMEOUT_MS);
      const onAbort = (): void => {
        giveUp('cancelled', `'${method}' was cancelled.`);
      };
      const cleanup = (): void => {
        clearTimeout(timer);
        options.signal?.removeEventListener('abort', onAbort);
      };
      if (options.signal?.aborted) {
        cleanup();
        reject(new RpcError('cancelled', `'${method}' was cancelled.`));
        return;
      }
      options.signal?.addEventListener('abort', onAbort);
      pending.set(id, { method, resolve: resolve as (value: unknown) => void, reject, cleanup });
      webview.postMessage(message);
    });
  }

  function on<E extends EventName>(event: E, handler: (payload: EventOf<E>) => void): () => void {
    const set = handlers.get(event) ?? new Set();
    handlers.set(event, set);
    const wrapped = handler as (payload: unknown) => void;
    set.add(wrapped);
    return () => {
      set.delete(wrapped);
    };
  }

  return { connected: webview !== undefined, request, on };
}
