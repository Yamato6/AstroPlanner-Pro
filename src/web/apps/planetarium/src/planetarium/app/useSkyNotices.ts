// Toasts for what the sky does on its own (spec §9.2): tracking stops when the view is moved by hand.
import { skyEvents } from '@astroplanner/sky-engine';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from '../state/toasts';

export function useSkyNotices(): void {
  const { t } = useTranslation();
  useEffect(
    () =>
      skyEvents.on('tracking', (event) => {
        if (!event.active && event.reason === 'manual') {
          toast(t('sky.trackingStopped'));
        }
      }),
    [t],
  );
}
