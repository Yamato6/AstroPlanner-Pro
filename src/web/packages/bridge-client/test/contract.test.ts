// Contract tests of the app channel: the golden files of tests/fixtures/bridge/app, shared with C#.
import { describe, expect, it } from 'vitest';
import { parseHostAppMessage, webRequestSchema } from '../src/envelope';
import { appEvents, rpcMethods } from '../src/generated/contracts';
import { jsonFiles, readJson } from './fixtures';

describe('host to web messages (written by C#)', () => {
  it.each(jsonFiles('outgoing'))('%s matches its contract', (file) => {
    const message = parseHostAppMessage(readJson(file));
    expect(message, file).not.toBeNull();
    if (message?.kind === 'res' && 'payload' in message) {
      const method = rpcMethods[message.type as keyof typeof rpcMethods];
      expect(method.response.safeParse(message.payload).success).toBe(true);
    } else if (message?.kind === 'evt') {
      const schema = appEvents[message.type as keyof typeof appEvents];
      expect(schema.safeParse(message.payload).success).toBe(true);
    }
  });

  it('cover a response, an error and every event', () => {
    const messages = jsonFiles('outgoing').map((file) => parseHostAppMessage(readJson(file)));
    expect(messages.some((m) => m?.kind === 'res' && 'payload' in m)).toBe(true);
    expect(messages.some((m) => m?.kind === 'res' && 'error' in m)).toBe(true);
    const events = new Set(messages.flatMap((m) => (m?.kind === 'evt' ? [m.type] : [])));
    expect([...events].sort()).toEqual(Object.keys(appEvents).sort());
  });
});

describe('web to host messages', () => {
  it.each(jsonFiles('incoming'))('%s is accepted', (file) => {
    expect(webRequestSchema.safeParse(readJson(file)).success).toBe(true);
  });

  it.each(jsonFiles('invalid'))('%s is rejected', (file) => {
    expect(webRequestSchema.safeParse(readJson(file)).success).toBe(false);
  });
});

describe('the sky protocol', () => {
  it('is not mistaken for the app channel', () => {
    expect(parseHostAppMessage({ v: 1, type: 'setTheme', payload: { theme: 'dark' } })).toBeNull();
  });
});
