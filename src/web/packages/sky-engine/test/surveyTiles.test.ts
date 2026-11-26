// The survey layer's tile choice and placement (spec §8.3), without WebGL.
import { describe, expect, it } from 'vitest';
import { faceToVector, nestedToFaceXy, tileCap } from '../src/math/healpix';
import type { Vec3 } from '../src/math/frame';
import {
  allskyRect,
  allskyUrl,
  ancestorOf,
  ancestorRect,
  surveyOpacity,
  surveyOrder,
  tileGrid,
  tileUrl,
  visibleTiles,
  type SurveyConfig,
} from '../src/render/surveyTiles';

const DSS: SurveyConfig = {
  id: 'DSSColor',
  baseUrl: 'https://tiles.astroplanner/',
  maxOrder: 9,
  tileWidth: 512,
  credit: 'DSS',
};

function radec(raDegrees: number, decDegrees: number): Vec3 {
  const r = Math.PI / 180;
  return [
    Math.cos(decDegrees * r) * Math.cos(raDegrees * r),
    Math.cos(decDegrees * r) * Math.sin(raDegrees * r),
    Math.sin(decDegrees * r),
  ];
}

function angle(a: Vec3, b: Vec3): number {
  return (Math.acos(Math.min(1, a[0] * b[0] + a[1] * b[1] + a[2] * b[2])) * 180) / Math.PI;
}

describe('survey tiles', () => {
  it('picks the order whose texels match the screen pixels, within the survey', () => {
    // 1080 px high: a 10° field is ~0.0093°/px → order 4; a 1° field → order 7.
    expect(surveyOrder(10 / 1080, 512, 9)).toBe(4);
    expect(surveyOrder(1 / 1080, 512, 9)).toBe(7);
    expect(surveyOrder(0.25 / 2160, 512, 9)).toBe(9);
    expect(surveyOrder(40 / 1080, 512, 9)).toBe(3);
  });

  it('fades the survey in as the field narrows', () => {
    expect(surveyOpacity(60)).toBe(0);
    expect(surveyOpacity(30)).toBe(0);
    expect(surveyOpacity(22.5)).toBeCloseTo(0.5, 9);
    expect(surveyOpacity(15)).toBe(1);
    expect(surveyOpacity(1)).toBe(1);
  });

  it('finds the tiles that cover a view, nearest first, and only those', () => {
    const m42 = radec(83.82, -5.39);
    const tiles = visibleTiles(7, m42, 1.5);
    expect(tiles.length).toBeGreaterThan(4);
    expect(tiles.length).toBeLessThan(80); // ~35 inside the cone plus a ring of edge tiles
    for (const nested of tiles) {
      const cap = tileCap(7, nested);
      expect(angle(cap.center, m42)).toBeLessThanOrEqual(1.5 + cap.radiusDegrees);
    }
    // The first tile holds the centre.
    const first = tileCap(7, tiles[0] ?? -1);
    expect(angle(first.center, m42)).toBeLessThan(first.radiusDegrees);
    // A cone that drops everything below the "horizon" (here: south of Dec −4°) keeps only northern tiles.
    const north = visibleTiles(
      7,
      m42,
      1.5,
      (cap) => cap.center[2] + Math.sin((cap.radiusDegrees * Math.PI) / 180) > Math.sin((-4 * Math.PI) / 180),
    );
    expect(north.length).toBeLessThan(tiles.length);
  });

  it('works at the poles and across RA 0h', () => {
    expect(visibleTiles(5, [0, 0, 1], 3).length).toBeGreaterThan(3);
    expect(visibleTiles(5, radec(359.9, 0), 3).length).toBeGreaterThan(3);
  });

  it('builds proxy URLs in the HiPS layout', () => {
    expect(tileUrl(DSS, 9, 3145727)).toBe(
      'https://tiles.astroplanner/DSSColor/Norder9/Dir3140000/Npix3145727.jpg',
    );
    expect(tileUrl(DSS, 3, 5)).toBe('https://tiles.astroplanner/DSSColor/Norder3/Dir0/Npix5.jpg');
    expect(allskyUrl(DSS)).toBe('https://tiles.astroplanner/DSSColor/Norder3/Allsky.jpg');
  });

  it('places a child tile in its ancestor: rows follow x, columns follow y', () => {
    // Child 1 of a tile has x + 1 (lower half of the image), child 2 has y + 1 (right half).
    expect(ancestorRect(4, 4 * 10 + 0, 3)).toEqual([0, 0, 0.5, 0.5]);
    expect(ancestorRect(4, 4 * 10 + 1, 3)).toEqual([0, 0.5, 0.5, 0.5]);
    expect(ancestorRect(4, 4 * 10 + 2, 3)).toEqual([0.5, 0, 0.5, 0.5]);
    expect(ancestorRect(5, 16 * 10 + 15, 3)).toEqual([0.75, 0.75, 0.25, 0.25]);
    expect(ancestorOf(5, 16 * 10 + 15, 3)).toBe(10);
    // Inside an Allsky cell.
    expect(ancestorRect(4, 4 * 28 + 3, 3, allskyRect(28, 1728, 1856))).toEqual([
      (64 + 32) / 1728,
      (64 + 32) / 1856,
      32 / 1728,
      32 / 1856,
    ]);
  });

  it('lays the Allsky atlas out 27 tiles to a row', () => {
    expect(allskyRect(0, 1728, 1856)).toEqual([0, 0, 64 / 1728, 64 / 1856]);
    expect(allskyRect(28, 1728, 1856)).toEqual([64 / 1728, 64 / 1856, 64 / 1728, 64 / 1856]);
    expect(allskyRect(767, 1728, 1856)[1]).toBeCloseTo((28 * 64) / 1856, 12);
  });

  it('grids a tile on the sky with its corners at the HEALPix corners', () => {
    const grid = tileGrid(6, 1234, 4);
    expect(grid.positions.length).toBe(25 * 3);
    expect(grid.uvs.length).toBe(25 * 2);
    expect(grid.indices.length).toBe(16 * 6);
    const { face, x, y } = nestedToFaceXy(6, 1234);
    const corner = (k: number): number[] => Array.from(grid.positions.slice(k * 3, k * 3 + 3));
    faceToVector(6, face, x, y).forEach((v, i) => {
      expect(corner(0)[i]).toBeCloseTo(v, 6);
    });
    faceToVector(6, face, x + 1, y + 1).forEach((v, i) => {
      expect(corner(24)[i]).toBeCloseTo(v, 6);
    });
    expect(Array.from(grid.uvs.slice(0, 2))).toEqual([0, 0]);
    // Vertex (i = 1, j = 0): one step along x → v (row) grows.
    expect(Array.from(grid.uvs.slice(10, 12))).toEqual([0, 0.25]);
    expect(Array.from(grid.uvs.slice(48, 50))).toEqual([1, 1]);
  });
});
