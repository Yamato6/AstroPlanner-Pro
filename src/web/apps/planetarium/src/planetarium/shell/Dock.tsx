import type { SkySnapshot } from '@astroplanner/bridge-client';
import { IconButton, Panel, ToggleIconButton } from '@astroplanner/ui-kit';
import { type ZonedDateTime, fromAbsolute } from '@internationalized/date';
import {
  CircleDot,
  Compass,
  Eclipse,
  FastForward,
  Globe,
  Grid3x3,
  type LucideIcon,
  Mountain,
  Orbit,
  Pause,
  Play,
  Rewind,
  Satellite,
  SquareDashed,
  Star,
  SunDim,
  Tag,
  Type,
  Waves,
  Spline,
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Button,
  DateField,
  DateInput,
  DateSegment,
  type Key,
  type PressEvent,
  ToggleButton,
  ToggleButtonGroup,
} from 'react-aria-components';
import { useSkyCommand, useSnapshot } from '../app/hostQueries';
import { DarknessBar } from './DarknessBar';
import classes from './Dock.module.css';
import { useSkyTime } from './useSkyTime';

const styles = classes as Readonly<
  Record<
    'dock' | 'controls' | 'group' | 'divider' | 'now' | 'segmented' | 'segment' | 'date' | 'segmentInput',
    string
  >
>;

type LayerKey = SkySnapshot['layers'][number]['key'];

/** Icons of the sky's layer switches, in the order the dock shows them. */
const layerIcons: Readonly<Record<LayerKey, LucideIcon>> = {
  stars: Star,
  constellationLines: Spline,
  constellationNames: Type,
  constellationBoundaries: SquareDashed,
  milkyWay: Waves,
  deepSky: CircleDot,
  bodies: Orbit,
  satellites: Satellite,
  labels: Tag,
  altAzGrid: Grid3x3,
  equatorialGrid: Globe,
  ecliptic: Eclipse,
  meridian: Compass,
  ground: Mountain,
  skyBrightness: SunDim,
};
const layerOrder = Object.keys(layerIcons) as LayerKey[];

/** Minutes of a step: 10, or 1 h with Shift, 1 day with Ctrl, 1 min with Alt (spec §3.2). */
export function stepMinutes(event: Pick<PressEvent, 'shiftKey' | 'ctrlKey' | 'altKey'>): number {
  if (event.ctrlKey) {
    return 1440;
  }
  if (event.shiftKey) {
    return 60;
  }
  if (event.altKey) {
    return 1;
  }
  return 10;
}

/** The sky time as a date field value in the site's zone (null without one). */
function zoned(utcMs: number | null, timeZone: string | undefined): ZonedDateTime | null {
  if (utcMs === null || !timeZone) {
    return null;
  }
  try {
    return fromAbsolute(utcMs, timeZone);
  } catch {
    return fromAbsolute(utcMs, 'UTC');
  }
}

/**
 * The bottom dock (spec §3.2): the darkness bar, which is also the time scrubber; time controls
 * (steps, play/pause, speed, now, the date and time); the layers; the view mode.
 */
