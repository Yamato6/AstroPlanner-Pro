// Bridge protocol (spec §5.4), mirrored from src/AstroPlanner.SkyBridge/Payloads.cs. Every
// message is validated on arrival; the contract tests (test/contract.test.ts) check these schemas
// against the golden samples in tests/fixtures/bridge written by the C# side.
import { z } from 'zod';

// No JIT: zod would otherwise probe `Function('')`, which the CSP (no unsafe-eval) forbids.
z.config({ jitless: true });

export const PROTOCOL_VERSION = 1;
const MAX_TEXT = 128;

const text = z.string().min(1).max(MAX_TEXT);
const utc = z.iso.datetime();
const kind = z.enum(['star', 'dso', 'body']);
const viewMode = z.enum(['dome', 'allSky', 'finder']);

// ---- C# → renderer -------------------------------------------------------------------------

export const refractionSchema = z.strictObject({
  a: z.number(),
  b: z.number(),
  c: z.number(),
  d: z.number(),
  minimumAltitudeDegrees: z.number(),
});

export const layersSchema = z.strictObject({
  stars: z.boolean(),
  constellationLines: z.boolean(),
  constellationBoundaries: z.boolean(),
  constellationNames: z.boolean(),
  altAzGrid: z.boolean(),
  equatorialGrid: z.boolean(),
  ecliptic: z.boolean(),
  meridian: z.boolean(),
  milkyWay: z.boolean(),
  deepSky: z.boolean(),
  bodies: z.boolean(),
  labels: z.boolean(),
  ground: z.boolean(),
  skyBrightness: z.boolean(),
  satellites: z.boolean(),
  starLimitMagnitude: z.number().min(-2).max(12),
  deepSkyLimitMagnitude: z.number().min(-2).max(20),
});

const initPayload = z.strictObject({
  dataBaseUrl: z.url(),
  refraction: refractionSchema,
  layers: layersSchema,
  viewMode,
  theme: z.enum(['dark', 'night']),
  showStats: z.boolean(),
});

const observerPayload = z.strictObject({
  name: z.string().max(MAX_TEXT),
  latitudeDegrees: z.number().min(-90).max(90),
  longitudeDegrees: z.number().min(-180).max(180),
  elevationMeters: z.number(),
  minimumAltitudeDegrees: z.number().min(-90).max(90),
  horizon: z
    .array(z.strictObject({ azimuth: z.number().min(0).max(360), altitude: z.number().min(-10).max(90) }))
    .max(720),
  refraction: refractionSchema,
});

const timePayload = z.strictObject({ utc, rate: z.number().min(-1e6).max(1e6) });
const timeRatePayload = z.strictObject({ rate: z.number().min(-1e6).max(1e6) });

const frameTransformPayload = z.strictObject({
  epochUtc: utc,
  precession: z.array(z.number()).length(9),
  latitudeDegrees: z.number().min(-90).max(90),
  lstDegrees: z.number().min(0).max(360),
  lstRateDegreesPerSecond: z.number().positive(),
});

const movingBodySchema = z.strictObject({
  id: text,
  kind: z.enum(['sun', 'moon', 'planet', 'comet', 'asteroid', 'satellite']),
  name: text,
  magnitude: z.number(),
  diameterArcsec: z.number().min(0),
  phaseAngleDegrees: z.number().min(0).max(180),
  illuminatedFraction: z.number().min(0).max(1),
  brightLimbAngleDegrees: z.number().min(0).max(360),
  track: z.strictObject({
    startUtc: utc,
    stepSeconds: z.number().positive(),
    vectors: z
      .array(z.number().min(-1.000001).max(1.000001))
      .min(3)
      .refine((v) => v.length % 3 === 0, 'vectors must be x, y, z triples'),
  }),
  /** Satellites only: 1 = sunlit, 0 = in the Earth's shadow, per track sample. */
  sunlit: z
    .array(z.union([z.literal(0), z.literal(1)]))
    .max(2000)
    .nullable(),
});

