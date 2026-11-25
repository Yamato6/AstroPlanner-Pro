// Dome camera motion (spec §8.1, §9.2): flights, inertia, Home.
import { describe, expect, it } from 'vitest';
import { skyControl } from '../src/control';
import { fromHorizontal } from '../src/math/frame';
import { easeInOut, flightDuration, Flight, HOME_VIEW, Inertia, slerp } from '../src/render/camera';
import { separationDegrees } from '../src/render/projection';

describe('flights', () => {
  it('take 300 ms for a hop and at most 1.5 s across the sky', () => {
    expect(flightDuration(0, 70, 70)).toBe(300);
    expect(flightDuration(10, 70, 70)).toBeGreaterThan(300);
    expect(flightDuration(180, 1, 120)).toBe(1500);
  });

  it('ease in and out', () => {
    expect(easeInOut(0)).toBe(0);
    expect(easeInOut(0.5)).toBeCloseTo(0.5, 10);
    expect(easeInOut(1)).toBe(1);
    expect(easeInOut(0.1)).toBeLessThan(0.1);
    expect(easeInOut(0.9)).toBeGreaterThan(0.9);
  });

  it('follow the great circle', () => {
    const a = fromHorizontal(0, 0);
    const b = fromHorizontal(0, 90);
    const mid = slerp(a, b, 0.5);
    expect(separationDegrees(mid, fromHorizontal(0, 45))).toBeLessThan(1e-9);
  });

  it('start where the view is and arrive on the target, then report done', () => {
    const start = { azimuth: 100, altitude: 20, fov: 60 };
    const target = { azimuth: 160, altitude: 40, fov: 5 };
    const flight = new Flight(start, target, 1000);
    const first = flight.at(1000, target);
    expect(first.done).toBe(false);
    expect(first.view.azimuth).toBeCloseTo(100, 6);
    expect(first.view.altitude).toBeCloseTo(20, 6);
    expect(first.view.fov).toBeCloseTo(60, 6);
    const middle = flight.at(1000 + flight.durationMs / 2, target).view;
    expect(middle.fov).toBeLessThan(60);
    expect(middle.fov).toBeGreaterThan(5);
    const end = flight.at(1000 + flight.durationMs, target);
    expect(end.done).toBe(true);
    expect(end.view).toEqual(target);
  });

  it('follow a target that moves while flying (the sky turns)', () => {
    const start = { azimuth: 180, altitude: 30, fov: 70 };
    const flight = new Flight(start, { azimuth: 200, altitude: 30, fov: 70 }, 0);
    const moved = { azimuth: 205, altitude: 31, fov: 70 };
    const end = flight.at(flight.durationMs, moved);
    expect(end.view).toEqual(moved);
  });

  it('open the field in the middle of a long turn so that the way shows', () => {
    const start = { azimuth: 0, altitude: 20, fov: 10 };
    const target = { azimuth: 170, altitude: 20, fov: 10 };
    const flight = new Flight(start, target, 0);
    expect(flight.at(flight.durationMs / 2, target).view.fov).toBeGreaterThan(20);
  });

  it('never look further down than −30° or past the zenith', () => {
    const flight = new Flight(HOME_VIEW, { azimuth: 0, altitude: -60, fov: 70 }, 0);
    expect(flight.at(flight.durationMs, { azimuth: 0, altitude: -60, fov: 70 }).view.altitude).toBe(-30);
  });

  it('go home to the south, 30° up, 70° field', () => {
    expect(HOME_VIEW).toEqual({ azimuth: 180, altitude: 30, fov: 70 });
  });
});

describe('inertia', () => {
  it('coasts after a fast release and slows down to a stop', () => {
    const inertia = new Inertia();
    for (let t = 0; t <= 60; t += 10) {
      inertia.sample(t * 2, 0, t); // 2 px/ms
    }
    expect(inertia.release(62)).toBe(true);
    const first = inertia.step(78);
    expect(first?.dx).toBeGreaterThan(20);
    let total = first?.dx ?? 0;
    let steps = 0;
    let now = 78;
    let step = first;
    while (step && steps < 500) {
      now += 16;
      step = inertia.step(now);
      total += step?.dx ?? 0;
      steps++;
    }
    expect(inertia.active).toBe(false);
    expect(steps).toBeLessThan(200);
    expect(total).toBeLessThan(2 * 250 * 1.2); // ≈ v·τ
  });

  it('does not coast after a slow drag or a pause before releasing', () => {
    const slow = new Inertia();
    for (let t = 0; t <= 60; t += 10) {
      slow.sample(t * 0.1, 0, t);
    }
    expect(slow.release(60)).toBe(false);

    const paused = new Inertia();
    for (let t = 0; t <= 60; t += 10) {
      paused.sample(t * 2, 0, t);
    }
    expect(paused.release(200)).toBe(false);
  });

  it('stops at once when asked (a new drag)', () => {
    const inertia = new Inertia();
    inertia.sample(0, 0, 0);
    inertia.sample(100, 0, 40);
    inertia.release(41);
    inertia.stop();
    expect(inertia.step(60)).toBeNull();
  });
});

describe('sky control', () => {
  it('does nothing before the sky is mounted', () => {
    expect(skyControl.isTracking).toBe(false);
    expect(skyControl.setTracking(true)).toBe(false);
    expect(() => {
      skyControl.home();
      skyControl.setInertia(false);
    }).not.toThrow();
  });
});
