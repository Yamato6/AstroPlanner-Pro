// 2D charts, field-of-view overlay, sky appearance and horizon profile (spec §4.2).
import { describe, expect, it } from 'vitest';
import { fromHorizontal, type Vec3 } from '../src/math/frame';
import {
  appearanceAt,
  DARK_SKY,
  deepSkyLoss,
  horizonColor,
  milkyWayFactor,
  skyColor,
  starCap,
  toTrack,
} from '../src/render/appearance';
import { hasObstruction, maskAltitude, silhouetteTriangles } from '../src/render/horizon';
import {
  allSkyGeometry,
  allSkyInverse,
  allSkyPoint,
  finderGeometry,
  fovOutline,
  fovUpMarker,
  gnomonic,
  gnomonicInverse,
  insidePolygon,
  raDec,
  rotateTowards,
  separationDegrees,
  tangentBasis,
} from '../src/render/projection';

const DEG = Math.PI / 180;

function equatorial(raHours: number, decDegrees: number): Vec3 {
  const ra = raHours * 15 * DEG;
  const dec = decDegrees * DEG;
  return [Math.cos(dec) * Math.cos(ra), Math.cos(dec) * Math.sin(ra), Math.sin(dec)];
}

describe('all-sky chart', () => {
  const g = allSkyGeometry(800, 600);

  it('puts the zenith at the center and the horizon on the circle', () => {
    expect(g.cx).toBe(400);
    expect(g.cy).toBe(300);
    expect(g.scale).toBe(270);
    expect(allSkyPoint(90, 123, g)).toEqual({ x: 400, y: 300 });
    const north = allSkyPoint(0, 0, g);
    expect(north.x).toBeCloseTo(400, 9);
    expect(north.y).toBeCloseTo(30, 9);
  });

  it('has north up and east LEFT (v1 convention)', () => {
    const east = allSkyPoint(0, 90, g);
    const south = allSkyPoint(0, 180, g);
    const west = allSkyPoint(0, 270, g);
    expect(east.x).toBeCloseTo(130, 9);
    expect(east.y).toBeCloseTo(300, 9);
    expect(south.y).toBeCloseTo(570, 9);
    expect(west.x).toBeCloseTo(670, 9);
  });

  it('is equidistant in altitude and inverts', () => {
    const p = allSkyPoint(30, 225, g);
    expect(Math.hypot(p.x - g.cx, p.y - g.cy)).toBeCloseTo((60 / 90) * g.scale, 9);
    const back = allSkyInverse(p.x, p.y, g);
    expect(back.altitudeDegrees).toBeCloseTo(30, 9);
    expect(back.azimuthDegrees).toBeCloseTo(225, 9);
    expect(allSkyInverse(g.cx, g.cy, g).altitudeDegrees).toBe(90);
  });
});

describe('finder chart (gnomonic)', () => {
  const m42 = equatorial(5.5881, -5.3911);
  const basis = tangentBasis(m42);
  const g = finderGeometry(800, 600, 10);

  it('builds an orthonormal east/north/center frame', () => {
    const rows: Vec3[] = [
      [basis[0], basis[1], basis[2]],
      [basis[3], basis[4], basis[5]],
      [basis[6], basis[7], basis[8]],
    ];
    rows.forEach((a, i) => {
      rows.forEach((b, j) => {
        expect(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]).toBeCloseTo(i === j ? 1 : 0, 12);
      });
    });
    // At the poles "east" falls back to +y.
    const pole = tangentBasis([0, 0, 1]);
    expect(pole[1]).toBeCloseTo(1, 12);
  });

  it('centers the object, north up, east left, and spans the field across the height', () => {
    const center = gnomonic(basis, m42, g);
    expect(center?.x).toBeCloseTo(400, 9);
    expect(center?.y).toBeCloseTo(300, 9);
    const north = gnomonic(basis, equatorial(5.5881, -5.3911 + 2), g);
    expect(north?.y).toBeLessThan(300);
    expect(north?.x).toBeCloseTo(400, 6);
    const east = gnomonic(basis, equatorial(5.5881 + 0.1, -5.3911), g);
    expect(east?.x).toBeLessThan(400);
    // 5° north (half the 10° field) lands on the top edge.
    expect(gnomonic(basis, equatorial(5.5881, -5.3911 + 5), g)?.y).toBeCloseTo(0, 6);
  });

  it('clips the far hemisphere and inverts', () => {
    expect(gnomonic(basis, [-m42[0], -m42[1], -m42[2]], g)).toBeNull();
    const v = gnomonicInverse(basis, 250, 120, g);
    const p = gnomonic(basis, v, g);
    expect(p?.x).toBeCloseTo(250, 9);
    expect(p?.y).toBeCloseTo(120, 9);
  });
});

