// The survey layer's HEALPix against Core's (tests/fixtures/bridge/healpix.json, spec §8.3): the
// same face pixels for every index and the same directions within 1″.
import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import {
  faceToLonLat,
  faceToVector,
  faceXyToNested,
  nestedToFaceXy,
  pixelCount,
  tileCap,
} from '../src/math/healpix';
import { bridgeFixtures, readJson } from './fixtures';

const fixture = z
  .object({
    cases: z.array(
      z.object({
        order: z.number().int(),
        nested: z.number().int(),
        face: z.number().int(),
        x: z.number().int(),
        y: z.number().int(),
        fx: z.number(),
        fy: z.number(),
        longitudeDegrees: z.number(),
        latitudeDegrees: z.number(),
      }),
    ),
  })
  .parse(readJson(`${bridgeFixtures}healpix.json`));

const ARCSEC = 1 / 3600;

function separationDegrees(a: { lon: number; lat: number }, lon: number, lat: number): number {
  const r = Math.PI / 180;
  const c =
    Math.sin(a.lat * r) * Math.sin(lat * r) +
    Math.cos(a.lat * r) * Math.cos(lat * r) * Math.cos((a.lon - lon) * r);
  return Math.acos(Math.min(1, Math.max(-1, c))) / r;
}

describe('HEALPix (NESTED) against Core', () => {
  it('has cases from order 0 to 12 on every face', () => {
    expect(fixture.cases.length).toBeGreaterThanOrEqual(300);
    expect(new Set(fixture.cases.map((c) => c.order))).toEqual(new Set([0, 1, 3, 6, 9, 12]));
  });

  it('splits every index into the same face pixel, and back', () => {
    for (const c of fixture.cases) {
      expect(nestedToFaceXy(c.order, c.nested)).toEqual({ face: c.face, x: c.x, y: c.y });
      expect(faceXyToNested(c.order, c.face, c.x, c.y)).toBe(c.nested);
    }
  });

  it('places every point within 1″ of Core', () => {
    for (const c of fixture.cases) {
      const p = faceToLonLat(c.order, c.face, c.x + c.fx, c.y + c.fy);
      expect(separationDegrees(p, c.longitudeDegrees, c.latitudeDegrees)).toBeLessThan(ARCSEC);
      const v = faceToVector(c.order, c.face, c.x + c.fx, c.y + c.fy);
      expect(Math.hypot(...v)).toBeCloseTo(1, 12);
    }
  });

  it('bounds every tile with a cap that holds its corners', () => {
    for (const order of [0, 3, 7]) {
      for (const nested of [0, 5, pixelCount(order) - 1, Math.floor(pixelCount(order) / 3)]) {
        const cap = tileCap(order, nested);
        const { face, x, y } = nestedToFaceXy(order, nested);
        for (const [fx, fy] of [
          [0, 0],
          [1, 1],
          [0.3, 0.9],
        ] as const) {
          const v = faceToVector(order, face, x + fx, y + fy);
          const angle =
            (Math.acos(Math.min(1, v[0] * cap.center[0] + v[1] * cap.center[1] + v[2] * cap.center[2])) *
              180) /
            Math.PI;
          expect(angle).toBeLessThanOrEqual(cap.radiusDegrees);
        }
      }
    }
  });

  it('rejects orders the layer does not use', () => {
    expect(() => nestedToFaceXy(14, 0)).toThrow(RangeError);
    expect(() => nestedToFaceXy(-1, 0)).toThrow(RangeError);
  });
});
