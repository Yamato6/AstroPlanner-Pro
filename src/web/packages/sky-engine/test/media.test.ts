// The sky's images (R2c): the manifest the DataBuilder writes, and how the renderer samples them.
import { describe, expect, it } from 'vitest';
import {
  equirectangularLod,
  landscapeHorizon,
  mediaManifestSchema,
  panoramaFieldFade,
} from '../src/render/media';
import { maskAltitude } from '../src/render/horizon';
import { readJson, repoRoot } from './fixtures';

const manifest = mediaManifestSchema.parse(readJson(`${repoRoot}data/build/media/manifest.json`));

describe('media manifest', () => {
  it('is the one the DataBuilder wrote: a 2:1 panorama and landscapes with a full horizon', () => {
    expect(manifest.panorama.width).toBe(2 * manifest.panorama.height);
    expect(manifest.panorama.credit.author).toBe('ESO/S. Brunier');
    expect(manifest.landscapes.length).toBeGreaterThanOrEqual(3);
    for (const landscape of manifest.landscapes) {
      expect(landscape.horizon).toHaveLength(360);
      expect(Math.max(...landscape.horizon)).toBeLessThanOrEqual(landscape.topAltitudeDegrees);
    }
  });

  it('rejects a file outside the media folder', () => {
    const tampered = structuredClone(manifest);
    tampered.panorama.file = '../app/index.html';
    expect(mediaManifestSchema.safeParse(tampered).success).toBe(false);
  });
});

describe('landscape horizon', () => {
  it('gives the label cut-off the landscape outline, degree by degree', () => {
    const landscape = manifest.landscapes[0];
    expect(landscape).toBeDefined();
    if (!landscape) return;
    const points = landscapeHorizon(landscape);
    expect(points).toHaveLength(360);
    expect(maskAltitude(points, 90.5)).toBeCloseTo(landscape.horizon[90] ?? 0, 6);
    expect(maskAltitude(points, 359.9)).toBeGreaterThanOrEqual(
      Math.min(landscape.horizon[359] ?? 0, landscape.horizon[0] ?? 0),
    );
  });
});

describe('sampling', () => {
  it('picks the mipmap level with one texel per pixel', () => {
    // 4096 texels over 360° drawn at 0.0879°/px: exactly level 0; twice that: level 1.
    expect(equirectangularLod(4096, 360 / 4096)).toBeCloseTo(0, 9);
    expect(equirectangularLod(4096, 720 / 4096)).toBeCloseTo(1, 9);
    expect(equirectangularLod(4096, 0.001)).toBe(0);
  });

  it('fades the panorama out in narrow fields', () => {
    expect(panoramaFieldFade(70)).toBe(1);
    expect(panoramaFieldFade(20)).toBe(1);
    expect(panoramaFieldFade(5)).toBe(0);
    expect(panoramaFieldFade(1)).toBe(0);
    expect(panoramaFieldFade(12.5)).toBeCloseTo(0.5, 9);
  });
});