describe('field-of-view overlay', () => {
  const center = equatorial(0.7123, 41.2688);

  it('is a sensor rectangle of the requested angular size', () => {
    const outline = fovOutline(center, 6, 4, 0, 8);
    expect(outline).toHaveLength(33);
    expect(outline[32]).toEqual(outline[0]);
    // Edge midpoints are half the field away from the center (tangent-plane rectangle).
    const top = outline[4];
    const right = outline[12];
    expect(top && separationDegrees(center, top)).toBeCloseTo(2, 9);
    expect(right && separationDegrees(center, right)).toBeCloseTo(3, 9);
    // Every point is a unit vector.
    outline.forEach((v) => {
      expect(Math.hypot(...v)).toBeCloseTo(1, 12);
    });
  });

  it('rotates by position angle from north through east', () => {
    const basis = tangentBasis(center);
    const g = finderGeometry(800, 800, 20);
    const up0 = gnomonic(basis, fovUpMarker(center, 4, 0), g);
    const up90 = gnomonic(basis, fovUpMarker(center, 4, 90), g);
    expect(up0?.x).toBeCloseTo(400, 6);
    expect(up0?.y).toBeLessThan(400); // north = up on the chart
    expect(up90?.x).toBeLessThan(400); // east = left
    expect(up90?.y).toBeCloseTo(400, 6);
  });

  it('can be hit-tested and dragged', () => {
    const square = [
      { x: 0, y: 0 },
      { x: 10, y: 0 },
      { x: 10, y: 10 },
      { x: 0, y: 10 },
    ];
    expect(insidePolygon(square, 5, 5)).toBe(true);
    expect(insidePolygon(square, 15, 5)).toBe(false);
    const from = equatorial(1, 10);
    const to = equatorial(2, 20);
    const moved = rotateTowards(from, from, to);
    moved.forEach((c, i) => {
      expect(c).toBeCloseTo(to[i] ?? 0, 12);
    });
    expect(rotateTowards(center, from, from)).toBe(center);
  });

  it('reports the center as RA/Dec', () => {
    const { raHours, decDegrees } = raDec(equatorial(23.5, -45));
    expect(raHours).toBeCloseTo(23.5, 9);
    expect(decDegrees).toBeCloseTo(-45, 9);
  });
});

describe('sky appearance', () => {
  const track = toTrack({
    startUtc: '2026-03-20T00:00:00.000Z',
    stepSeconds: 600,
    zenithSqm: [6, 12, 21.5],
    limitingMagnitude: [-1, 2, 6.4],
    twilightSqm: [6, 12, 99],
    moonlightSqm: [99, 99, 99],
    siteSqm: 21.5,
    extinction: 0.2,
    brighteningPerAirmass: 0.5,
  });
  const start = Date.parse('2026-03-20T00:00:00.000Z');

  it('interpolates the Core samples and clamps to the ends', () => {
    expect(appearanceAt(track, start + 300_000).zenithSqm).toBeCloseTo(9, 9);
    expect(appearanceAt(track, start + 900_000).limitingMagnitude).toBeCloseTo(4.2, 9);
    expect(appearanceAt(track, start - 1e9).zenithSqm).toBe(6);
    expect(appearanceAt(track, start + 1e9).zenithSqm).toBe(21.5);
    expect(appearanceAt(null, start)).toBe(DARK_SKY);
    // The parts are interpolated as light: halfway from 12 to nothing is 0.75 mag fainter.
    expect(appearanceAt(track, start + 900_000).twilightSqm).toBeCloseTo(12.753, 3);
    expect(appearanceAt(track, start).moonlightSqm).toBe(99);
    expect(appearanceAt(track, start).extinction).toBe(0.2);
  });

  it('turns brightness into colors that get lighter as the sky brightens', () => {
    const luminance = (sqm: number): number => skyColor(sqm).reduce((a, b) => a + b, 0);
    expect(skyColor(23)).toEqual(skyColor(22));
    expect(luminance(20)).toBeGreaterThan(luminance(22));
    expect(luminance(12)).toBeGreaterThan(luminance(18));
    expect(luminance(3)).toBeGreaterThanOrEqual(luminance(5));
    horizonColor(21).forEach((c, i) => {
      expect(c).toBeCloseTo(skyColor(19.8)[i] ?? 0, 9);
    });
  });

  it('hides faint stars, deep-sky objects and the Milky Way in a bright sky', () => {
    expect(starCap(DARK_SKY)).toBeCloseTo(8.62, 2);
    expect(starCap({ zenithSqm: 18, limitingMagnitude: 4 })).toBe(6);
    expect(deepSkyLoss(DARK_SKY)).toBe(0);
    expect(deepSkyLoss({ zenithSqm: 18, limitingMagnitude: 4 })).toBeCloseTo(2.62, 2);
    expect(milkyWayFactor(DARK_SKY)).toBe(1);
    expect(milkyWayFactor({ zenithSqm: 20, limitingMagnitude: 5.4 })).toBeCloseTo(0.5, 9);
    expect(milkyWayFactor({ zenithSqm: 17, limitingMagnitude: 3 })).toBe(0);
  });
});

describe('horizon profile', () => {
  // Same points as HorizonMaskTests in C#: interpolated linearly, wrapping around north.
  const points = [
    { azimuth: 90, altitude: 10 },
    { azimuth: 180, altitude: 20 },
    { azimuth: 270, altitude: 0 },
  ];

  it('interpolates like Core HorizonMask.AltitudeAt', () => {
    expect(maskAltitude([], 45)).toBe(0);
    expect(maskAltitude([{ azimuth: 10, altitude: 7 }], 200)).toBe(7);
    expect(maskAltitude(points, 135)).toBeCloseTo(15, 12);
    expect(maskAltitude(points, 0)).toBeCloseTo(5, 12);
    expect(maskAltitude(points, 315)).toBeCloseTo(2.5, 12);
    expect(maskAltitude(points, -45)).toBeCloseTo(2.5, 12);
  });

  it('draws ground only where the profile rises above the horizon', () => {
    expect(hasObstruction([{ azimuth: 0, altitude: -2 }])).toBe(false);
    expect(hasObstruction(points)).toBe(true);
    const triangles = silhouetteTriangles(points, 10);
    expect(triangles.length % 9).toBe(0);
    expect(triangles.length).toBeGreaterThan(0);
    // The highest vertex is the 20° peak toward the south.
    let maxUp = -1;
    for (let i = 2; i < triangles.length; i += 3) {
      maxUp = Math.max(maxUp, triangles[i] ?? -1);
    }
    expect(maxUp).toBeCloseTo(fromHorizontal(20, 180)[2], 6);
  });
});
