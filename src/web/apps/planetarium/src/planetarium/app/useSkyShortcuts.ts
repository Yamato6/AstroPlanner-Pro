// The sky's keyboard map (spec §3.6) on the sky.* endpoints. The engine's own keys are off on
// this page (mountSky keyboard: false): this registry owns the keyboard.
import type { SkySnapshot } from '@astroplanner/bridge-client';
import { useMemo } from 'react';
import { useUi } from '../state/ui';
import { useSkyCommand, useSnapshot } from './hostQueries';
import { type Shortcut, useShortcuts } from './shortcuts';

type LayerKey = SkySnapshot['layers'][number]['key'];

/** The rate before or after the current one (×1 → ×60 → ×600 → ×3600). */
export function neighbourRate(sky: SkySnapshot, direction: -1 | 1): number {
  const rates = sky.rates;
  const current = sky.isPlaying ? sky.rate : (rates[1] ?? rates[0] ?? 60);
  const index = rates.indexOf(current);
  const next = rates[Math.min(rates.length - 1, Math.max(0, (index < 0 ? 0 : index) + direction))];
  return next ?? current;
}

export function useSkyShortcuts(): readonly Shortcut[] {
  const { data: sky } = useSnapshot('sky.get');
  const play = useSkyCommand('sky.play');
  const pause = useSkyCommand('sky.pause');
  const now = useSkyCommand('sky.now');
  const setRate = useSkyCommand('sky.setRate');
  const setLayer = useSkyCommand('sky.setLayer');
  const setViewMode = useSkyCommand('sky.setViewMode');
  const toggleImmersive = useUi((state) => state.toggleImmersive);

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
        key: ' ',
        display: 'Space',
        run: () => {
          (sky?.isPlaying ? pause : play).mutate({});
        },
      },
      {
        id: 'now',
        key: 'F5',
        display: 'F5',
        run: () => {
          now.mutate({});
        },
      },
      {
        id: 'slower',
        key: 'j',
        display: 'J',
        run: () => {
          changeRate(-1);
        },
      },
      {
        id: 'faster',
        key: 'l',
        display: 'L',
        run: () => {
          changeRate(1);
        },
      },
      {
        id: 'dome',
        key: '1',
        display: '1',
        run: () => {
          setViewMode.mutate({ mode: 'dome' });
        },
      },
      {
        id: 'allSky',
        key: '2',
        display: '2',
        run: () => {
          setViewMode.mutate({ mode: 'allSky' });
        },
      },
      {
        id: 'finder',
        key: '3',
        display: '3',
        run: () => {
          setViewMode.mutate({ mode: 'finder' });
        },
      },
      {
        id: 'constellationLines',
        key: 'c',
        display: 'C',
        run: () => {
          toggleLayer('constellationLines');
        },
      },
      {
        id: 'constellationBoundaries',
        key: 'b',
        display: 'B',
        run: () => {
          toggleLayer('constellationBoundaries');
        },
      },
      {
        id: 'constellationNames',
        key: 'n',
        display: 'N',
        run: () => {
          toggleLayer('constellationNames');
        },
      },
      {
        id: 'altAzGrid',
        key: 'g',
        display: 'G',
        run: () => {
          toggleLayer('altAzGrid');
        },
      },
      {
        id: 'equatorialGrid',
        key: 'e',
        display: 'E',
        run: () => {
          toggleLayer('equatorialGrid');
        },
      },
      { id: 'immersive', key: 'h', display: 'H', run: toggleImmersive },
    ];
  }, [sky, play, pause, now, setRate, setLayer, setViewMode, toggleImmersive]);

  useShortcuts(shortcuts);
  return shortcuts;
}
