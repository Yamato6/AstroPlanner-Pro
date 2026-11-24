import type { SkySnapshot } from '@astroplanner/bridge-client';
import { useEffect, useState } from 'react';

/** Display refresh while time plays (spec §7.4: the time display updates at 4 Hz). */
const TICK_MS = 250;

/** The sky's time now: the snapshot's clock advanced by its rate since it was received. */
export function skyTimeAt(sky: SkySnapshot, receivedAt: number, now: number): number {
  return sky.isPlaying ? sky.clockUtcMilliseconds + sky.rate * (now - receivedAt) : sky.clockUtcMilliseconds;
}

/** The sky time (UTC milliseconds), ticking while it plays; null before the first snapshot. */
export function useSkyTime(sky: SkySnapshot | undefined, receivedAt: number): number | null {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!sky?.isPlaying) {
      return undefined;
    }
    const timer = window.setInterval(() => {
      setNow(Date.now());
    }, TICK_MS);
    return () => {
      window.clearInterval(timer);
    };
  }, [sky?.isPlaying]);

  return sky ? skyTimeAt(sky, receivedAt, Math.max(now, receivedAt)) : null;
}
