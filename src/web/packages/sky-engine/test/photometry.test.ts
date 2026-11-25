// Sky brightness cross-check (spec §8.2): the renderer rebuilds Core's SkyBackground.At from the
// zenith levels it receives, within 0.05 mag (tests/fixtures/bridge/photometry.json).
import { describe, expect, it } from 'vitest';
import { z } from 'zod';
import {
  extinctionAt,
  fluxOf,
  kastenYoung,
  nakedEyeLimit,
  siteGlowFactor,
  skyBrightnessAt,
  sqmOf,
} from '../src/render/photometry';
import { bridgeFixtures, readJson } from './fixtures';

const fixture = z
  .object({
    cases: z.array(
      z.object({
        siteSqm: z.number(),
        extinction: z.number(),
        brighteningPerAirmass: z.number(),
        moonAltitudeDegrees: z.number(),
        moonlightZenithSqm: z.number(),
        altitudeDegrees: z.number(),
        separationDegrees: z.number(),
        totalSqm: z.number(),
        limitingMagnitude: z.number(),
      }),
    ),
  })
  .parse(readJson(`${bridgeFixtures}photometry.json`));

describe('sky brightness cross-check', () => {
  it('has cases for several sites, extinctions, Moons and directions', () => {
    expect(fixture.cases.length).toBeGreaterThanOrEqual(200);
  });

  it('rebuilds Core SkyBackground.At from the zenith levels within 0.05 mag', () => {
    let worst = 0;
    for (const c of fixture.cases) {
      const sqm = skyBrightnessAt(
        {
          siteSqm: c.siteSqm,
          twilightSqm: 99,
          moonlightSqm: c.moonlightZenithSqm,
          extinction: c.extinction,
          brighteningPerAirmass: c.brighteningPerAirmass,
        },
        c.altitudeDegrees,
        c.moonAltitudeDegrees,
        c.separationDegrees,
      );
      worst = Math.max(worst, Math.abs(sqm - c.totalSqm));
      expect(Math.abs(nakedEyeLimit(sqm) - c.limitingMagnitude)).toBeLessThan(0.05);
    }
    expect(worst).toBeLessThan(0.05);
  });
});

describe('photometry', () => {
  it('converts between brightness and flux; 99 means nothing', () => {
    expect(sqmOf(fluxOf(21.3))).toBeCloseTo(21.3, 10);
    expect(fluxOf(99)).toBe(0);
    expect(sqmOf(0)).toBe(99);
  });

  it('uses the Kasten & Young air mass', () => {
    expect(kastenYoung(90)).toBeCloseTo(1, 3);
    expect(kastenYoung(30)).toBeCloseTo(1.995, 2);
    expect(kastenYoung(0)).toBeCloseTo(38, 0);
    expect(kastenYoung(-10)).toBe(Number.POSITIVE_INFINITY);
  });

  it('brightens the site glow toward the horizon, capped at 10°', () => {
    expect(siteGlowFactor(90, 0.5)).toBeCloseTo(1, 3);
    expect(-2.5 * Math.log10(1 / siteGlowFactor(30, 0.5))).toBeCloseTo(0.44, 1);
    expect(siteGlowFactor(2, 0.5)).toBe(siteGlowFactor(0, 0.5));
  });

  it('dims stars by k·X, capped at X = 40', () => {
    expect(extinctionAt(90, 0.25)).toBeCloseTo(0.25, 3);
    expect(extinctionAt(-7, 0.25)).toBe(10);
  });

  it('adds twilight by its shape toward the direction', () => {
    const levels = {
      siteSqm: 22,
      twilightSqm: 15,
      moonlightSqm: 99,
      extinction: 0.25,
      brighteningPerAirmass: 0.5,
    };
    const dim = skyBrightnessAt(levels, 90, -10, 100, 1);
    const glow = skyBrightnessAt(levels, 90, -10, 100, 10);
    expect(dim - glow).toBeGreaterThan(2);
  });
});
