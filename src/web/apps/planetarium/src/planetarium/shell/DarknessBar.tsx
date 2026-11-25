import type { NightTimeline } from '@astroplanner/bridge-client';
import { useLayoutEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Label, Slider, SliderThumb, SliderTrack } from 'react-aria-components';
import { siteDate, siteTime } from '../app/format';
import { covers, darknessRuns, hourTicks, instantAt, minutesAt, moonRuns, spanMinutes } from './darkness';
import classes from './Dock.module.css';

const styles = classes as Readonly<
  Record<
    | 'bar'
    | 'barLabel'
    | 'track'
    | 'strip'
    | 'day'
    | 'civil'
    | 'nautical'
    | 'astronomical'
    | 'night'
    | 'moon'
    | 'cloud'
    | 'thumb'
    | 'away'
    | 'ticks'
    | 'tick',
    string
  >
>;

export interface DarknessBarProps {
  readonly timeline: NightTimeline;
  readonly timeZone: string;
  /** The sky's time now (UTC ms), advancing while time plays. */
  readonly skyTime: number;
  /** The user let go of the thumb (or pressed a key) at this instant. */
  readonly onCommit: (utcMs: number) => void;
}

/**
 * Tonight from an hour before sunset to an hour after sunrise: daylight, the three twilights and
 * full night, the Moon above the horizon (a light band at the top, brighter for a fuller Moon)
 * and the cloud cover (at the bottom, when there is a forecast). It is also the time scrubber: a
 * slider over the night (arrows: 10 min, Page Up/Down: 1 h), committed when released.
 */
export function DarknessBar({ timeline, timeZone, skyTime, onCommit }: DarknessBarProps) {
  const { t } = useTranslation();
  const [dragging, setDragging] = useState<number | null>(null);
  const input = useRef<HTMLInputElement>(null);
  const span = spanMinutes(timeline);
  const samples = timeline.sunAltitudeDegrees.length;
  const shown = covers(timeline, skyTime);

  const drawing = useMemo(
    () => ({
      darkness: darknessRuns(timeline),
      moon: moonRuns(timeline),
      ticks: hourTicks(timeline, timeZone),
    }),
    [timeline, timeZone],
  );

  const value = dragging ?? minutesAt(timeline, skyTime);
  const at = instantAt(timeline, value);
  const valueText = `${siteDate(at, timeZone)} ${siteTime(at, timeZone)}`;

  // React Aria reads the value out as a number; a screen reader must hear the date and time. It
  // sets its attribute again on every render, so this runs after every render.
  useLayoutEffect(() => {
    input.current?.setAttribute('aria-valuetext', valueText);
  });

  if (samples < 2) {
    return null;
  }

  const moonOpacity = 0.25 + (0.75 * timeline.moonIlluminatedPercent) / 100;

  return (
    <Slider
      className={styles.bar}
      minValue={0}
      maxValue={span}
      step={timeline.stepMinutes}
      value={value}
      onChange={(minutes) => {
        setDragging(minutes);
      }}
      onChangeEnd={(minutes) => {
        setDragging(null);
        onCommit(instantAt(timeline, minutes));
      }}
    >
      <Label className={styles.barLabel}>{t('dock.darknessBar')}</Label>
      <SliderTrack className={styles.track}>
        <svg
          className={styles.strip}
          viewBox={`0 0 ${samples - 1} 10`}
          preserveAspectRatio="none"
          aria-hidden="true"
        >
          {drawing.darkness.map((run) => (
            <rect
              key={run.start}
              className={styles[run.value]}
              x={Math.max(0, run.start - 0.5)}
              width={Math.min(samples - 1, run.end - 0.5) - Math.max(0, run.start - 0.5)}
              y={0}
              height={10}
            />
          ))}
          {drawing.moon.map((run) => (
            <rect
              key={`moon-${run.start}`}
              className={styles.moon}
              x={Math.max(0, run.start - 0.5)}
              width={Math.min(samples - 1, run.end - 0.5) - Math.max(0, run.start - 0.5)}
              y={0}
              height={3}
              opacity={moonOpacity}
            />
          ))}
          {timeline.cloudPercent.map((cloud, index) =>
            cloud > 0 ? (
              <rect
                key={`cloud-${index}`}
                className={styles.cloud}
                x={Math.max(0, index - 0.5)}
                width={1}
                y={7}
                height={3}
                opacity={cloud / 100}
              />
            ) : null,
          )}
        </svg>
        <SliderThumb
          className={shown || dragging !== null ? styles.thumb : `${styles.thumb} ${styles.away}`}
          aria-label={t('dock.skyTime')}
          inputRef={input}
        />
      </SliderTrack>
      <div className={styles.ticks} aria-hidden="true">
        {drawing.ticks.map((tick) => (
          <span
            key={tick.minutes}
            className={styles.tick}
            style={{ left: `${(100 * tick.minutes) / span}%` }}
          >
            {tick.label}
          </span>
        ))}
      </div>
    </Slider>
  );
}
