// The window layout survives a restart (spec §3.3): layout.load once at start, layout.save a
// moment after the last change (a drag is one save, not one per frame).
import { useEffect } from 'react';
import { host } from '../host';
import { toSaved, useWindows } from '../state/windows';

export const SAVE_DELAY_MS = 300;

export function useLayoutPersistence(): void {
  useEffect(() => {
    const { hydrate } = useWindows.getState();
    if (!host.connected) {
      hydrate([]); // windows still work, they are just not kept
      return undefined;
    }

    const controller = new AbortController();
    host
      .request('layout.load', {}, { signal: controller.signal })
      .then((saved) => {
        hydrate(saved.windows);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('layout.load failed', error);
          hydrate([]);
        }
      });

    let timer: number | undefined;
    const unsubscribe = useWindows.subscribe((state, previous) => {
      if (!previous.hydrated || state.layout === previous.layout) {
        return; // the saved layout arriving is not a change to save
      }
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        host
          .request('layout.save', { windows: toSaved(useWindows.getState().layout) })
          .catch((error: unknown) => {
            console.warn('layout.save failed', error);
          });
      }, SAVE_DELAY_MS);
    });

    return () => {
      controller.abort();
      unsubscribe();
      window.clearTimeout(timer);
    };
  }, []);
}
