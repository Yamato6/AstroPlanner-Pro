// Toasts for what the sky does on its own: tracking stops when the view is moved by hand (spec
// §9.2), and the atmosphere gets cheaper when frames are slow (spec §7.4).
import { skyEvents } from '@astroplanner/sky-engine';
import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from '../state/toasts';

export function useSkyNotices(): void {
  const { t } = useTranslation();
  useEffect(() => {
    const stopTracking = skyEvents.on('tracking', (event) => {
      if (!event.active && event.reason === 'manual') {
        toast(t('sky.trackingStopped'));
      }
    });
    const stopQuality = skyEvents.on('quality', (event) => {
      toast(t('sky.qualityLowered', { quality: t(`settings.quality.${event.quality}`) }));
    });
    return () => {
      stopTracking();
      stopQuality();
    };
  }, [t]);
}
