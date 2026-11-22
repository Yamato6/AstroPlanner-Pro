// Envelopes of the app channel (bridge v2, docs/bridge.md). The payload contracts are generated
// from C# (generated/contracts.ts); these wrappers are fixed and checked against the same golden
// files as the C# codec (tests/fixtures/bridge/app).
import { z } from 'zod';
import { APP_PROTOCOL_VERSION, rpcErrorPayloadSchema } from './generated/contracts';

// No JIT: zod would otherwise probe `Function('')`, which the CSP (no unsafe-eval) forbids.
z.config({ jitless: true });

const version = z.literal(APP_PROTOCOL_VERSION);
const id = z.int().min(1).max(2_147_483_647);
const methodName = z
  .string()
  .max(64)
  .regex(/^[a-z]+(\.[a-z][a-zA-Z]*)+$/);

/** What the web UI sends: a request or a cancellation (the C# codec accepts exactly these). */
export const webRequestSchema = z.union([
  z.strictObject({
    v: version,
    kind: z.literal('req'),
    id,
    type: methodName,
    payload: z.record(z.string(), z.unknown()),
  }),
  z.strictObject({ v: version, kind: z.literal('cancel'), id }),
]);

const responseSchema = z.union([
  z.strictObject({ v: version, kind: z.literal('res'), id, type: methodName, payload: z.unknown() }),
  z.strictObject({ v: version, kind: z.literal('res'), id, type: methodName, error: rpcErrorPayloadSchema }),
]);

const eventSchema = z.strictObject({
  v: version,
  kind: z.literal('evt'),
  type: methodName,
  payload: z.unknown(),
});

export type HostResponse = z.infer<typeof responseSchema>;
export type HostEvent = z.infer<typeof eventSchema>;

/**
 * Parses a host message of the app channel: a response or an event, with its payload still
 * unchecked (the caller knows which contract applies). Null for anything else, including the sky
 * protocol's v1 messages, which share the WebView2 pipe.
 */
export function parseHostAppMessage(data: unknown): HostResponse | HostEvent | null {
  if (typeof data !== 'object' || data === null || (data as { v?: unknown }).v !== APP_PROTOCOL_VERSION) {
    return null;
  }
  const response = responseSchema.safeParse(data);
  if (response.success) {
    return response.data;
  }
  const event = eventSchema.safeParse(data);
  return event.success ? event.data : null;
}
