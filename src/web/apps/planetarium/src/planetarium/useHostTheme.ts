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
    host
      .request('app.info', {}, { signal: controller.signal })
      .then((info) => {
        apply(info.theme);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('app.info failed', error);
        }
      });
    const unsubscribe = host.on('theme.changed', (event) => {
      apply(event.theme);
    });
    return () => {
      controller.abort();
      unsubscribe();
    };
  }, []);
}
