import { useEffect } from 'react';
import { host } from './host';

/**
 * Keeps `<html data-theme>` on the host's theme (dark or night vision): read once with
 * `app.info`, then followed through `theme.changed`. The CSS reads the attribute; nothing
 * re-renders.
 */
export function useHostTheme(): void {
  useEffect(() => {
    if (!host.connected) {
      return undefined;
    }
    const apply = (theme: string): void => {
      document.documentElement.dataset.theme = theme;
    };
    const controller = new AbortController();
    // An event is newer than any app.info snapshot: once one arrives, the snapshot is ignored.
    let eventSeen = false;
    host
      .request('app.info', {}, { signal: controller.signal })
      .then((info) => {
        if (!eventSeen) {
          apply(info.theme);
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('app.info failed', error);
        }
      });
    const unsubscribe = host.on('theme.changed', (event) => {
      eventSeen = true;
      apply(event.theme);
    });
    return () => {
      controller.abort();
      unsubscribe();
    };
  }, []);
}
