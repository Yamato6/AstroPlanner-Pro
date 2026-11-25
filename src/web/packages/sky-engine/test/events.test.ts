import { describe, expect, it } from 'vitest';
import { skyEvents, skyPoint } from '../src/events';

describe('sky events for the page', () => {
  it('turns a J2000 unit vector into right ascension and declination', () => {
    const horizontal = { altitudeDegrees: 30, azimuthDegrees: 120 };

    expect(skyPoint([1, 0, 0], horizontal)).toMatchObject({
      raHours: 0,
      decDegrees: 0,
      altitudeDegrees: 30,
      azimuthDegrees: 120,
    });
    expect(skyPoint([0, 1, 0], horizontal).raHours).toBeCloseTo(6, 10);
    expect(skyPoint([0, -1, 0], horizontal).raHours).toBeCloseTo(18, 10);
    expect(skyPoint([0, 0, 2], horizontal).decDegrees).toBeCloseTo(90, 10);
    expect(skyPoint([Math.SQRT1_2, 0, -Math.SQRT1_2], horizontal).decDegrees).toBeCloseTo(-45, 10);
  });

  it('delivers events to listeners until they stop listening', () => {
    const seen: (number | null)[] = [];
    const stop = skyEvents.on('cursor', (point) => seen.push(point?.raHours ?? null));

    skyEvents.emit('cursor', null);
    skyEvents.emit('cursor', { raHours: 5.5, decDegrees: -5, altitudeDegrees: 40, azimuthDegrees: 150 });
    stop();
    skyEvents.emit('cursor', null);

    expect(seen).toEqual([null, 5.5]);
  });
});
