// The darkness bar's model (spec §1.5, signature detail): tonight as runs of daylight, the three
// twilights and full night, with the Moon above the horizon and the cloud cover, from the samples
// C# computes (night.get). Pure functions, tested on their own.
import type { NightTimeline } from '@astroplanner/bridge-client';

export type Darkness = 'day' | 'civil' | 'nautical' | 'astronomical' | 'night';

/** The darkness a Sun altitude gives: day above the horizon, then twilights every 6°, night below −18°. */
export function darknessOf(sunAltitudeDegrees: number): Darkness {
  if (sunAltitudeDegrees > 0) {
    return 'day';
  }
  if (sunAltitudeDegrees > -6) {
    return 'civil';
  }
  if (sunAltitudeDegrees > -12) {
    return 'nautical';
  }
  if (sunAltitudeDegrees > -18) {
    return 'astronomical';
  }
  return 'night';
}

/** Samples [start, end) that share one value. */
export interface Run<T> {
  readonly start: number;
  readonly end: number;
  readonly value: T;
}

/** Consecutive samples with the same value, merged. */
export function runs<T>(values: readonly T[]): Run<T>[] {
  const result: Run<T>[] = [];
  values.forEach((value, index) => {
    const last = result[result.length - 1];
    if (last?.value === value) {
      result[result.length - 1] = { ...last, end: index + 1 };
    } else {
      result.push({ start: index, end: index + 1, value });
    }
  });
  return result;
}

/** Runs of darkness. */
export function darknessRuns(timeline: NightTimeline): Run<Darkness>[] {
  return runs(timeline.sunAltitudeDegrees.map(darknessOf));
}

/** Runs while the Moon is above the horizon. */
export function moonRuns(timeline: NightTimeline): Run<true>[] {
  return runs(timeline.moonAltitudeDegrees.map((altitude) => altitude > 0)).filter(
    (run): run is Run<true> => run.value,
  );
}

const MINUTE_MS = 60_000;

/** The bar's span in minutes (0 when there are no samples). */
export function spanMinutes(timeline: NightTimeline): number {
  return Math.max(0, timeline.sunAltitudeDegrees.length - 1) * timeline.stepMinutes;
}

/** Minutes from the start of the bar to an instant, kept on the bar. */
export function minutesAt(timeline: NightTimeline, utcMs: number): number {
  const minutes = (utcMs - timeline.startUtcMilliseconds) / MINUTE_MS;
  return Math.min(spanMinutes(timeline), Math.max(0, minutes));
}

/** The instant a position on the bar stands for. */
export function instantAt(timeline: NightTimeline, minutes: number): number {
  return timeline.startUtcMilliseconds + Math.round(minutes) * MINUTE_MS;
}

/** True when an instant falls on the bar (the sky may show another night). */
export function covers(timeline: NightTimeline, utcMs: number): boolean {
  const minutes = (utcMs - timeline.startUtcMilliseconds) / MINUTE_MS;
  return spanMinutes(timeline) > 0 && minutes >= 0 && minutes <= spanMinutes(timeline);
}

/** Positions (minutes) of the whole hours on the bar, every `everyHours`, in the site's time zone. */
export function hourTicks(
  timeline: NightTimeline,
  timeZone: string,
  everyHours = 2,
): { minutes: number; label: string }[] {
  const span = spanMinutes(timeline);
  const format = (() => {
    try {
      return new Intl.DateTimeFormat('en-GB', {
        timeZone,
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      });
    } catch {
      return new Intl.DateTimeFormat('en-GB', {
        timeZone: 'UTC',
        hour: '2-digit',
        minute: '2-digit',
        hour12: false,
      });
    }
  })();
  const ticks: { minutes: number; label: string }[] = [];
  // Whole hours fall on whole minutes: walk the bar minute by minute from the first whole minute.
  const firstMinute = Math.ceil(timeline.startUtcMilliseconds / MINUTE_MS) * MINUTE_MS;
  for (let ms = firstMinute; (ms - timeline.startUtcMilliseconds) / MINUTE_MS <= span; ms += MINUTE_MS) {
    const label = format.format(ms);
    if (label.endsWith(':00')) {
      if (Number(label.slice(0, 2)) % everyHours === 0) {
        ticks.push({ minutes: (ms - timeline.startUtcMilliseconds) / MINUTE_MS, label });
      }
      ms += 59 * MINUTE_MS; // the next whole hour is an hour away
    }
  }
  return ticks;
}
