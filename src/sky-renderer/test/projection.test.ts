// Projection cross-check (spec §5.3): the same golden file as the C# test
// (tests/fixtures/bridge/projection.json, produced from Core GetHorizontal).
import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import {
  apparentHorizontal,
  azimuthDifference,
  j2000ToHorizontal,
  type Mat3,
  type Vec3,
} from '../src/math/frame';
import { bridgeFixtures, readJson } from './fixtures';

const fixture = z
  .object({
    refraction: z.object({
      a: z.number(),
      b: z.number(),
      c: z.number(),
      d: z.number(),
      minimumAltitudeDegrees: z.number(),
    }),
    cases: z.array(
      z.object({
        site: z.string(),
        target: z.string(),
        utc: z.string(),
        frame: z.object({
          epochUtc: z.string(),
          precession: z.array(z.number()).length(9),
          latitudeDegrees: z.number(),
          lstDegrees: z.number(),
          lstRateDegreesPerSecond: z.number(),
        }),
        j2000: z.array(z.number()).length(3),
        altitudeDegrees: z.number(),
        azimuthDegrees: z.number(),
      }),
    ),
  })
  .parse(readJson(`${bridgeFixtures}projection.json`));

describe('projection cross-check', () => {
  it('has cases for several sites, epochs and extrapolated times', () => {
    expect(fixture.cases.length).toBeGreaterThanOrEqual(40);
  });

  it.each(fixture.cases.map((c) => [`${c.target} @ ${c.site} ${c.utc}`, c] as const))(
    '%s reproduces Core within 0.01 degrees',
    (_, c) => {
      const frame = {
        epochMs: Date.parse(c.frame.epochUtc),
        precession: c.frame.precession as unknown as Mat3,
        latitudeDegrees: c.frame.latitudeDegrees,
        lstDegrees: c.frame.lstDegrees,
        lstRateDegreesPerSecond: c.frame.lstRateDegreesPerSecond,
      };
      const h = j2000ToHorizontal(frame, Date.parse(c.utc));
      const actual = apparentHorizontal(h, c.j2000 as unknown as Vec3, fixture.refraction);
      expect(Math.abs(actual.altitudeDegrees - c.altitudeDegrees)).toBeLessThan(0.01);
      expect(azimuthDifference(actual.azimuthDegrees, c.azimuthDegrees)).toBeLessThan(0.01);
    },
  );
});
