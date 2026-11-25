// The sky's images (R2c, spec §8.3/§8.5): the Milky Way panorama and the landscapes, next to the
// exe (data/, served as https://media.astroplanner/) and described by manifest.json, written by
// the DataBuilder's build-media (tools/AstroPlanner.DataBuilder/MediaBuilder.cs). Without them
// the sky keeps the outline Milky Way and the horizon profile.
import { z } from 'zod';

const creditSchema = z.object({
  author: z.string().min(1).max(200),
  license: z.string().min(1).max(50),
  url: z.string().max(200),
  changes: z.string().max(500),
});

const fileSchema = z
  .string()
  .regex(/^[a-z0-9-]+\/[a-z0-9-]+\.webp$/)
  .max(100);

export const mediaManifestSchema = z.object({
  version: z.literal(1),
  /** J2000 equirectangular: u = RA / 360° from the left edge, v = (90° − Dec) / 180° from the top. */
  panorama: z.object({
    file: fileSchema,
    width: z.number().int().positive(),
    height: z.number().int().positive(),
    credit: creditSchema,
  }),
  /**
   * Columns: azimuth 0°–360° from the left edge (north, then east); rows from topAltitudeDegrees
   * (top) down to −90° (bottom); transparent above the ground.
   */
  landscapes: z
    .array(
      z.object({
        id: z.string().regex(/^[a-z0-9-]{1,40}$/),
        name: z.string().min(1).max(100),
        file: fileSchema,
        width: z.number().int().positive(),
        height: z.number().int().positive(),
        topAltitudeDegrees: z.number().min(-10).max(90),
        horizon: z.array(z.number().min(-10).max(90)).length(360),
        credit: creditSchema,
      }),
    )
    .max(32),
});

export type MediaManifest = z.infer<typeof mediaManifestSchema>;
export type LandscapeEntry = MediaManifest['landscapes'][number];

/** The manifest at `baseUrl`, or null when it is missing or malformed (the sky then works without images). */
export async function loadMediaManifest(baseUrl: string): Promise<MediaManifest | null> {
  const response = await fetch(new URL('manifest.json', baseUrl).toString());
  if (!response.ok) {
    return null;
  }
  const parsed = mediaManifestSchema.safeParse(await response.json());
  return parsed.success ? parsed.data : null;
}

/**
 * Decodes an image of the media folder off the main thread, at most `maxWidth` wide (the height
 * keeps the ratio). Rows stay top-down: the shaders sample v = 0 at the top.
 */
export async function loadMediaImage(baseUrl: string, file: string, maxWidth: number): Promise<ImageBitmap> {
  const response = await fetch(new URL(file, baseUrl).toString());
  if (!response.ok) {
    throw new Error(`sky media: ${file} → HTTP ${response.status}`);
  }
  const blob = await response.blob();
  const probe = await createImageBitmap(blob);
  if (probe.width <= maxWidth) {
    return probe;
  }
  const height = Math.round((probe.height * maxWidth) / probe.width);
  probe.close();
  return createImageBitmap(blob, { resizeWidth: maxWidth, resizeHeight: height, resizeQuality: 'high' });
}

/** The landscape's horizon as profile points (the label cut-off and the silhouette's rules). */
export function landscapeHorizon(entry: LandscapeEntry): { azimuth: number; altitude: number }[] {
  return entry.horizon.map((altitude, degree) => ({ azimuth: degree + 0.5, altitude }));
}

/**
 * Mipmap level for an equirectangular texture `texelsPer360` wide drawn at `degreesPerPixel`:
 * one texel per pixel at level 0. Set per frame instead of per-pixel derivatives, which jump at
 * the seam where the azimuth (or RA) wraps from 360° to 0°.
 */
export function equirectangularLod(texelsPer360: number, degreesPerPixel: number): number {
  return Math.max(0, Math.log2(Math.max(1e-6, (texelsPer360 / 360) * degreesPerPixel)));
}

/**
 * How strongly the panorama shows at a field of view: in full above 20°, gone below 5° (its
 * pixels are 5′ wide; a telescope field shows no Milky Way glow).
 */
export function panoramaFieldFade(fovDegrees: number): number {
  const t = Math.min(1, Math.max(0, (fovDegrees - 5) / 15));
  return t * t * (3 - 2 * t);
}
