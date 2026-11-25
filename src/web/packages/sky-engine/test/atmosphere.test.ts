// Atmosphere colour sanity (spec §12): the scattering shape behaves like a real sky.
import { describe, expect, it } from 'vitest';
import { luminance, scatteredLight, tableCoordinates, tableDirection } from '../src/render/atmosphere';

describe('atmosphere', () => {
  it('gets brighter at the zenith as the Sun rises', () => {
    let previous = 0;
    for (let sun = -6; sun <= 40; sun += 2) {
      const zenith = luminance(scatteredLight(90, 0, sun));
      expect(zenith).toBeGreaterThan(previous);
      previous = zenith;
    }
  });

  it('is blue at the zenith by day', () => {
    const [r, g, b] = scatteredLight(90, 0, 40);
    expect(b).toBeGreaterThan(g);
    expect(g).toBeGreaterThan(r);
  });

  it('is warmer toward the setting Sun than at the zenith', () => {
    const horizon = scatteredLight(2, 0, 0);
    const zenith = scatteredLight(90, 0, 0);
    expect(horizon[0] / horizon[2]).toBeGreaterThan(2 * (zenith[0] / zenith[2]));
  });

  it('glows toward the Sun in twilight', () => {
    const towardSun = luminance(scatteredLight(5, 0, -4));
    const away = luminance(scatteredLight(5, 180, -4));
    expect(towardSun).toBeGreaterThan(away);
  });

  it('is brightest around the Sun by day (forward scattering)', () => {
    expect(luminance(scatteredLight(30, 5, 30))).toBeGreaterThan(luminance(scatteredLight(30, 120, 30)));
  });

  it('maps the table both ways, denser near the horizon', () => {
    for (const [alt, az] of [
      [-5, 0],
      [0, 45],
      [30, 90],
      [90, 180],
    ] as const) {
      const { u, v } = tableCoordinates(alt, az);
      const back = tableDirection(u, v);
      expect(back.altitudeDegrees).toBeCloseTo(alt, 9);
      expect(back.azimuthFromSunDegrees).toBeCloseTo(az, 9);
    }
    expect(tableCoordinates(10, 0).u).toBeGreaterThan(0.38);
  });
});