export function Dock({ idle }: { readonly idle: boolean }) {
  const { t } = useTranslation();
  const { data: sky, dataUpdatedAt } = useSnapshot('sky.get');
  const { data: observer } = useSnapshot('observer.get');
  const { data: night } = useSnapshot('night.get');
  const skyTime = useSkyTime(sky, dataUpdatedAt);
  const play = useSkyCommand('sky.play');
  const pause = useSkyCommand('sky.pause');
  const now = useSkyCommand('sky.now');
  const step = useSkyCommand('sky.step');
  const setTime = useSkyCommand('sky.setTime');
  const setRate = useSkyCommand('sky.setRate');
  const setLayer = useSkyCommand('sky.setLayer');
  const setViewMode = useSkyCommand('sky.setViewMode');

  // The HUD and the toasts sit above the dock, whatever its height (it wraps on narrow windows).
  const panel = useRef<HTMLDivElement>(null);
  const shown = sky !== undefined;
  useEffect(() => {
    const element = panel.current;
    if (!element) {
      return undefined;
    }
    const observer = new ResizeObserver(() => {
      document.documentElement.style.setProperty('--dock-height', `${Math.round(element.offsetHeight)}px`);
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [shown]);

  // The date field edits a copy: the sky moves when it is committed (Enter or leaving the field).
  // A copy made before the sky last changed is stale and dropped.
  const [edit, setEdit] = useState<{ value: ZonedDateTime; revision: number } | null>(null);
  const timeZone = observer?.timeZoneId;

  if (!sky) {
    return null;
  }

  const draft = edit?.revision === sky.revision ? edit.value : null;
  const setDraft = (value: ZonedDateTime | null): void => {
    setEdit(value ? { value, revision: sky.revision } : null);
  };
  const commitDraft = (): void => {
    if (draft) {
      setTime.mutate({ utcMilliseconds: draft.toDate().getTime() });
      setDraft(null);
    }
  };
  const stepBy = (direction: 1 | -1) => (event: PressEvent) => {
    step.mutate({ minutes: direction * stepMinutes(event) });
  };
  const rateKey = sky.isPlaying ? String(sky.rate) : null;

  return (
    <Panel
      ref={panel}
      className={styles.dock}
      role="region"
      aria-label={t('dock.label')}
      data-toolbar=""
      data-idle={idle || undefined}
    >
      {night && timeZone && skyTime !== null ? (
        <DarknessBar
          timeline={night}
          timeZone={timeZone}
          skyTime={skyTime}
          onCommit={(utcMilliseconds) => {
            setTime.mutate({ utcMilliseconds });
          }}
        />
      ) : null}

      <div className={styles.controls}>
        <div className={styles.group} role="group" aria-label={t('dock.time')}>
          <IconButton label={t('dock.back')} icon={Rewind} tooltipPlacement="top" onPress={stepBy(-1)} />
          {sky.isPlaying ? (
            <IconButton
              label={t('dock.pause')}
              icon={Pause}
              shortcut="Space"
              tooltipPlacement="top"
              onPress={() => {
                pause.mutate({});
              }}
            />
          ) : (
            <IconButton
              label={t('dock.play')}
              icon={Play}
              shortcut="Space"
              tooltipPlacement="top"
              onPress={() => {
                play.mutate({});
              }}
            />
          )}
          <IconButton
            label={t('dock.forward')}
            icon={FastForward}
            tooltipPlacement="top"
            onPress={stepBy(1)}
          />
          <Button
            className={styles.now}
            onPress={() => {
              now.mutate({});
            }}
          >
            {t('dock.now')}
          </Button>
          <DateField
            className={styles.date}
            aria-label={t('dock.dateTime')}
            granularity="minute"
            hourCycle={24}
            hideTimeZone
            value={draft ?? zoned(skyTime, timeZone)}
            onChange={(value) => {
              setDraft(value);
            }}
            onBlur={commitDraft}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                commitDraft();
              }
            }}
          >
            <DateInput className={styles.segmentInput}>
              {(segment) => <DateSegment segment={segment} />}
            </DateInput>
          </DateField>
          <ToggleButtonGroup
            className={styles.segmented}
            aria-label={t('dock.rate')}
            selectionMode="single"
            selectedKeys={rateKey ? [rateKey] : []}
            onSelectionChange={(keys: Set<Key>) => {
              const [key] = [...keys];
              if (key === undefined) {
                return;
              }
              setRate.mutate({ rate: Number(key) });
              if (!sky.isPlaying) {
                play.mutate({});
              }
            }}
          >
            {sky.rates.map((rate) => (
              <ToggleButton key={rate} id={String(rate)} className={styles.segment}>
                ×{rate}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        </div>

        <span className={styles.divider} aria-hidden="true" />

        <div className={styles.group} role="group" aria-label={t('dock.layers')}>
          {layerOrder.map((key) => {
            const layer = sky.layers.find((l) => l.key === key);
            return layer ? (
              <ToggleIconButton
                key={key}
                label={layer.name}
                icon={layerIcons[key]}
                tooltipPlacement="top"
                isSelected={layer.enabled}
                onChange={(enabled) => {
                  setLayer.mutate({ key, enabled });
                }}
              />
            ) : null;
          })}
        </div>

        <span className={styles.divider} aria-hidden="true" />

        <ToggleButtonGroup
          className={styles.segmented}
          aria-label={t('dock.viewMode')}
          selectionMode="single"
          disallowEmptySelection
          selectedKeys={[sky.viewMode]}
          onSelectionChange={(keys: Set<Key>) => {
            const [mode] = [...keys];
            if (mode === 'dome' || mode === 'allSky' || mode === 'finder') {
              setViewMode.mutate({ mode });
            }
          }}
        >
          <ToggleButton id="dome" className={styles.segment}>
            {t('dock.dome')}
          </ToggleButton>
          <ToggleButton id="allSky" className={styles.segment}>
            {t('dock.allSky')}
          </ToggleButton>
          <ToggleButton id="finder" className={styles.segment}>
            {t('dock.finder')}
          </ToggleButton>
        </ToggleButtonGroup>
      </div>
    </Panel>
  );
}
