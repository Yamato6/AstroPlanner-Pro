// The interface state the host keeps (ui.get / ui.changed): night vision and reduce transparency
// become attributes of <html>, which the tokens read; nothing re-renders.
import { useEffect } from 'react';
import { useSnapshot } from './hostQueries';

export function useHostInterface(): void {
  const { data: ui } = useSnapshot('ui.get');

  useEffect(() => {
    if (!ui) {
      return;
    }
    const root = document.documentElement;
    root.dataset.theme = ui.theme;
    if (ui.reduceTransparency) {
      root.dataset.transparency = 'reduced';
    } else {
      delete root.dataset.transparency;
    }
  }, [ui]);
}