const movingBodiesPayload = z.strictObject({ bodies: z.array(movingBodySchema).max(128) });
const highlightsPayload = z.strictObject({ plan: z.array(text).max(5000) });
export const selectionPayload = z.union([
  z.strictObject({ kind, id: text }),
  z.strictObject({ kind: z.null(), id: z.null() }),
]);
const centerOnPayload = z.strictObject({
  x: z.number(),
  y: z.number(),
  z: z.number(),
  fovDegrees: z.number().min(0.25).max(180).nullable(),
});
const viewModePayload = z.strictObject({ mode: viewMode });
const themePayload = z.strictObject({ theme: z.enum(['dark', 'night']) });
const vectorSchema = z.strictObject({
  x: z.number().min(-1.000001).max(1.000001),
  y: z.number().min(-1.000001).max(1.000001),
  z: z.number().min(-1.000001).max(1.000001),
});
const fovPayload = z.strictObject({
  visible: z.boolean(),
  widthDegrees: z.number().gt(0).max(180),
  heightDegrees: z.number().gt(0).max(180),
  rotationDegrees: z.number().min(0).lt(360),
  center: vectorSchema.nullable(),
  label: z.string().max(MAX_TEXT),
});
const skyAppearancePayload = z
  .strictObject({
    startUtc: utc,
    stepSeconds: z.number().positive(),
    zenithSqm: z.array(z.number().min(-5).max(30)).min(1).max(2000),
    limitingMagnitude: z.array(z.number().min(-30).max(10)).min(1).max(2000),
  })
  .refine((p) => p.zenithSqm.length === p.limitingMagnitude.length, 'one limiting magnitude per sample');

function envelope<T extends string, P extends z.ZodType>(type: T, payload: P) {
  return z.strictObject({ v: z.literal(PROTOCOL_VERSION), type: z.literal(type), payload });
}

export const hostMessageSchema = z.discriminatedUnion('type', [
  envelope('init', initPayload),
  envelope('setObserver', observerPayload),
  envelope('setTime', timePayload),
  envelope('setTimeRate', timeRatePayload),
  envelope('frameTransform', frameTransformPayload),
  envelope('movingBodies', movingBodiesPayload),
  envelope('setLayers', layersSchema),
  envelope('setHighlights', highlightsPayload),
  envelope('selectObject', selectionPayload),
  envelope('centerOn', centerOnPayload),
  envelope('setViewMode', viewModePayload),
  envelope('setTheme', themePayload),
  envelope('setFov', fovPayload),
  envelope('setSkyAppearance', skyAppearancePayload),
]);

export type HostMessage = z.infer<typeof hostMessageSchema>;
export type Layers = z.infer<typeof layersSchema>;
export type MovingBody = z.infer<typeof movingBodySchema>;
export type Observer = z.infer<typeof observerPayload>;
export type Selection = z.infer<typeof selectionPayload>;
export type ViewMode = z.infer<typeof viewMode>;
export type Fov = z.infer<typeof fovPayload>;
export type SkyAppearance = z.infer<typeof skyAppearancePayload>;

// ---- renderer → C# -------------------------------------------------------------------------

export const rendererMessageSchema = z.discriminatedUnion('type', [
  envelope(
    'ready',
    z.strictObject({ rendererVersion: text, webGlVersion: z.union([z.literal(1), z.literal(2)]) }),
  ),
  envelope('firstFrame', z.strictObject({ milliseconds: z.number().min(0) })),
  envelope('selectObject', selectionPayload),
  envelope(
    'hover',
    z.union([
      z.strictObject({ kind, id: text, label: z.string().max(MAX_TEXT).nullable() }),
      z.strictObject({ kind: z.null(), id: z.null(), label: z.null() }),
    ]),
  ),
  envelope(
    'viewChanged',
    z.strictObject({
      mode: viewMode,
      azimuthDegrees: z.number().min(0).lt(360),
      altitudeDegrees: z.number().min(-90).max(90),
      fovDegrees: z.number().gt(0).max(360),
    }),
  ),
  envelope(
    'stats',
    z.strictObject({
      fps: z.number().min(0),
      p95FrameMs: z.number().min(0),
      frames: z.int().min(0),
      width: z.int().min(0),
      height: z.int().min(0),
      stars: z.int().min(0),
      benchmark: z.boolean(),
    }),
  ),
  envelope(
    'shortcut',
    z.strictObject({ action: z.enum(['togglePause', 'viewDome', 'viewAllSky', 'toggleFov', 'focusSearch']) }),
  ),
  envelope(
    'fovChanged',
    z
      .strictObject({
        x: z.number(),
        y: z.number(),
        z: z.number(),
        rotationDegrees: z.number().min(0).lt(360),
      })
      .refine((p) => Math.abs(Math.hypot(p.x, p.y, p.z) - 1) <= 1e-3, 'center must be a unit vector'),
  ),
  envelope('error', z.strictObject({ message: z.string().min(1).max(1024) })),
]);

export type RendererMessage = z.infer<typeof rendererMessageSchema>;
