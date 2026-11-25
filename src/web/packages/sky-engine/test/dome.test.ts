// The dome projection (spec §8.1): perspective up to 60°, stereographic from 120°, 0.25–180°.
import * as THREE from 'three';
import { describe, expect, it } from 'vitest';
import { segmentVertices } from '../src/render/grids';
import {
  domeCamera,
  domeInverse,
  domeK,
  domePixelsPerDegree,
  domeProject,
  separationDegrees,
} from '../src/render/projection';
import { domeStarScale, MAX_FOV, MIN_FOV } from '../src/render/skyStyle';
import { fromHorizontal, neuToWorld, type Vec3 } from '../src/math/frame';

const W = 1920;
const H = 1080;

function world(altitude: number, azimuth: number): Vec3 {
  return neuToWorld(fromHorizontal(altitude, azimuth));
}

describe('dome projection parameter', () => {
  it('is a perspective up to 60°, stereographic from 120° and continuous between', () => {
    expect(domeK(0.25)).toBe(1);
    expect(domeK(60)).toBe(1);
    expect(domeK(90)).toBeCloseTo(1.5, 10);
    expect(domeK(120)).toBe(2);
    expect(domeK(180)).toBe(2);
    for (let fov = 60; fov < 120; fov += 0.5) {
      expect(domeK(fov + 0.5) - domeK(fov)).toBeGreaterThanOrEqual(0);
      expect(domeK(fov + 0.5) - domeK(fov)).toBeLessThan(0.02);
    }
  });

  it('covers 0.25° to 180°', () => {
    expect(MIN_FOV).toBe(0.25);
    expect(MAX_FOV).toBe(180);
  });
});

describe('dome projection', () => {
  it.each([0.25, 1, 20, 45, 60])('matches the three.js perspective camera at %s° (as before R2)', (fov) => {
    const camera = new THREE.PerspectiveCamera(fov, W / H, 0.01, 10);
    const target = world(35, 200);
    camera.position.set(0, 0, 0);
    camera.up.set(0, 1, 0);
    camera.lookAt(target[0], target[1], target[2]);
    camera.updateMatrixWorld();
    camera.updateProjectionMatrix();
    const dome = domeCamera(35, 200, fov, W, H);
    for (let i = 0; i < 50; i++) {
      // Points spread over the screen: offsets up to 0.7 of the half field.
      const altitude = 35 + (((i * 37) % 100) / 100 - 0.5) * fov * 0.7;
      const azimuth = 200 + (((i * 61) % 100) / 100 - 0.5) * fov * 0.7;
      const p = world(altitude, azimuth);
      const v = new THREE.Vector3(p[0], p[1], p[2]).project(camera);
      const expected = { x: ((v.x + 1) / 2) * W, y: ((1 - v.y) / 2) * H };
      const actual = domeProject(dome, p);
      expect(actual).not.toBeNull();
      expect(Math.abs((actual?.x ?? 0) - expected.x)).toBeLessThan(0.01);
      expect(Math.abs((actual?.y ?? 0) - expected.y)).toBeLessThan(0.01);
    }
  });

  it.each([0.25, 10, 60, 75, 90, 120, 150, 180])('puts the top edge at half the field (%s°)', (fov) => {
    const dome = domeCamera(30, 180, fov, W, H);
    const top = domeProject(dome, world(30 + fov / 2, 180));
    expect(top?.x).toBeCloseTo(W / 2, 6);
    expect(top?.y).toBeCloseTo(0, 6);
    expect(domeProject(dome, world(30, 180))).toEqual({ x: W / 2, y: H / 2 });
  });

  it.each([0.25, 10, 60, 75, 90, 120, 150, 180])('round-trips screen ↔ sky at %s°', (fov) => {
    const dome = domeCamera(25, 140, fov, W, H);
    for (const [x, y] of [
      [0, 0],
      [W, H],
      [W / 2, H / 2],
      [100, 900],
      [1700, 200],
    ] as const) {
      const p = domeProject(dome, domeInverse(dome, x, y));
      expect(p?.x).toBeCloseTo(x, 4);
      expect(p?.y).toBeCloseTo(y, 4);
    }
  });

  it('keeps the screen corners inside the drawn angle at 180° (stereographic)', () => {
    const dome = domeCamera(0, 180, 180, W, H);
    const corner = domeInverse(dome, 0, 0);
    expect(separationDegrees(corner, world(0, 180))).toBeLessThan(135);
    expect(domeProject(dome, world(0, 0))).toBeNull(); // straight behind
  });

  it('is east-left when facing south', () => {
    const dome = domeCamera(20, 180, 70, W, H);
    const east = domeProject(dome, world(20, 170));
    expect(east?.x).toBeLessThan(W / 2);
  });

  it('has the same scale at the center for every k', () => {
    for (const fov of [30, 90, 150]) {
      const dome = domeCamera(40, 90, fov, W, H);
      const a = domeProject(dome, world(40, 90));
      const b = domeProject(dome, world(40.01, 90));
      expect(((a?.y ?? 0) - (b?.y ?? 0)) / 0.01).toBeCloseTo(domePixelsPerDegree(dome), 1);
    }
  });

  it('grows bright stars a little as the field narrows', () => {
    expect(domeStarScale(90)).toBe(1);
    expect(domeStarScale(60)).toBe(1);
    expect(domeStarScale(1)).toBeCloseTo(1.5, 1);
    expect(domeStarScale(0.25)).toBe(1.5);
  });
});

describe('segment tables', () => {
  it('splits long lines along their great circle into pieces of at most 2°', () => {
    const a: Vec3 = [1, 0, 0];
    const b: Vec3 = [Math.cos(0.5), Math.sin(0.5), 0]; // 28.6° apart
    const float32 = new Float32Array([...a, ...b, 0, ...a, 0.9999, 0.01, 0, 0]);
    const uint32 = new Uint32Array(float32.length);
    uint32[6] = 3;
    const { positions, groups } = segmentVertices({ count: 2, float32, uint32 });
    const pieces = positions.length / 6;
    expect(pieces).toBe(15 + 1);
    expect(groups[0]).toBe(3);
    for (let i = 0; i < 15; i++) {
      const p: Vec3 = [positions[i * 6] ?? 0, positions[i * 6 + 1] ?? 0, positions[i * 6 + 2] ?? 0];
      const q: Vec3 = [positions[i * 6 + 3] ?? 0, positions[i * 6 + 4] ?? 0, positions[i * 6 + 5] ?? 0];
      expect(separationDegrees(p, q)).toBeLessThanOrEqual(2.0001);
      expect(Math.hypot(...q)).toBeCloseTo(1, 5);
      expect(q[2]).toBeCloseTo(0, 6);
    }
  });
});
