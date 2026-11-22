// Development only (`npm run dev` in a browser): replays the golden messages written by the C#
// contract tests, so the renderer can be worked on without the WPF host. Never bundled.
import { parseHostMessage } from '../bridge/bridge';
import type { HostMessage } from '../bridge/schema';

const samples = import.meta.glob<unknown>('../../../../../../tests/fixtures/bridge/outgoing/*.json', {
  eager: true,
  import: 'default',
});

export function demoMessages(): HostMessage[] {
  const byType = new Map<string, unknown>();
  for (const [path, json] of Object.entries(samples)) {
    byType.set(path.replace(/^.*\//, '').replace('.json', ''), json);
  }
  const order = ['init', 'setObserver', 'frameTransform', 'setTime', 'movingBodies', 'setHighlights'];
  const messages: HostMessage[] = [];
  for (const type of order) {
    const message = parseHostMessage(byType.get(type));
    if (message?.type === 'init') {
      messages.push({
        ...message,
        payload: {
          ...message.payload,
          dataBaseUrl: new URL(__DEV_SKY_DATA__, window.location.href).toString(),
          showStats: true,
        },
      });
    } else if (message) {
      messages.push(message);
    }
  }
  return messages;
}
