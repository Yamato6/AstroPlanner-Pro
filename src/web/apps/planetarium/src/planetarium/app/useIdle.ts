// "Calm by default" (spec §3.1): the toolbars fade after a few seconds without input and come
// back with the first movement or key. Never while the keyboard focus is in one of them.
import { useEffect, useState } from 'react';

export const IDLE_MS = 3000;

/** Elements marked with this attribute keep the toolbars awake while they hold the focus. */
export const TOOLBAR_ATTRIBUTE = 'data-toolbar';

export function useIdle(enabled: boolean): boolean {
  const [idle, setIdle] = useState(false);

  useEffect(() => {
    if (!enabled) {
      setIdle(false);
      return undefined;
    }
    let timer = 0;
    const arm = (): void => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => {
        const focused = document.activeElement;
        if (focused instanceof Element && focused.closest(`[${TOOLBAR_ATTRIBUTE}]`)) {
          arm(); // still working in a toolbar
          return;
        }
        setIdle(true);
      }, IDLE_MS);
    };
    const wake = (): void => {
      setIdle(false);
      arm();
    };
    const events = ['pointermove', 'pointerdown', 'keydown', 'wheel', 'focusin'] as const;
    for (const name of events) {
      window.addEventListener(name, wake, { passive: true });
    }
    arm();
    return () => {
      window.clearTimeout(timer);
      for (const name of events) {
        window.removeEventListener(name, wake);
      }
    };
  }, [enabled]);

  return idle;
}
