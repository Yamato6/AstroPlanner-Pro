import { describe, expect, it } from 'vitest';
import {
  azimuthDifference,
  fromHorizontal,
  interpolateTrack,
  neuToWorld,
  refract,
  rotationZ,
  toHorizontal,
  transform,
  transpose,
  worldToNeu,
} from '../src/math/frame';
import { dsoShape, starColor, temperatureFromColorIndex } from '../src/render/colors';
import { pickNearest, type PickCandidate } from '../src/render/picking';
import { FrameStats } from '../src/stats';
import { SkyClock } from '../src/time/clock';

const standard = { a: 1.02, b: 10.3, c: 5.11, d: 0.0019279, minimumAltitudeDegrees: -2 };

describe('frame math', () => {
  it('refraction matches the Core reference values', () => {
    expect(refract(90, standard)).toBeCloseTo(90, 6);
    expect(refract(45, standard) - 45).toBeCloseTo(0.0167, 3);
    expect(refract(-0.5667, standard) + 0.5667).toBeCloseTo(0.57, 1);
    expect(refract(-5, standard)).toBe(-5);
  });

  it('round-trips horizontal coordinates and world axes', () => {
    const h = toHorizontal(fromHorizontal(37.5, 251.25));
    expect(h.altitudeDegrees).toBeCloseTo(37.5, 9);
    expect(h.azimuthDegrees).toBeCloseTo(251.25, 9);
    expect(worldToNeu(neuToWorld([0.1, 0.2, 0.3]))).toEqual([0.1, 0.2, 0.3]);
    expect(toHorizontal([0, 0, 1]).azimuthDegrees).toBe(0);
  });

  it('inverts a rotation with its transpose', () => {
    const v = transform(transpose(rotationZ(33)), transform(rotationZ(33), [1, 2, 3]));
    v.forEach((c, i) => {
      expect(c).toBeCloseTo([1, 2, 3][i] ?? 0, 12);
    });
  });

  it('interpolates tracks and clamps outside them', () => {
    const vectors = [1, 0, 0, 0, 1, 0];
    const mid = interpolateTrack(vectors, 0, 10, 5000);
    expect(mid[0]).toBeCloseTo(Math.SQRT1_2, 9);
    expect(mid[1]).toBeCloseTo(Math.SQRT1_2, 9);
    expect(interpolateTrack(vectors, 0, 10, -1e6)).toEqual([1, 0, 0]);
    expect(interpolateTrack(vectors, 0, 10, 1e6)).toEqual([0, 1, 0]);
    expect(interpolateTrack([0, 0, 1], 0, 10, 99)).toEqual([0, 0, 1]);
  });

  it('measures azimuth differences across north', () => {
    expect(azimuthDifference(359.5, 0.5)).toBeCloseTo(1, 12);
  });
});

describe('picking', () => {
  const candidate = (id: string, x: number, weight: number, radius = 0): PickCandidate => ({
    kind: 'star',
    id,
    label: id,
    x,
    y: 0,
    weight,
    radius,
  });

  it('picks the closest candidate within the tolerance', () => {
    expect(pickNearest([candidate('a', 5, 0), candidate('b', 2, 0)], 0, 0)?.id).toBe('b');
    expect(pickNearest([candidate('a', 50, 0)], 0, 0)).toBeNull();
  });

  it('lets heavier objects win near ties and measures from disc edges', () => {
    expect(pickNearest([candidate('star', 1, 0), candidate('planet', 4, 20)], 0, 0)?.id).toBe('planet');
    expect(pickNearest([candidate('moon', 30, 0, 25)], 0, 0)?.id).toBe('moon');
  });
});

describe('clock', () => {
  it('advances at the rate set by C#', () => {
    let perf = 0;
    const clock = new SkyClock(() => perf, 1_000_000, 60);
    perf = 1000;
    expect(clock.now()).toBe(1_060_000);
    clock.setRate(0);
    perf = 5000;
    expect(clock.now()).toBe(1_060_000);
    clock.set(0, 1);
    perf = 5500;
    expect(clock.now()).toBe(500);
    expect(clock.rate).toBe(1);
  });
});

describe('frame statistics', () => {
  it('computes fps and the 95th percentile frame time', () => {
    const stats = new FrameStats(100);
    for (let i = 0; i <= 100; i++) {
      stats.tick(i * 10 + (i === 50 ? 30 : 0));
    }
    expect(stats.frames).toBe(101);
    expect(stats.fps).toBeCloseTo(100, 0);
    expect(stats.p95FrameMs).toBeGreaterThanOrEqual(10);
    stats.reset();
    expect(stats.fps).toBe(0);
    expect(stats.p95FrameMs).toBe(0);
  });
});

describe('colors', () => {
  it('maps B-V to plausible temperatures and tints', () => {
    expect(temperatureFromColorIndex(0.65)).toBeGreaterThan(5500);
    expect(temperatureFromColorIndex(0.65)).toBeLessThan(6000);
    const [r, , b] = starColor(1.8);
    expect(r).toBeGreaterThan(b);
    const [r2, , b2] = starColor(-0.3);
    expect(b2).toBeGreaterThanOrEqual(r2);
  });

  it('assigns marker shapes per type', () => {
    expect(dsoShape('Galaxy')).toBe(0);
    expect(dsoShape('OpenCluster')).toBe(1);
    expect(dsoShape('EmissionNebula')).toBe(2);
    expect(dsoShape('PlanetaryNebula')).toBe(3);
  });
});
