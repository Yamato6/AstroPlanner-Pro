// The sky's keyboard map (spec §3.6) on the sky.* endpoints. The engine's own keys are off on
// this page (mountSky keyboard: false): this registry owns the keyboard.
import type { SkySnapshot } from '@astroplanner/bridge-client';
import { skyControl } from '@astroplanner/sky-engine';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { toast } from '../state/toasts';
import { useUi } from '../state/ui';
import { useSkyCommand, useSnapshot } from './hostQueries';
import type { Shortcut } from './shortcuts';

type LayerKey = SkySnapshot['layers'][number]['key'];

/** The rate before or after the current one (×1 → ×60 → ×600 → ×3600). */
export function neighbourRate(sky: SkySnapshot, direction: -1 | 1): number {
  const rates = sky.rates;
  const current = sky.isPlaying ? sky.rate : (rates[1] ?? rates[0] ?? 60);
  const index = rates.indexOf(current);
  const next = rates[Math.min(rates.length - 1, Math.max(0, (index < 0 ? 0 : index) + direction))];
  return next ?? current;
}

/** Starts or stops tracking the selection (spec §9.2); says so when there is nothing to track. */
export function toggleTracking(nothingToTrack: string): void {
  if (skyControl.isTracking) {
    skyControl.setTracking(false);
  } else if (!skyControl.setTracking(true)) {
    toast(nothingToTrack);
  }
}

/** The sky's shortcuts (installed by useAppShortcuts with the others). */
export function useSkyShortcuts(): readonly Shortcut[] {
  const { data: sky } = useSnapshot('sky.get');
  const play = useSkyCommand('sky.play');
  const pause = useSkyCommand('sky.pause');
  const now = useSkyCommand('sky.now');
  const setRate = useSkyCommand('sky.setRate');
  const setLayer = useSkyCommand('sky.setLayer');
  const setViewMode = useSkyCommand('sky.setViewMode');
  const toggleImmersive = useUi((state) => state.toggleImmersive);
  const { t } = useTranslation();

  const shortcuts = useMemo((): Shortcut[] => {
    const toggleLayer = (key: LayerKey): void => {
      const layer = sky?.layers.find((l) => l.key === key);
      if (layer) {
        setLayer.mutate({ key, enabled: !layer.enabled });
      }
    };
    const changeRate = (direction: -1 | 1): void => {
      if (sky) {
        setRate.mutate({ rate: neighbourRate(sky, direction) });
      }
    };
    return [
      {
        id: 'togglePlay',
        group: 'time',
        key: ' ',
        display: 'Space',
        run: () => {
          (sky?.isPlaying ? pause : play).mutate({});
        },
      },
      {
        id: 'now',
        group: 'time',
        key: 'F5',
        display: 'F5',
        run: () => {
          now.mutate({});
        },
      },
      {
        id: 'slower',
        group: 'time',
        key: 'j',
        display: 'J',
        run: () => {
          changeRate(-1);
        },
      },
      {
        id: 'faster',
        group: 'time',
        key: 'l',
        display: 'L',
        run: () => {
          changeRate(1);
        },
      },
      {
        id: 'dome',
        group: 'sky',
        key: '1',
        display: '1',
        run: () => {
          setViewMode.mutate({ mode: 'dome' });
        },
      },
      {
        id: 'allSky',
        group: 'sky',
        key: '2',
        display: '2',
        run: () => {
          setViewMode.mutate({ mode: 'allSky' });
        },
      },
      {
        id: 'finder',
        group: 'sky',
        key: '3',
        display: '3',
        run: () => {
          setViewMode.mutate({ mode: 'finder' });
        },
      },
      {
        id: 'constellationLines',
        group: 'sky',
        key: 'c',
        display: 'C',
        run: () => {
          toggleLayer('constellationLines');
        },
      },
      {
        id: 'constellationBoundaries',
        group: 'sky',
        key: 'b',
        display: 'B',
        run: () => {
          toggleLayer('constellationBoundaries');
        },
      },
      {
        id: 'constellationNames',
        group: 'sky',
        key: 'n',
        display: 'N',
        run: () => {
          toggleLayer('constellationNames');
        },
      },
      {
        id: 'altAzGrid',
        group: 'sky',
        key: 'g',
        display: 'G',
        run: () => {
          toggleLayer('altAzGrid');
        },
      },
      {
        id: 'equatorialGrid',
        group: 'sky',
        key: 'e',
        display: 'E',
        run: () => {
          toggleLayer('equatorialGrid');
        },
      },
      {
        id: 'atmosphere',
        group: 'sky',
        key: 'a',
        display: 'A',
        run: () => {
          toggleLayer('skyBrightness');
        },
      },
      {
        id: 'track',
        group: 'sky',
        key: 't',
        display: 'T',
        run: () => {
          toggleTracking(t('sky.nothingToTrack'));
        },
      },
      {
        id: 'home',
        group: 'sky',
        key: 'Home',
        display: 'Home',
        run: () => {
          skyControl.home();
        },
      },
      { id: 'immersive', group: 'interface', key: 'h', display: 'H', run: toggleImmersive },
    ];
  }, [sky, play, pause, now, setRate, setLayer, setViewMode, toggleImmersive, t]);

  return shortcuts;
}
