// The sky view: one three.js scene whose shaders apply the C#-computed frame in three projections
// (3D dome, all-sky chart, finder chart), plus a 2D canvas overlay for the Sun, Moon, planets,
// labels, cardinal points and the camera field-of-view overlay.
import * as THREE from 'three';
import type { Fov, Layers, MovingBody, Observer, SkyAppearance } from '../bridge/schema';
import { Dso, Star, UNKNOWN_MAGNITUDE } from '../data/buffers';
import type { SkyData } from '../data/loader';
import {
  apparentHorizontal,
  fromHorizontal,
  interpolateTrack,
  j2000ToHorizontal,
  neuToWorld,
  toHorizontal,
  transform,
  transpose,
  worldToNeu,
  type FrameTransform,
  type Horizontal,
  type Mat3,
  type RefractionModel,
  type Vec3,
} from '../math/frame';
import {
  appearanceAt,
  COLOR_STOPS,
  DARK_SKY,
  deepSkyLoss,
  displayLevel,
  displayLuminance,
  milkyWayFactor,
  skyColor,
  starCap,
  toTrack,
  type AppearanceNow,
  type AppearanceTrack,
} from './appearance';
import { AtmosphereTable, type SkyQuality } from './atmosphere';
import { extinctionAt, fluxOf, nakedEyeLimit, skyBrightnessAt } from './photometry';
import { Flight, HOME_VIEW, MAX_ALTITUDE, MIN_ALTITUDE, type DomeView } from './camera';
import { dsoShape, palette, starColor } from './colors';
import { altAzGrid, ecliptic, equatorialGrid, meridian, segmentVertices } from './grids';
import { hasObstruction, maskAltitude, silhouetteTriangles, type HorizonPoint } from './horizon';
import {
  equirectangularLod,
  landscapeHorizon,
  loadMediaImage,
  panoramaFieldFade,
  type LandscapeEntry,
  type MediaManifest,
} from './media';
import { pickNearest, type PickCandidate } from './picking';
import {
  allSkyGeometry,
  allSkyInverse,
  allSkyPoint,
  domeCamera,
  domeInverse,
  domeK,
  domeMaxAngle,
  domePixelsPerDegree,
  domeProject,
  domeScale,
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
  type ChartGeometry,
  type DomeCamera,
  type ScreenPoint,
  type ViewMode,
} from './projection';
import {
  dsoFragment,
  dsoVertex,
  groundFragment,
  groundVertex,
  landscapeFragment,
  landscapeVertex,
  lineFragment,
  lineVertex,
  silhouetteFragment,
  silhouetteVertex,
  skyPassFragment,
  skyPassVertex,
  starFragment,
  starVertex,
} from './shaders';

export interface BodyTrack {
  readonly body: MovingBody;
  readonly startMs: number;
}

export interface Selection {
  readonly kind: 'star' | 'dso' | 'body';
  readonly id: string;
}

export type { ScreenPoint, ViewMode } from './projection';

/**
 * Display brightness of the panorama under a dark natural sky (the texel's colour × this): the
 * star clouds of Sagittarius about 0.3, the faint band a few hundredths over the 0.04 of the sky.
 */
export const PANORAMA_GAIN = 0.6;

/** Light on the landscape under a dark natural sky (linear, 1 = the daylight photo): dim and bluish. */
const LANDSCAPE_NIGHT: Rgb = [0.05, 0.06, 0.08];

/**
 * Widest landscape or panorama texture: 4096 px (11 px per degree). An 8192 px landscape costs
 * about 280 MB more in WebView2's GPU process and takes the app past its 1.2 GB (spec §11).
 */
const MEDIA_MAX_WIDTH = 4096;

/** An image of the media folder now on the GPU, with the credit it must show. */
export interface MediaCredit {
  readonly kind: 'panorama' | 'landscape';
  readonly author: string;
  readonly license: string;
}

/** A projected point with its apparent horizontal position (90°/0 in the finder). */
interface Placed extends ScreenPoint {
  readonly altitude: number;
  readonly azimuth: number;
}

interface FovState {
  readonly widthDegrees: number;
  readonly heightDegrees: number;
  rotationDegrees: number;
  center: Vec3 | null;
  readonly label: string;
}

interface FinderLabel {
  readonly vector: Vec3;
  readonly text: string;
}

/** Why tracking stopped: a manual pan, the selection changed or vanished, or another mode. */
export type TrackingStop = 'manual' | 'selection' | 'lost' | 'mode';

type FlightTarget =
  | { readonly kind: 'direction'; readonly j2000: Vec3; readonly fov: number }
  | { readonly kind: 'view'; readonly view: DomeView }
  | { readonly kind: 'selection'; readonly fov: number };

const IDENTITY: Mat3 = [1, 0, 0, 0, 1, 0, 0, 0, 1];
/** Dome field of view, degrees: M57 (1.4′) fills a good part of the narrowest; 180° is a hemisphere. */
export const MIN_FOV = 0.25;
export const MAX_FOV = 180;
/** Above this field (degrees) only the brightest star names, no Messier and no minor-body labels are drawn. */
const WIDE_LABEL_FOV = 110;
const MIN_FINDER_FOV = 0.5;
const MAX_FINDER_FOV = 60;
const MILKY_WAY_OPACITY = 0.22;
const FINDER_LABELS = 60;
const CARDINALS = ['N', 'NE', 'E', 'SE', 'S', 'SW', 'W', 'NW'] as const;
const FOV_COLOR = '#ff6f6f';
/** Satellite trail: the last minute of the path, in 5 s pieces. */
const SATELLITE_TRAIL_STEPS = 12;
const SATELLITE_TRAIL_STEP_MS = 5000;

/** Light-pollution glow colour (spec §8.2): high-pressure sodium, white LED or neutral. */
export type LightPollutionTint = 'sodium' | 'led' | 'neutral';

type Rgb = readonly [number, number, number];

/** A colour scaled to luminance 1 (the sky pass multiplies it by the display level). */
function unitChroma(rgb: Rgb): THREE.Vector3 {
  const l = displayLuminance(rgb) || 1;
  return new THREE.Vector3(rgb[0] / l, rgb[1] / l, rgb[2] / l);
}

const TINTS: Readonly<Record<LightPollutionTint, Rgb>> = {
  sodium: [1.0, 0.66, 0.36],
  led: [0.86, 0.88, 0.95],
  neutral: [0.8, 0.8, 0.8],
};
/** Moonlight looks blue-grey to dark-adapted eyes (desaturated). */
const MOON_CHROMA: Rgb = [0.66, 0.74, 0.88];
/** The ground lit by day (sRGB). */
const DAY_GROUND: Rgb = [0.2, 0.22, 0.18];
const DAY_HORIZON: Rgb = [0.42, 0.46, 0.44];

/** The display curve as GLSL (piecewise linear over COLOR_STOPS' luminance). */
function displayLevelGlsl(): string {
  const stops = COLOR_STOPS.map(([sqm, rgb]) => [sqm, displayLuminance(rgb)] as const);
  const first = stops[0] ?? [22, 0];
  const last = stops[stops.length - 1] ?? [4, 1];
  const lines = [`  if (sqm >= ${first[0].toFixed(3)}) return ${first[1].toFixed(5)};`];
  for (let k = 1; k < stops.length; k++) {
    const [upperSqm, upperY] = stops[k - 1] ?? first;
    const [lowerSqm, lowerY] = stops[k] ?? last;
    lines.push(
      `  if (sqm >= ${lowerSqm.toFixed(3)}) return mix(${upperY.toFixed(5)}, ${lowerY.toFixed(5)}, (${upperSqm.toFixed(3)} - sqm) / ${(upperSqm - lowerSqm).toFixed(3)});`,
    );
  }
  lines.push(`  return ${last[1].toFixed(5)};`);
  return `float displayLevel(float sqm) {\n${lines.join('\n')}\n}`;
}

function color(rgb: readonly [number, number, number]): THREE.Color {
  return new THREE.Color(rgb[0], rgb[1], rgb[2]);
}

/** Dome star size: bright stars grow a little as the field narrows (×1 at 60°, ×1.5 at 1° and below). */
export function domeStarScale(fovDegrees: number): number {
  return 1 + Math.min(0.5, Math.max(0, 0.28 * Math.log10(60 / fovDegrees)));
}

function clampFov(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

export class SkyView {
  readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(70, 1, 0.01, 10);
  private readonly overlay: CanvasRenderingContext2D;

  private readonly shared = {
    uFrame: { value: new THREE.Matrix3() },
    uRefraction: { value: new THREE.Vector4(1.02, 10.3, 5.11, 0.0019279) },
    uRefractionMin: { value: -2 },
    uApplyRefraction: { value: 1 },
    uHideBelowHorizon: { value: 1 },
    uMode: { value: 0 },
    uFinder: { value: new THREE.Matrix3() },
    uChartScale: { value: new THREE.Vector2(1, 1) },
    uDomeBasis: { value: new THREE.Matrix3() },
    uProjK: { value: 1 },
    uProjScale: { value: 1 },
    uAspect: { value: 1 },
    uMaxTheta: { value: 1.5 },
    // Sky brightness (photometry, spec §8.2).
    uSiteFlux: { value: fluxOf(22) },
    uNaturalFlux: { value: fluxOf(22) },
    uTwilightFlux: { value: 0 },
    uMoonFlux: { value: 0 },
    uExtinction: { value: 0.25 },
    uBrightening: { value: 0.5 },
    uBrightnessOn: { value: 1 },
    uSunNeu: { value: new THREE.Vector3(0, 0, -1) },
    uMoonNeu: { value: new THREE.Vector3(0, 0, -1) },
    uAtmosphere: { value: null as THREE.Texture | null },
    uHasAtmosphere: { value: 0 },
    uFieldBonus: { value: 0 },
    uScintillation: { value: 0 },
    uTime: { value: 0 },
  };
  private readonly atmosphere: AtmosphereTable;
  private quality: SkyQuality = 'medium';
  private scintillation = false;
  private frameMs = 0;
  private readonly skyPassUniforms = {
    uViewport: { value: new THREE.Vector2(1, 1) },
    uNightChroma: { value: unitChroma(skyColor(22)) },
    uTintChroma: { value: unitChroma(TINTS.led) },
    uMoonChroma: { value: unitChroma(MOON_CHROMA) },
  };
  private readonly groundColors = {
    uGround: { value: color(palette.ground) },
    uHorizon: { value: color(palette.horizon) },
  };
  private readonly panoramaUniforms = {
    uPanorama: { value: null as THREE.Texture | null },
    uPanoramaOn: { value: 0 },
    uPanoramaGain: { value: 0 },
    uPanoramaLod: { value: 0 },
  };
  private readonly landscapeUniforms = {
    uLandscape: { value: null as THREE.Texture | null },
    uTopAltitude: { value: 0 },
    uLandscapeLod: { value: 0 },
    uLight: { value: new THREE.Vector3(1, 1, 1) },
    uSaturation: { value: 1 },
  };
  private media: { readonly baseUrl: string; readonly manifest: MediaManifest } | null = null;
  private panorama: { readonly texture: THREE.Texture; readonly width: number } | null = null;
  private landscape: { readonly entry: LandscapeEntry; texture: THREE.Texture | null; width: number } | null =
    null;
  private readonly landscapePass: THREE.Mesh;
  /** The dome camera of the last frame (projection of the overlay, picking, unproject). */
  private dome: DomeCamera = domeCamera(35, 180, 70, 1, 1);
  private readonly starLimitUniform = { value: 8.5 };
  private readonly starScaleUniform = { value: 1 };
  private readonly dsoUniforms = {
    uLimit: { value: 12 },
    uPixelsPerDegree: { value: 10 },
    uViewport: { value: new THREE.Vector2(1, 1) },
  };
  private readonly horizonFrame = {
    uFrame: { value: new THREE.Matrix3() },
    uApplyRefraction: { value: 0 },
  };
  private readonly milkyWayOpacity = { value: MILKY_WAY_OPACITY };

  private readonly stars: THREE.Points;
  private readonly dsos: THREE.Points;
  private readonly dsoHighlight: Float32Array;
  private readonly dsoIdIndex = new Map<string, number>();
  private readonly constellationLines: THREE.LineSegments;
  private readonly constellationBounds: THREE.LineSegments;
  private readonly milkyWay: THREE.LineSegments;
  private readonly altAz: THREE.LineSegments;
  private readonly meridianLine: THREE.LineSegments;
  private readonly eclipticLine: THREE.LineSegments;
  private equatorial: THREE.LineSegments | null = null;
  private equatorialPrecession: Mat3 | null = null;
  private readonly ground: THREE.Mesh;
  private readonly skyPass: THREE.Mesh;
  private silhouette: THREE.Mesh | null = null;

  private frame: FrameTransform | null = null;
  private refraction: RefractionModel = {
    a: 1.02,
    b: 10.3,
    c: 5.11,
    d: 0.0019279,
    minimumAltitudeDegrees: -2,
  };
  private layers: Layers;
  private observer: Observer | null = null;
  private horizonPoints: HorizonPoint[] = [];
  private bodies: BodyTrack[] = [];
  private plan = new Set<string>();
  private selection: Selection | null = null;
  private appearanceTrack: AppearanceTrack | null = null;
  private appearance: AppearanceNow = DARK_SKY;
  private currentH: Mat3 = IDENTITY;
  private width = 1;
  private height = 1;
  private readonly pixelRatio: number;
  private readonly starCount: number;
  private fovState: FovState | null = null;
  private fovScreen: (ScreenPoint | null)[] = [];
  private finderLabels: { key: string; items: FinderLabel[] } = { key: '', items: [] };
  private chart: ChartGeometry = { cx: 0, cy: 0, scale: 1 };
  private finderBasis: Mat3 = IDENTITY;

  /** Current projection. */
  mode: ViewMode = 'dome';
  /** Dome view direction and field of view (degrees). */
  azimuth = 180;
  altitude = 30;
  fov = 70;
  /** Finder chart center (J2000) and field. */
  finderCenter: Vec3 = [1, 0, 0];
  finderFov = 10;
  showStats = false;
  statsText = '';
  /** Called when tracking starts or stops. */
  onTrackingChanged: ((active: boolean, reason: TrackingStop | 'started') => void) | null = null;
  private flight: { flight: Flight; target: FlightTarget } | null = null;
  private tracking: Selection | null = null;
  private trackedFixed: Vec3 | null = null;
  private lastUtcMs = 0;
  /** Called when the field-of-view overlay gets a center from the view (null center) or is moved. */
  onFovChanged: ((center: Vec3, rotationDegrees: number) => void) | null = null;

  constructor(
    canvas: HTMLCanvasElement,
    overlayCanvas: HTMLCanvasElement,
    private readonly data: SkyData,
    layers: Layers,
    starMultiplier = 1,
    private readonly fixedSize: { width: number; height: number } | null = null,
  ) {
    this.layers = layers;
    this.pixelRatio = fixedSize ? 1 : Math.min(window.devicePixelRatio || 1, 2);
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(this.pixelRatio);
    this.scene.background = new THREE.Color(palette.background);
    const context = overlayCanvas.getContext('2d');
    if (!context) {
      throw new Error('2D canvas not available');
    }
    this.overlay = context;

    // The sky: a full-screen pass in the dome and the all-sky chart (the finder has a flat
    // background), drawn before everything else.
    this.atmosphere = new AtmosphereTable(this.quality);
    this.shared.uAtmosphere = this.atmosphere.texture;
    this.skyPass = new THREE.Mesh(
      new THREE.PlaneGeometry(2, 2),
      new THREE.ShaderMaterial({
        vertexShader: skyPassVertex,
        fragmentShader: skyPassFragment(displayLevelGlsl()),
        uniforms: { ...this.shared, ...this.skyPassUniforms, ...this.panoramaUniforms },
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.skyPass.renderOrder = -20;
    this.skyPass.frustumCulled = false;
    this.scene.add(this.skyPass);

    // Ground: a lower hemisphere drawn behind everything; objects below the horizon are discarded.
    this.ground = new THREE.Mesh(
      new THREE.SphereGeometry(5, 64, 16, 0, Math.PI * 2, Math.PI / 2, Math.PI / 2),
      new THREE.ShaderMaterial({
        vertexShader: groundVertex,
        fragmentShader: groundFragment,
        uniforms: { ...this.groundColors, ...this.domeUniforms() },
        side: THREE.BackSide,
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.ground.renderOrder = -10;
    this.ground.frustumCulled = false;
    this.scene.add(this.ground);

    // The landscape: a full-screen pass over the stars and lines (renderOrder), once its image is loaded.
    this.landscapePass = new THREE.Mesh(
      new THREE.PlaneGeometry(2, 2),
      new THREE.ShaderMaterial({
        vertexShader: landscapeVertex,
        fragmentShader: landscapeFragment,
        uniforms: {
          ...this.domeUniforms(),
          uMode: this.shared.uMode,
          uChartScale: this.shared.uChartScale,
          uViewport: this.skyPassUniforms.uViewport,
          ...this.landscapeUniforms,
        },
        transparent: true,
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.landscapePass.renderOrder = 20;
    this.landscapePass.frustumCulled = false;
    this.landscapePass.visible = false;
    this.scene.add(this.landscapePass);

    this.milkyWay = this.segments(data.milkyWay, palette.milkyWay, this.milkyWayOpacity, 0.15);
    this.milkyWay.renderOrder = -5;
    this.constellationBounds = this.segments(data.constellationBounds, palette.constellationBounds, {
      value: 0.35,
    });
    this.constellationLines = this.segments(data.constellationLines, palette.constellationLines, {
      value: 0.55,
    });
    this.altAz = this.lines(altAzGrid(), palette.altAzGrid, 0.35, this.horizonFrame);
    this.meridianLine = this.lines(meridian(), palette.meridian, 0.6, this.horizonFrame);
    this.eclipticLine = this.lines(ecliptic(), palette.ecliptic, 0.5);

    const built = this.buildStars(starMultiplier);
    this.stars = built.points;
    this.starCount = built.count;
    const dso = this.buildDsos();
    this.dsos = dso.points;
    this.dsoHighlight = dso.highlight;
  }

  /** The shared dome projection uniforms (the same objects, so one update reaches every material). */
  private domeUniforms(): Record<string, THREE.IUniform> {
    const s = this.shared;
    return {
      uDomeBasis: s.uDomeBasis,
      uProjK: s.uProjK,
      uProjScale: s.uProjScale,
      uAspect: s.uAspect,
      uMaxTheta: s.uMaxTheta,
    };
  }

  get starsDrawn(): number {
    return this.starCount;
  }

  private material(
    vertexShader: string,
    fragmentShader: string,
    uniforms: Record<string, THREE.IUniform>,
    frame: Record<string, THREE.IUniform> = {},
  ): THREE.ShaderMaterial {
    return new THREE.ShaderMaterial({
      vertexShader,
      fragmentShader,
      uniforms: { ...this.shared, ...frame, ...uniforms },
      transparent: true,
      depthTest: false,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
  }

  private lineMaterial(
    rgb: readonly [number, number, number],
    opacity: THREE.IUniform,
    groupFade: number,
    frame: Record<string, THREE.IUniform>,
  ): THREE.ShaderMaterial {
    return this.material(
      lineVertex,
      lineFragment,
      { uColor: { value: color(rgb) }, uOpacity: opacity, uGroupFade: { value: groupFade } },
      frame,
    );
  }

  private segments(
    table: SkyData['milkyWay'],
    rgb: readonly [number, number, number],
    opacity: THREE.IUniform,
    groupFade = 0,
  ): THREE.LineSegments {
    const { positions, groups } = segmentVertices(table);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('group', new THREE.BufferAttribute(groups, 1));
    const lines = new THREE.LineSegments(geometry, this.lineMaterial(rgb, opacity, groupFade, {}));
    lines.frustumCulled = false;
    this.scene.add(lines);
    return lines;
  }

  private lines(
    positions: Float32Array,
    rgb: readonly [number, number, number],
    opacity: number,
    frame: Record<string, THREE.IUniform> = {},
  ): THREE.LineSegments {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('group', new THREE.BufferAttribute(new Float32Array(positions.length / 3), 1));
    const lines = new THREE.LineSegments(geometry, this.lineMaterial(rgb, { value: opacity }, 0, frame));
    lines.frustumCulled = false;
    this.scene.add(lines);
    return lines;
  }

  private buildStars(multiplier: number): { points: THREE.Points; count: number } {
    const { stars } = this.data;
    const copies = Math.max(1, Math.floor(multiplier));
    const count = stars.count * copies;
    const positions = new Float32Array(count * 3);
    const magnitudes = new Float32Array(count);
    const colors = new Float32Array(count * 3);
    for (let copy = 0; copy < copies; copy++) {
      // Stress mode: extra copies are rotated a little so that they do not overlap.
      const angle = copy * 0.37;
      const c = Math.cos(angle);
      const s = Math.sin(angle);
      for (let i = 0; i < stars.count; i++) {
        const base = i * Star.stride;
        const x = stars.float32[base + Star.x] ?? 0;
        const y = stars.float32[base + Star.y] ?? 0;
        const z = stars.float32[base + Star.z] ?? 0;
        const k = copy * stars.count + i;
        positions[k * 3] = c * x - s * y;
        positions[k * 3 + 1] = s * x + c * y;
        positions[k * 3 + 2] = z;
        magnitudes[k] = stars.float32[base + Star.magnitude] ?? 99;
        const rgb = starColor(stars.float32[base + Star.colorIndex] ?? 0.65);
        colors.set(rgb, k * 3);
      }
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('magnitude', new THREE.BufferAttribute(magnitudes, 1));
    geometry.setAttribute('starColor', new THREE.BufferAttribute(colors, 3));
    const material = this.material(starVertex, starFragment, {
      uLimit: this.starLimitUniform,
      uStarScale: this.starScaleUniform,
      uPixelRatio: { value: this.pixelRatio },
    });
    const points = new THREE.Points(geometry, material);
    points.frustumCulled = false;
    this.scene.add(points);
    return { points, count };
  }

  private buildDsos(): { points: THREE.Points; highlight: Float32Array } {
    const { dsos, dsoIndex, dsoTypes } = this.data;
    const n = dsos.count;
    const positions = new Float32Array(n * 3);
    const magnitude = new Float32Array(n);
    const major = new Float32Array(n);
    const minor = new Float32Array(n);
    const pa = new Float32Array(n);
    const shape = new Float32Array(n);
    const highlight = new Float32Array(n);
    for (let i = 0; i < n; i++) {
      const base = i * Dso.stride;
      positions[i * 3] = dsos.float32[base + Dso.x] ?? 0;
      positions[i * 3 + 1] = dsos.float32[base + Dso.y] ?? 0;
      positions[i * 3 + 2] = dsos.float32[base + Dso.z] ?? 0;
      magnitude[i] = dsos.float32[base + Dso.magnitude] ?? UNKNOWN_MAGNITUDE;
      major[i] = dsos.float32[base + Dso.major] ?? 0;
      minor[i] = dsos.float32[base + Dso.minor] ?? 0;
      pa[i] = dsos.float32[base + Dso.positionAngle] ?? -1;
      shape[i] = dsoShape(dsoTypes[dsos.uint32[base + Dso.type] ?? 0] ?? '');
      const id = dsoIndex[i]?.[0];
      if (id !== undefined) {
        this.dsoIdIndex.set(id, i);
      }
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('magnitude', new THREE.BufferAttribute(magnitude, 1));
    geometry.setAttribute('majorAxis', new THREE.BufferAttribute(major, 1));
    geometry.setAttribute('minorAxis', new THREE.BufferAttribute(minor, 1));
    geometry.setAttribute('positionAngle', new THREE.BufferAttribute(pa, 1));
    geometry.setAttribute('shape', new THREE.BufferAttribute(shape, 1));
    geometry.setAttribute('highlight', new THREE.BufferAttribute(highlight, 1));
    const material = this.material(dsoVertex, dsoFragment, {
      ...this.dsoUniforms,
      uPixelRatio: { value: this.pixelRatio },
      uColors: { value: palette.dsoShapes.map(color) },
      uPlanColor: { value: color(palette.plan) },
      uSelectedColor: { value: color(palette.selected) },
    });
    material.blending = THREE.NormalBlending;
    const points = new THREE.Points(geometry, material);
    points.frustumCulled = false;
    points.renderOrder = 5;
    this.scene.add(points);
    return { points, highlight };
  }

  private buildSilhouette(): void {
    if (this.silhouette) {
      this.scene.remove(this.silhouette);
      this.silhouette.geometry.dispose();
      this.silhouette = null;
    }
    if (!hasObstruction(this.horizonPoints)) {
      return;
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(silhouetteTriangles(this.horizonPoints), 3));
    const material = new THREE.ShaderMaterial({
      vertexShader: silhouetteVertex,
      fragmentShader: silhouetteFragment,
      uniforms: {
        ...this.shared,
        ...this.horizonFrame,
        ...this.groundColors,
      },
      side: THREE.DoubleSide,
      // Transparent (though fully opaque) so that it is sorted with the stars and lines and
      // drawn after them (renderOrder): three.js draws every opaque object first.
      transparent: true,
      depthTest: false,
      depthWrite: false,
    });
    this.silhouette = new THREE.Mesh(geometry, material);
    this.silhouette.frustumCulled = false;
    this.silhouette.renderOrder = 20;
    this.scene.add(this.silhouette);
  }

  // ---- state from the bridge -------------------------------------------------------------

  setRefraction(model: RefractionModel): void {
    this.refraction = model;
    this.shared.uRefraction.value.set(model.a, model.b, model.c, model.d);
    this.shared.uRefractionMin.value = model.minimumAltitudeDegrees;
  }

  setFrame(frame: FrameTransform): void {
    this.frame = frame;
    const changed =
      this.equatorialPrecession === null ||
      frame.precession.some((v, i) => Math.abs(v - (this.equatorialPrecession?.[i] ?? 0)) > 1e-7);
    if (changed) {
      this.equatorialPrecession = frame.precession;
      if (this.equatorial) {
        this.scene.remove(this.equatorial);
        this.equatorial.geometry.dispose();
      }
      this.equatorial = this.lines(equatorialGrid(frame.precession), palette.equatorialGrid, 0.3);
    }
  }

  setObserver(observer: Observer): void {
    this.observer = observer;
    // Refraction scaled by the site's pressure (computed in C#, applied by the shader).
    this.setRefraction(observer.refraction);
    this.applyLandscape();
  }

  // ---- media (panorama, landscapes) ----------------------------------------------------------

  /**
   * The media folder's manifest: loads the panorama, and the site's landscape when it has one.
   * Called once, after the sky is built (the images arrive later; until then the outline Milky
   * Way and the horizon profile stay).
   */
  setMedia(baseUrl: string, manifest: MediaManifest): void {
    this.media = { baseUrl, manifest };
    const width = Math.min(manifest.panorama.width, this.maxTextureWidth(MEDIA_MAX_WIDTH));
    loadMediaImage(baseUrl, manifest.panorama.file, width)
      .then((image) => {
        const imageWidth = image.width;
        this.panorama = { texture: this.equirectangularTexture(image), width: imageWidth };
        this.panoramaUniforms.uPanorama.value = this.panorama.texture;
      })
      .catch((error: unknown) => {
        this.onMediaError?.(error);
      });
    this.applyLandscape();
  }

  /** Called when an image of the media folder could not be loaded (logged by the engine). */
  onMediaError: ((error: unknown) => void) | null = null;

  /** The landscapes of the media folder (empty without it). */
  get landscapes(): readonly LandscapeEntry[] {
    return this.media?.manifest.landscapes ?? [];
  }

  /**
   * Is the panorama visible at all (its contrast at the zenith, the darkest sky: natural light
   * over all of it, ≥ 10 % — a sky of SQM ≈ 19.5 or darker)? Its credit shows only then.
   */
  private get milkyWayVisible(): boolean {
    if (!this.layers.skyBrightness) {
      return true;
    }
    const a = this.appearance;
    return fluxOf(22) / fluxOf(a.zenithSqm) >= 0.1;
  }

  /** The images on screen now, whose credits the page shows (spec §8.3: CC BY). */
  get mediaCredits(): MediaCredit[] {
    const credits: MediaCredit[] = [];
    const panorama = this.media?.manifest.panorama;
    if (
      panorama &&
      this.panoramaUniforms.uPanoramaOn.value > 0 &&
      this.panoramaUniforms.uPanoramaGain.value > 0 &&
      this.milkyWayVisible
    ) {
      credits.push({ kind: 'panorama', author: panorama.credit.author, license: panorama.credit.license });
    }
    if (this.landscape?.texture && this.landscapePass.visible) {
      const { credit } = this.landscape.entry;
      credits.push({ kind: 'landscape', author: credit.author, license: credit.license });
    }
    return credits;
  }

  private maxTextureWidth(wanted: number): number {
    return Math.min(wanted, this.renderer.capabilities.maxTextureSize);
  }

  /**
   * An equirectangular image on the GPU, with mipmaps. The decoded image is uploaded now and
   * released (it would otherwise stay in the page's memory as well as in the GPU's).
   */
  private equirectangularTexture(image: ImageBitmap): THREE.Texture {
    const texture = new THREE.Texture(image);
    texture.flipY = false; // rows top-down: v = 0 at the top (ImageBitmaps are never flipped)
    texture.wrapS = THREE.RepeatWrapping;
    texture.wrapT = THREE.ClampToEdgeWrapping;
    texture.minFilter = THREE.LinearMipmapLinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = true;
    texture.anisotropy = Math.min(4, this.renderer.capabilities.getMaxAnisotropy());
    texture.needsUpdate = true;
    this.renderer.initTexture(texture);
    image.close();
    return texture;
  }

  /**
   * The site's landscape (setObserver.landscape): a landscape of the media folder replaces the
   * horizon profile (its own horizon cuts the labels; its outline is drawn until the image is
   * loaded), "flat" shows none, null the site's profile.
   */
  private applyLandscape(): void {
    const id = this.observer?.landscape ?? null;
    const entry = id === null ? null : (this.media?.manifest.landscapes.find((l) => l.id === id) ?? null);
    if (entry?.id !== this.landscape?.entry.id) {
      this.landscape?.texture?.dispose();
      this.landscapeUniforms.uLandscape.value = null;
      this.landscape = entry ? { entry, texture: null, width: 0 } : null;
      if (entry && this.media) {
        const requested = this.landscape;
        loadMediaImage(
          this.media.baseUrl,
          entry.file,
          Math.min(entry.width, this.maxTextureWidth(MEDIA_MAX_WIDTH)),
        )
          .then((image) => {
            if (this.landscape !== requested || !requested) {
              image.close();
              return;
            }
            requested.width = image.width;
            requested.texture = this.equirectangularTexture(image);
            this.landscapeUniforms.uLandscape.value = requested.texture;
            this.landscapeUniforms.uTopAltitude.value = entry.topAltitudeDegrees;
          })
          .catch((error: unknown) => {
            this.onMediaError?.(error);
          });
      }
    }
    this.horizonPoints = entry
      ? landscapeHorizon(entry)
      : id === 'flat'
        ? []
        : [...(this.observer?.horizon ?? [])].sort((a, b) => a.azimuth - b.azimuth);
    this.buildSilhouette();
  }

  /** Panorama and landscape uniforms for this frame (after the sky light is known). */
  private updateMedia(groundLight: number): void {
    const degreesPerPixel = 1 / Math.max(1e-6, this.pixelsPerDegree * this.pixelRatio);
    const finder = this.mode === 'finder';
    const p = this.panoramaUniforms;
    p.uPanoramaOn.value = this.panorama && this.layers.milkyWay && !finder ? 1 : 0;
    p.uPanoramaGain.value = PANORAMA_GAIN * (this.mode === 'dome' ? panoramaFieldFade(this.fov) : 1);
    p.uPanoramaLod.value = this.panorama ? equirectangularLod(this.panorama.width, degreesPerPixel) : 0;

    const l = this.landscapeUniforms;
    const ready = this.landscape?.texture ? this.landscape : null;
    this.landscapePass.visible = ready !== null && this.layers.ground && !finder;
    if (ready) {
      l.uLandscapeLod.value = equirectangularLod(ready.width, degreesPerPixel);
      // Daylight shows the photo as taken; the night darkens it to a faint, nearly grey shape
      // tinted by the sky at the horizon (moonlight and the towns' glow raise it).
      const k = groundLight;
      l.uLight.value.set(
        LANDSCAPE_NIGHT[0] * (1 - k) + k,
        LANDSCAPE_NIGHT[1] * (1 - k) + k,
        LANDSCAPE_NIGHT[2] * (1 - k) + k,
      );
      l.uSaturation.value = 0.25 + 0.75 * k;
    }
  }

  setBodies(bodies: MovingBody[]): void {
    this.bodies = bodies.map((body) => ({ body, startMs: Date.parse(body.track.startUtc) }));
  }

  setAppearance(appearance: SkyAppearance): void {
    this.appearanceTrack = toTrack(appearance);
  }

  setLayers(layers: Layers): void {
    this.layers = layers;
  }

  setHighlights(plan: readonly string[]): void {
    this.plan = new Set(plan);
    this.refreshHighlights();
  }

  setSelection(selection: Selection | null): void {
    const changed = selection?.kind !== this.selection?.kind || selection?.id !== this.selection?.id;
    this.selection = selection;
    this.refreshHighlights();
    if (changed && this.tracking) {
      this.stopTracking('selection');
    }
  }

  setFov(fov: Fov): void {
    this.fovState = fov.visible
      ? {
          widthDegrees: fov.widthDegrees,
          heightDegrees: fov.heightDegrees,
          rotationDegrees: fov.rotationDegrees,
          center: fov.center ? [fov.center.x, fov.center.y, fov.center.z] : (this.fovState?.center ?? null),
          label: fov.label,
        }
      : null;
    this.fovScreen = [];
  }

  /** The overlay's center, or null when it is hidden. */
  get fovCenter(): Vec3 | null {
    return this.fovState?.center ?? null;
  }

  setMode(mode: ViewMode): void {
    if (mode !== 'dome') {
      this.flight = null;
      if (this.tracking) {
        this.stopTracking('mode');
      }
    }
    if (mode === this.mode) {
      return;
    }
    if (mode === 'finder') {
      this.finderCenter = this.fovState?.center ?? this.viewCenter();
    }
    this.mode = mode;
  }

  private refreshHighlights(): void {
    this.dsoHighlight.fill(0);
    for (const id of this.plan) {
      const index = this.dsoIdIndex.get(id);
      if (index !== undefined) {
        this.dsoHighlight[index] = 1;
      }
    }
    if (this.selection?.kind === 'dso') {
      const index = this.dsoIdIndex.get(this.selection.id);
      if (index !== undefined) {
        this.dsoHighlight[index] = 2;
      }
    }
    const attribute = this.dsos.geometry.getAttribute('highlight');
    attribute.needsUpdate = true;
  }

  /** Visibility of every layer for the current layers and mode (cheap: set each frame). */
  private applyVisibility(): void {
    const l = this.layers;
    const dome = this.mode === 'dome';
    const finder = this.mode === 'finder';
    this.stars.visible = l.stars;
    this.constellationLines.visible = l.constellationLines;
    this.constellationBounds.visible = l.constellationBoundaries;
    // The panorama replaces the outline Milky Way once it is loaded.
    this.milkyWay.visible = l.milkyWay && !(this.panorama && !finder);
    // Alt-az lines are in the horizon frame: they have no place on the (equatorial) finder chart.
    this.altAz.visible = l.altAzGrid && !finder;
    this.meridianLine.visible = l.meridian && !finder;
    this.eclipticLine.visible = l.ecliptic;
    if (this.equatorial) {
      this.equatorial.visible = l.equatorialGrid;
    }
    this.dsos.visible = l.deepSky;
    this.ground.visible = dome && l.ground;
    this.skyPass.visible = !finder;
    if (this.silhouette) {
      this.silhouette.visible = !finder && l.ground && !this.landscape?.texture;
    }
    this.shared.uHideBelowHorizon.value = this.mode === 'allSky' ? 1 : finder ? 0 : l.ground ? 1 : 0;
  }

  // ---- view --------------------------------------------------------------------------------

  resize(width: number, height: number): void {
    this.width = Math.max(1, this.fixedSize?.width ?? width);
    this.height = Math.max(1, this.fixedSize?.height ?? height);
    this.renderer.setSize(this.width, this.height, false);
    const canvas = this.overlay.canvas;
    canvas.width = Math.round(this.width * this.pixelRatio);
    canvas.height = Math.round(this.height * this.pixelRatio);
    this.overlay.setTransform(this.pixelRatio, 0, 0, this.pixelRatio, 0, 0);
  }

  /** Field of view the current mode shows across the height, degrees. */
  private get effectiveFov(): number {
    return this.mode === 'dome' ? this.fov : this.mode === 'finder' ? this.finderFov : 180;
  }

  /** Pixels per degree at the view center. */
  private get pixelsPerDegree(): number {
    if (this.mode === 'dome') {
      return domePixelsPerDegree(this.dome);
    }
    return this.mode === 'finder' ? this.chart.scale * (Math.PI / 180) : this.chart.scale / 90;
  }

  /** Pans by a pointer drag from (x − dx, y − dy) to (x, y), CSS pixels ("grab the sky"). */
  pan(dx: number, dy: number, x: number, y: number): void {
    if (this.mode === 'dome') {
      const degreesPerPixel = 1 / this.pixelsPerDegree;
      this.azimuth = (((this.azimuth - dx * degreesPerPixel) % 360) + 360) % 360;
      this.altitude = Math.min(89.9, Math.max(-30, this.altitude + dy * degreesPerPixel));
    } else if (this.mode === 'finder') {
      const from = this.unproject(x - dx, y - dy);
      const to = this.unproject(x, y);
      if (from && to) {
        this.finderCenter = rotateTowards(this.finderCenter, to, from);
        this.updateChart();
      }
    }
  }

  zoom(factor: number): void {
    if (this.mode === 'dome') {
      this.fov = clampFov(this.fov * factor, MIN_FOV, MAX_FOV);
    } else if (this.mode === 'finder') {
      this.finderFov = clampFov(this.finderFov * factor, MIN_FINDER_FOV, MAX_FINDER_FOV);
    }
  }

  // ---- camera motion (spec §8.1, §9.2) --------------------------------------------------------

  /**
   * Flies the dome to a J2000 direction (and field), following it while the sky turns; the
   * finder recenters at once and the all-sky chart only keeps the direction for the dome.
   */
  flyTo(j2000: Vec3, fovDegrees: number | null, nowMs: number): void {
    if (this.mode !== 'dome') {
      this.centerOn(j2000, fovDegrees);
      return;
    }
    const fov = fovDegrees === null ? this.fov : clampFov(fovDegrees, MIN_FOV, MAX_FOV);
    this.startFlight({ kind: 'direction', j2000, fov }, nowMs);
  }

  /** Flies the dome back to the south, 30° up, 70° field ("Home"). */
  flyHome(nowMs: number): void {
    if (this.tracking) {
      this.stopTracking('manual');
    }
    if (this.mode !== 'dome') {
      this.azimuth = HOME_VIEW.azimuth;
      this.altitude = HOME_VIEW.altitude;
      this.fov = HOME_VIEW.fov;
      return;
    }
    this.startFlight({ kind: 'view', view: HOME_VIEW }, nowMs);
  }

  /**
   * Keeps the selection centred while time runs (dome only): flies to it first. Returns false
   * when there is nothing to track (no selection, another mode or no position).
   */
  startTracking(nowMs: number, utcMs: number): boolean {
    const selection = this.selection;
    if (this.mode !== 'dome' || !selection || !this.selectionPosition(selection, utcMs)) {
      return false;
    }
    this.tracking = selection;
    this.trackedFixed = selection.kind === 'body' ? null : this.selectionPosition(selection, utcMs);
    this.startFlight({ kind: 'selection', fov: this.fov }, nowMs);
    this.onTrackingChanged?.(true, 'started');
    return true;
  }

  /** Stops tracking and says why ('manual': the user moved the view). */
  stopTracking(reason: TrackingStop): void {
    if (!this.tracking) {
      return;
    }
    this.tracking = null;
    if (this.flight?.target.kind === 'selection') {
      this.flight = null;
    }
    this.onTrackingChanged?.(false, reason);
  }

  /** Where the tracked object is: stars and deep-sky objects do not move (no catalog scan per frame). */
  private trackedPosition(utcMs: number): Vec3 | null {
    if (!this.tracking) {
      return null;
    }
    return this.trackedFixed ?? this.selectionPosition(this.tracking, utcMs);
  }

  get isTracking(): boolean {
    return this.tracking !== null;
  }

  /** The user moved the view by hand: a flight stops, and so does tracking. */
  interrupt(): void {
    this.flight = null;
    this.stopTracking('manual');
  }

  private startFlight(target: FlightTarget, nowMs: number): void {
    const view = this.flightTarget(target, null);
    if (!view) {
      return;
    }
    this.flight = {
      flight: new Flight({ azimuth: this.azimuth, altitude: this.altitude, fov: this.fov }, view, nowMs),
      target,
    };
  }

  /** Where a flight is going right now (the sky turns), or null when the target has no position. */
  private flightTarget(target: FlightTarget, utcMs: number | null): DomeView | null {
    if (target.kind === 'view') {
      return target.view;
    }
    const j2000 = target.kind === 'direction' ? target.j2000 : this.trackedPosition(utcMs ?? this.lastUtcMs);
    if (!j2000) {
      return null;
    }
    const h = apparentHorizontal(this.currentH, j2000, this.refraction);
    return { azimuth: h.azimuthDegrees, altitude: h.altitudeDegrees, fov: target.fov };
  }

  /** Advances a flight and tracking to this frame (before {@link render}); true when a flight arrived. */
  animate(nowMs: number, utcMs: number): boolean {
    this.lastUtcMs = utcMs;
    this.frameMs = nowMs;
    if (this.frame) {
      this.currentH = j2000ToHorizontal(this.frame, utcMs);
    }
    if (this.flight) {
      const target = this.flightTarget(this.flight.target, utcMs);
      if (!target) {
        this.flight = null;
        return false;
      } else {
        const { view, done } = this.flight.flight.at(nowMs, target);
        this.azimuth = view.azimuth;
        this.altitude = view.altitude;
        this.fov = clampFov(view.fov, MIN_FOV, MAX_FOV);
        if (done) {
          this.flight = null;
        }
        return done;
      }
    }
    if (this.tracking) {
      const j2000 = this.trackedPosition(utcMs);
      if (!j2000) {
        this.stopTracking('lost');
        return false;
      }
      const h = apparentHorizontal(this.currentH, j2000, this.refraction);
      this.azimuth = h.azimuthDegrees;
      this.altitude = Math.min(MAX_ALTITUDE, Math.max(MIN_ALTITUDE, h.altitudeDegrees));
    }
    return false;
  }

  /** Stops a flight where it is (a manual zoom); tracking, if on, carries on. */
  stopFlight(): void {
    this.flight = null;
  }

  /** True while a flight runs (the engine reports the view once it ends). */
  get isFlying(): boolean {
    return this.flight !== null;
  }

  /** Turns the view to a J2000 direction at the current time (the finder recenters instead). */
  centerOn(j2000: Vec3, fovDegrees: number | null): void {
    if (this.mode === 'finder') {
      this.finderCenter = j2000;
      if (fovDegrees !== null) {
        this.finderFov = clampFov(fovDegrees, MIN_FINDER_FOV, MAX_FINDER_FOV);
      }
      return;
    }
    const horizontal = apparentHorizontal(this.currentH, j2000, this.refraction);
    this.azimuth = horizontal.azimuthDegrees;
    this.altitude = Math.min(89.9, Math.max(-30, horizontal.altitudeDegrees));
    if (fovDegrees !== null) {
      this.fov = clampFov(fovDegrees, MIN_FOV, MAX_FOV);
    }
  }

  /** Fainter stars appear as the field narrows (magnitudes). */
  private get fieldBonus(): number {
    return Math.min(3, Math.max(0, 2 * Math.log10(60 / this.effectiveFov)));
  }

  /**
   * Star limit at the zenith: fainter stars appear as the field narrows; a bright sky hides
   * them. The darkest part of the sky, so no star fainter than this is drawn anywhere (labels,
   * picking, the finder chart); the dome applies the limit of each star's own sky.
   */
  get starLimit(): number {
    const bonus = this.fieldBonus;
    const limit = Math.min(9.5, this.layers.starLimitMagnitude + bonus);
    return this.layers.skyBrightness ? Math.min(limit, starCap(this.appearance) + bonus) : limit;
  }

  /**
   * Faintest catalog star drawn at the middle of the view (the HUD): the limit of the sky there,
   * less what the air in front takes away.
   */
  get viewLimit(): number {
    if (this.mode !== 'dome' || !this.layers.skyBrightness) {
      return this.starLimit;
    }
    const bonus = this.fieldBonus;
    const moon = this.moonHorizontal;
    const center = fromHorizontal(this.altitude, this.azimuth);
    const separation = moon
      ? separationDegrees(center, fromHorizontal(moon.altitudeDegrees, moon.azimuthDegrees))
      : 180;
    const sqm = skyBrightnessAt(this.appearance, this.altitude, moon?.altitudeDegrees ?? -90, separation);
    const limit = Math.min(9.5, this.layers.starLimitMagnitude + bonus, nakedEyeLimit(sqm) + 2 + bonus);
    return limit - extinctionAt(Math.max(this.altitude, -6), this.appearance.extinction);
  }

  private get dsoLimit(): number {
    const loss = this.layers.skyBrightness ? deepSkyLoss(this.appearance) : 0;
    return this.layers.deepSkyLimitMagnitude + this.fieldBonus - loss;
  }

  // ---- sky rendering options (the page's preferences) -------------------------------------------

  /** Atmosphere quality: the scattering table's size and samples (Low for integrated GPUs). */
  setQuality(quality: SkyQuality): void {
    this.quality = quality;
    this.atmosphere.setQuality(quality);
    this.shared.uScintillation.value = this.scintillation && quality === 'high' ? 1 : 0;
  }

  get skyQuality(): SkyQuality {
    return this.quality;
  }

  setLightPollutionTint(tint: LightPollutionTint): void {
    this.skyPassUniforms.uTintChroma.value.copy(unitChroma(TINTS[tint]));
  }

  /** Twinkling stars (High quality only). */
  setScintillation(on: boolean): void {
    this.scintillation = on;
    this.shared.uScintillation.value = on && this.quality === 'high' ? 1 : 0;
  }

  /** The Sun and the Moon now, north/east/up (null without their tracks). */
  private bodyNeu(kind: 'sun' | 'moon', utcMs: number): Vec3 | null {
    const track = this.bodies.find((t) => t.body.kind === kind);
    return track ? transform(this.currentH, this.bodyPosition(track, utcMs)) : null;
  }

  private moonHorizontal: Horizontal | null = null;

  /** Sky brightness uniforms, the scattering table and the ground light for this frame. */
  private updateSky(utcMs: number): void {
    const a = this.appearance;
    const on = this.layers.skyBrightness;
    const s = this.shared;
    const sun = this.bodyNeu('sun', utcMs);
    const moon = this.bodyNeu('moon', utcMs);
    this.moonHorizontal = moon ? toHorizontal(moon) : null;
    s.uBrightnessOn.value = on ? 1 : 0;
    s.uSiteFlux.value = fluxOf(a.siteSqm);
    s.uTwilightFlux.value = on ? fluxOf(a.twilightSqm) : 0;
    s.uMoonFlux.value = on && moon ? fluxOf(a.moonlightSqm) : 0;
    s.uExtinction.value = a.extinction;
    s.uBrightening.value = a.brighteningPerAirmass;
    s.uFieldBonus.value = this.fieldBonus;
    s.uTime.value = this.frameMs / 1000;
    if (sun) {
      s.uSunNeu.value.set(sun[0], sun[1], sun[2]);
    }
    if (moon) {
      s.uMoonNeu.value.set(moon[0], moon[1], moon[2]);
    }
    if (on && sun && s.uTwilightFlux.value > 0 && this.mode !== 'finder') {
      this.atmosphere.update(this.renderer, toHorizontal(sun).altitudeDegrees);
      s.uHasAtmosphere.value = this.atmosphere.texture.value ? 1 : 0;
    } else {
      s.uHasAtmosphere.value = 0;
    }
    const buffer = this.renderer.getDrawingBufferSize(new THREE.Vector2());
    this.skyPassUniforms.uViewport.value.copy(buffer);

    // The ground follows the light of the sky at the horizon: night dark, daylight grey-green.
    const horizonSqm = skyBrightnessAt(a, 0, this.moonHorizontal?.altitudeDegrees ?? -90, 90);
    const light = Math.min(
      1,
      Math.max(0, (displayLevel(horizonSqm) - displayLevel(22)) / (displayLevel(4) - displayLevel(22))),
    );
    const mix = (night: Rgb, day: Rgb, target: THREE.Color): void => {
      target.setRGB(
        night[0] + (day[0] - night[0]) * light,
        night[1] + (day[1] - night[1]) * light,
        night[2] + (day[2] - night[2]) * light,
      );
    };
    mix(palette.ground, DAY_GROUND, this.groundColors.uGround.value);
    mix(palette.horizon, DAY_HORIZON, this.groundColors.uHorizon.value);
    this.updateMedia(light);
  }

  /** The sky brightness now (for diagnostics and tests). */
  get appearanceNow(): AppearanceNow {
    return this.appearance;
  }

  /** Canvas position (CSS px) of a dome world direction, or null when behind the camera / off screen. */
  private project(world: Vec3, margin = 40): ScreenPoint | null {
    return this.onScreen(domeProject(this.dome, world), margin);
  }

  private onScreen(p: ScreenPoint | null, margin: number): ScreenPoint | null {
    return p && p.x >= -margin && p.y >= -margin && p.x <= this.width + margin && p.y <= this.height + margin
      ? p
      : null;
  }

  /** Is an apparent position above the ground (and the horizon profile) in the current mode? */
  private aboveHorizon(h: Horizontal): boolean {
    if (this.mode === 'finder') {
      return true;
    }
    if (this.mode === 'dome' && !this.layers.ground) {
      return true;
    }
    const mask = this.layers.ground ? maskAltitude(this.horizonPoints, h.azimuthDegrees) : 0;
    return h.altitudeDegrees >= Math.max(0, mask);
  }

  /** Screen position of an apparent horizontal direction (null in the finder or off screen). */
  private placeHorizontal(altitudeDegrees: number, azimuthDegrees: number, margin = 40): ScreenPoint | null {
    if (this.mode === 'allSky') {
      return this.onScreen(allSkyPoint(altitudeDegrees, azimuthDegrees, this.chart), margin);
    }
    if (this.mode === 'dome') {
      return this.project(neuToWorld(fromHorizontal(altitudeDegrees, azimuthDegrees)), margin);
    }
    return null;
  }

  /**
   * Screen position of a J2000 direction in the current mode, or null when it is hidden (below
   * the ground or the horizon profile when `cut`) or off screen by more than `margin` pixels.
   */
  private place(j2000: Vec3, margin = 40, cut = true): Placed | null {
    if (this.mode === 'finder') {
      const p = this.onScreen(gnomonic(this.finderBasis, j2000, this.chart), margin);
      return p ? { ...p, altitude: 90, azimuth: 0 } : null;
    }
    const h = apparentHorizontal(this.currentH, j2000, this.refraction);
    if (cut && !this.aboveHorizon(h)) {
      return null;
    }
    const p = this.placeHorizontal(h.altitudeDegrees, h.azimuthDegrees, margin);
    return p ? { ...p, altitude: h.altitudeDegrees, azimuth: h.azimuthDegrees } : null;
  }

  /** J2000 direction under a screen point (refraction neglected), or null off the all-sky circle. */
  unproject(x: number, y: number): Vec3 | null {
    if (this.mode === 'finder') {
      return gnomonicInverse(this.finderBasis, x, y, this.chart);
    }
    let neu: Vec3;
    if (this.mode === 'allSky') {
      const h = allSkyInverse(x, y, this.chart);
      if (h.altitudeDegrees < -1) {
        return null;
      }
      neu = fromHorizontal(h.altitudeDegrees, h.azimuthDegrees);
    } else {
      neu = worldToNeu(domeInverse(this.dome, x, y));
    }
    return transform(transpose(this.currentH), neu);
  }

  /** The sky under a screen point: J2000 direction and apparent altitude/azimuth (null off the sky). */
  pointAt(x: number, y: number): { j2000: Vec3; horizontal: Horizontal } | null {
    const j2000 = this.unproject(x, y);
    return j2000 ? { j2000, horizontal: apparentHorizontal(this.currentH, j2000, this.refraction) } : null;
  }

  /** J2000 direction at the middle of the view. */
  viewCenter(): Vec3 {
    if (this.mode === 'finder') {
      return this.finderCenter;
    }
    if (this.mode === 'allSky') {
      return transform(transpose(this.currentH), [0, 0, 1]);
    }
    return transform(transpose(this.currentH), fromHorizontal(this.altitude, this.azimuth));
  }

  // ---- field-of-view overlay -------------------------------------------------------------

  /** True when (x, y) is inside the field-of-view rectangle on screen. */
  fovContains(x: number, y: number): boolean {
    if (!this.fovState || this.fovScreen.length < 4 || this.fovScreen.some((p) => p === null)) {
      return false;
    }
    return insidePolygon(this.fovScreen as ScreenPoint[], x, y);
  }

  /** Drags the overlay so that the sky point under (fromX, fromY) follows the pointer to (toX, toY). */
  moveFov(fromX: number, fromY: number, toX: number, toY: number): void {
    const center = this.fovState?.center;
    const from = this.unproject(fromX, fromY);
    const to = this.unproject(toX, toY);
    if (this.fovState && center && from && to) {
      this.fovState.center = rotateTowards(center, from, to);
      this.onFovChanged?.(this.fovState.center, this.fovState.rotationDegrees);
    }
  }

  rotateFov(deltaDegrees: number): void {
    if (this.fovState?.center) {
      this.fovState.rotationDegrees = (((this.fovState.rotationDegrees + deltaDegrees) % 360) + 360) % 360;
      this.onFovChanged?.(this.fovState.center, this.fovState.rotationDegrees);
    }
  }

  // ---- frame -------------------------------------------------------------------------------

  private updateChart(): void {
    if (this.mode === 'allSky') {
      this.chart = allSkyGeometry(this.width, this.height);
    } else if (this.mode === 'finder') {
      this.chart = finderGeometry(this.width, this.height, this.finderFov);
      this.finderBasis = tangentBasis(this.finderCenter);
      const b = this.finderBasis;
      this.shared.uFinder.value.set(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8]);
    }
    this.shared.uChartScale.value.set(
      (2 * this.chart.scale) / this.width,
      (2 * this.chart.scale) / this.height,
    );
  }

  /** The dome camera and its shader uniforms for the current direction and field. */
  private updateDome(): void {
    this.dome = domeCamera(this.altitude, this.azimuth, this.fov, this.width, this.height);
    const b = this.dome.basis;
    const k = domeK(this.fov);
    const s = this.shared;
    s.uDomeBasis.value.set(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7], b[8]);
    s.uProjK.value = k;
    s.uProjScale.value = (2 * domeScale(this.dome)) / this.height;
    s.uAspect.value = this.width / this.height;
    s.uMaxTheta.value = domeMaxAngle(k);
  }

  render(utcMs: number): void {
    if (this.frame) {
      this.currentH = j2000ToHorizontal(this.frame, utcMs);
    }
    const h = this.currentH;
    this.shared.uFrame.value.set(h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], h[8]);
    this.horizonFrame.uFrame.value.identity();

    this.appearance = this.layers.skyBrightness ? appearanceAt(this.appearanceTrack, utcMs) : DARK_SKY;
    const zenith = skyColor(this.appearance.zenithSqm);
    this.updateSky(utcMs);
    const background = this.scene.background as THREE.Color;
    if (this.mode === 'allSky') {
      background.set(palette.background);
    } else {
      // The palette is sRGB: the shaders output it as is, while the scene background is
      // color-managed (linear → sRGB), so it must be told the values are sRGB already.
      background.setRGB(zenith[0], zenith[1], zenith[2], THREE.SRGBColorSpace);
    }
    this.milkyWayOpacity.value =
      MILKY_WAY_OPACITY * (this.layers.skyBrightness ? milkyWayFactor(this.appearance) : 1);

    this.shared.uMode.value = this.mode === 'dome' ? 0 : this.mode === 'allSky' ? 1 : 2;
    this.updateChart();
    this.applyVisibility();

    this.updateDome();

    // The dome and the all-sky chart limit each star by its own sky (shaders.ts); the finder by the zenith.
    const finder = this.mode === 'finder';
    this.starLimitUniform.value = finder
      ? this.starLimit
      : Math.min(9.5, this.layers.starLimitMagnitude + this.fieldBonus);
    this.starScaleUniform.value =
      this.mode === 'finder' ? 1.6 : this.mode === 'dome' ? domeStarScale(this.fov) : 1;
    this.dsoUniforms.uLimit.value = finder
      ? this.dsoLimit
      : this.layers.deepSkyLimitMagnitude + this.fieldBonus;
    this.dsoUniforms.uPixelsPerDegree.value = this.pixelsPerDegree;
    this.dsoUniforms.uViewport.value.set(this.width, this.height);

    if (this.fovState && !this.fovState.center) {
      this.fovState.center = this.viewCenter();
      this.onFovChanged?.(this.fovState.center, this.fovState.rotationDegrees);
    }

    this.renderer.render(this.scene, this.camera);
    this.drawOverlay(utcMs);
  }

  // ---- overlay -----------------------------------------------------------------------------

  private bodyPosition(track: BodyTrack, utcMs: number): Vec3 {
    return interpolateTrack(track.body.track.vectors, track.startMs, track.body.track.stepSeconds, utcMs);
  }

  /** Screen directions of celestial north and east at a J2000 point (unit vectors in canvas px). */
  private screenAxes(j2000: Vec3, at: ScreenPoint): { north: ScreenPoint; east: ScreenPoint } | null {
    const offset = (direction: Vec3): ScreenPoint | null => {
      const p: Vec3 = [
        j2000[0] + direction[0] * 1e-3,
        j2000[1] + direction[1] * 1e-3,
        j2000[2] + direction[2] * 1e-3,
      ];
      const s = this.place(p, 1e6, false);
      if (!s) {
        return null;
      }
      const length = Math.hypot(s.x - at.x, s.y - at.y) || 1;
      return { x: (s.x - at.x) / length, y: (s.y - at.y) / length };
    };
    const north = offset([-j2000[0] * j2000[2], -j2000[1] * j2000[2], 1 - j2000[2] * j2000[2]]);
    const east = offset([-j2000[1], j2000[0], 0]);
    return north && east ? { north, east } : null;
  }

  private drawOverlay(utcMs: number): void {
    const g = this.overlay;
    g.clearRect(0, 0, this.width, this.height);
    g.font = '12px "Segoe UI", sans-serif';
    g.textBaseline = 'middle';
    const occupied = new Set<string>();
    const label = (text: string, x: number, y: number, style: string, force = false): void => {
      const key = `${Math.round(x / 70)}:${Math.round(y / 16)}`;
      if (!force && occupied.has(key)) {
        return;
      }
      occupied.add(key);
      g.fillStyle = style;
      g.fillText(text, x, y);
    };
    const labels = this.layers.labels;

    if (this.mode === 'allSky') {
      this.drawAllSkyFrame();
    }

    if (this.layers.constellationNames) {
      g.textAlign = 'center';
      for (const constellation of this.data.constellations) {
        const p = this.place(constellation.label);
        if (p) {
          label(constellation.name.toUpperCase(), p.x, p.y, palette.constellationLabel);
        }
      }
    }

    g.textAlign = 'left';
    if (labels && this.layers.stars) {
      if (this.mode === 'finder') {
        for (const item of this.finderStarLabels()) {
          const p = this.place(item.vector);
          if (p) {
            label(item.text, p.x + 5, p.y - 6, palette.label);
          }
        }
      } else {
        // Label density follows the field: only the brightest names in a very wide view.
        const limit = Math.min(this.effectiveFov > WIDE_LABEL_FOV ? 1.5 : 2.5, this.starLimit - 3.5);
        const { stars, starNames } = this.data;
        for (let i = 0; i < stars.count; i++) {
          const magnitude = stars.float32[i * Star.stride + Star.magnitude] ?? 99;
          if (magnitude > limit) {
            break;
          }
          const name = starNames.get(stars.uint32[i * Star.stride + Star.hygId] ?? 0);
          if (!name) {
            continue;
          }
          const p = this.place(this.starVector(i));
          if (p) {
            label(name, p.x + 6, p.y - 6, palette.label);
          }
        }
      }
    }

    if (labels && this.layers.deepSky) {
      const showAll = this.effectiveFov < 15;
      const showMessier = this.effectiveFov <= WIDE_LABEL_FOV;
      this.data.dsoIndex.forEach(([id], i) => {
        const highlighted = this.plan.has(id) || (this.selection?.kind === 'dso' && this.selection.id === id);
        if (!highlighted && !((showMessier && /^M\d/.test(id)) || (showAll && this.dsoVisible(i)))) {
          return;
        }
        const p = this.place(this.dsoVector(i));
        if (p) {
          label(id, p.x + 8, p.y + 8, highlighted ? '#ffb45e' : palette.label, highlighted);
        }
      });
    }

    for (const track of this.bodies) {
      if (track.body.kind === 'satellite') {
        if (this.layers.satellites) {
          this.drawSatellite(track, utcMs, labels ? label : null);
        }
      } else if (this.layers.bodies) {
        // In a very wide view only the Sun, the Moon, the planets and what is selected or
        // planned keep their names (minor bodies would bury the sky in labels).
        const minor = track.body.kind === 'comet' || track.body.kind === 'asteroid';
        const named =
          !minor ||
          this.effectiveFov <= WIDE_LABEL_FOV ||
          this.plan.has(track.body.id) ||
          (this.selection?.kind === 'body' && this.selection.id === track.body.id);
        this.drawBody(track, utcMs, labels && named ? label : null);
      }
    }

    if (this.selection && this.selection.kind !== 'dso') {
      const position = this.selectionPosition(this.selection, utcMs);
      const p = position ? this.place(position) : null;
      if (p) {
        g.strokeStyle = '#ffffff';
        g.lineWidth = 1.5;
        g.beginPath();
        g.arc(p.x, p.y, 12, 0, Math.PI * 2);
        g.stroke();
      }
    }

    this.drawFov();

    if (this.mode === 'dome' && this.layers.ground) {
      g.textAlign = 'center';
      g.font = 'bold 14px "Segoe UI", sans-serif';
      CARDINALS.forEach((name, i) => {
        const p = this.placeHorizontal(0, i * 45);
        if (p) {
          label(name, p.x, p.y - 12, palette.cardinal, true);
        }
      });
    } else if (this.mode === 'finder') {
      this.drawFinderFrame();
    }

    if (this.showStats && this.statsText) {
      g.textAlign = 'left';
      g.font = '12px Consolas, monospace';
      g.fillStyle = '#7fe08a';
      g.fillText(this.statsText, 8, 14);
    }
  }

  /** All-sky chart: horizon circle, minimum altitude and cardinal points around it. */
  private drawAllSkyFrame(): void {
    const g = this.overlay;
    const { cx, cy, scale } = this.chart;
    g.strokeStyle = 'rgba(255, 180, 94, 0.55)';
    g.lineWidth = 1.5;
    g.beginPath();
    g.arc(cx, cy, scale, 0, Math.PI * 2);
    g.stroke();
    const minimum = this.observer?.minimumAltitudeDegrees ?? 0;
    if (minimum > 0) {
      g.save();
      g.setLineDash([5, 5]);
      g.strokeStyle = 'rgba(255, 180, 94, 0.35)';
      g.lineWidth = 1;
      g.beginPath();
      g.arc(cx, cy, ((90 - minimum) / 90) * scale, 0, Math.PI * 2);
      g.stroke();
      g.restore();
    }
    g.textAlign = 'center';
    g.font = 'bold 14px "Segoe UI", sans-serif';
    g.fillStyle = palette.cardinal;
    CARDINALS.forEach((name, i) => {
      const az = i * 45 * (Math.PI / 180);
      const r = scale + (i % 2 === 0 ? 16 : 14);
      g.font = i % 2 === 0 ? 'bold 14px "Segoe UI", sans-serif' : '11px "Segoe UI", sans-serif';
      g.fillText(name, cx - r * Math.sin(az), cy - r * Math.cos(az));
    });
  }

  /** Finder chart: center coordinates, field, compass and a scale bar. */
  private drawFinderFrame(): void {
    const g = this.overlay;
    const { raHours, decDegrees } = raDec(this.finderCenter);
    const raH = Math.floor(raHours);
    const raM = (raHours - raH) * 60;
    const sign = decDegrees < 0 ? '−' : '+';
    const decAbs = Math.abs(decDegrees);
    const decD = Math.floor(decAbs);
    const decM = Math.round((decAbs - decD) * 60);
    const text =
      `α ${raH.toString().padStart(2, '0')}h ${raM.toFixed(1).padStart(4, '0')}m   ` +
      `δ ${sign}${decD.toString().padStart(2, '0')}° ${decM.toString().padStart(2, '0')}′   ` +
      (this.finderFov < 2 ? `${(this.finderFov * 60).toFixed(0)}′` : `${this.finderFov.toFixed(1)}°`);
    g.font = '12px "Segoe UI", sans-serif';
    g.textAlign = 'left';
    g.fillStyle = palette.label;
    g.fillText(text, 10, this.height - 14);

    // Compass: north up, east left (a mirror image of a map, as on the sky).
    const x = this.width - 40;
    const y = 44;
    g.strokeStyle = palette.cardinal;
    g.fillStyle = palette.cardinal;
    g.lineWidth = 1.5;
    g.beginPath();
    g.moveTo(x, y);
    g.lineTo(x, y - 24);
    g.moveTo(x, y);
    g.lineTo(x - 24, y);
    g.stroke();
    g.textAlign = 'center';
    g.font = 'bold 12px "Segoe UI", sans-serif';
    g.fillText('N', x, y - 32);
    g.fillText('E', x - 32, y);

    // Scale bar of a round angle about a fifth of the width.
    const pixelsPerDegree = this.pixelsPerDegree;
    const target = this.width / 5 / pixelsPerDegree;
    const steps = [1 / 60, 5 / 60, 10 / 60, 15 / 60, 0.5, 1, 2, 5, 10, 20];
    const angle = steps.reduce((best, s) => (Math.abs(s - target) < Math.abs(best - target) ? s : best), 1);
    const length = angle * pixelsPerDegree;
    const bx = this.width - 20 - length;
    const by = this.height - 14;
    g.beginPath();
    g.moveTo(bx, by);
    g.lineTo(bx + length, by);
    g.moveTo(bx, by - 4);
    g.lineTo(bx, by + 4);
    g.moveTo(bx + length, by - 4);
    g.lineTo(bx + length, by + 4);
    g.stroke();
    g.font = '11px "Segoe UI", sans-serif';
    g.fillText(angle < 1 ? `${Math.round(angle * 60)}′` : `${angle}°`, bx + length / 2, by - 10);
  }

  /** The camera field: the sensor rectangle, a tick on its "up" side and its label. */
  private drawFov(): void {
    const fov = this.fovState;
    if (!fov?.center) {
      this.fovScreen = [];
      return;
    }
    const g = this.overlay;
    const outline = fovOutline(fov.center, fov.widthDegrees, fov.heightDegrees, fov.rotationDegrees);
    this.fovScreen = outline.map((v) => this.place(v, 1e5, false));
    g.strokeStyle = FOV_COLOR;
    g.lineWidth = 1.6;
    g.beginPath();
    let drawing = false;
    for (const p of this.fovScreen) {
      if (!p) {
        drawing = false;
        continue;
      }
      if (drawing) {
        g.lineTo(p.x, p.y);
      } else {
        g.moveTo(p.x, p.y);
        drawing = true;
      }
    }
    g.stroke();

    const steps = (outline.length - 1) / 4;
    const topMiddle = this.fovScreen[Math.round(steps / 2)];
    const tick = this.place(fovUpMarker(fov.center, fov.heightDegrees, fov.rotationDegrees), 1e5, false);
    if (topMiddle && tick) {
      g.beginPath();
      g.moveTo(topMiddle.x, topMiddle.y);
      g.lineTo(tick.x, tick.y);
      g.stroke();
    }
    const corner = this.fovScreen[0];
    if (corner && fov.label) {
      g.font = '12px "Segoe UI", sans-serif';
      g.textAlign = 'left';
      g.fillStyle = FOV_COLOR;
      g.fillText(fov.label, corner.x + 4, corner.y - 9);
    }
  }

  /** Named stars and magnitudes for the finder (recomputed only when the chart changes). */
  private finderStarLabels(): FinderLabel[] {
    const limit = Math.min(this.starLimit - 1.5, 11);
    const c = this.finderCenter;
    const key = `${c.map((v) => v.toFixed(5)).join(',')}|${this.finderFov.toFixed(3)}|${this.width}x${this.height}|${limit.toFixed(1)}`;
    if (key === this.finderLabels.key) {
      return this.finderLabels.items;
    }
    const halfDiagonal =
      Math.atan(Math.hypot(this.width, this.height) / 2 / this.chart.scale) * (180 / Math.PI);
    const cosLimit = Math.cos(Math.min(89, halfDiagonal + 0.5) * (Math.PI / 180));
    const items: FinderLabel[] = [];
    const { stars, starNames } = this.data;
    for (let i = 0; i < stars.count && items.length < FINDER_LABELS; i++) {
      const magnitude = stars.float32[i * Star.stride + Star.magnitude] ?? 99;
      if (magnitude > limit) {
        break;
      }
      const v = this.starVector(i);
      if (v[0] * c[0] + v[1] * c[1] + v[2] * c[2] < cosLimit) {
        continue;
      }
      const name = starNames.get(stars.uint32[i * Star.stride + Star.hygId] ?? 0);
      items.push({ vector: v, text: name ?? magnitude.toFixed(1) });
    }
    this.finderLabels = { key, items };
    return items;
  }

  private drawBody(
    track: BodyTrack,
    utcMs: number,
    label: ((text: string, x: number, y: number, style: string, force?: boolean) => void) | null,
  ): void {
    const j2000 = this.bodyPosition(track, utcMs);
    const p = this.place(j2000);
    if (!p) {
      return;
    }
    const g = this.overlay;
    const body = track.body;
    const minimumRadius = { planet: 2.5, comet: 3, asteroid: 1.8, satellite: 2, sun: 6, moon: 6 }[body.kind];
    const radius = Math.max(minimumRadius, ((body.diameterArcsec / 3600) * this.pixelsPerDegree) / 2);

    if (body.kind === 'sun') {
      const glow = g.createRadialGradient(p.x, p.y, radius * 0.5, p.x, p.y, radius * 4);
      glow.addColorStop(0, 'rgba(255, 236, 170, 0.8)');
      glow.addColorStop(1, 'rgba(255, 236, 170, 0)');
      g.fillStyle = glow;
      g.beginPath();
      g.arc(p.x, p.y, radius * 4, 0, Math.PI * 2);
      g.fill();
      g.fillStyle = '#fff3c4';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    } else if (body.kind === 'moon') {
      this.drawMoon(p, radius, j2000, body);
    } else if (body.kind === 'comet') {
      this.drawComet(p, radius, j2000, body);
    } else if (body.kind === 'asteroid') {
      g.fillStyle = '#cfc8bc';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    } else {
      g.fillStyle = '#f5c26b';
      g.beginPath();
      g.arc(p.x, p.y, radius, 0, Math.PI * 2);
      g.fill();
    }

    if (label) {
      g.textAlign = 'left';
      label(body.name, p.x + radius + 4, p.y - radius - 2, palette.bodyLabel, true);
    }
  }

  /** Whether a satellite's short track covers the instant (outside it the satellite is not drawn). */
  private inTrack(track: BodyTrack, utcMs: number): boolean {
    const count = Math.floor(track.body.track.vectors.length / 3);
    const span = (count - 1) * track.body.track.stepSeconds * 1000;
    return utcMs >= track.startMs && utcMs <= track.startMs + span;
  }

  /** Whether the Sun lights a satellite at the instant (nearest track sample). */
  private isSunlit(track: BodyTrack, utcMs: number): boolean {
    const index = Math.round((utcMs - track.startMs) / (track.body.track.stepSeconds * 1000));
    return (track.body.sunlit?.[index] ?? 1) === 1;
  }

  /**
   * A satellite: a dot, bright while sunlit and dim in the Earth's shadow, with a fading trail of
   * the last minute of its path.
   */
  private drawSatellite(
    track: BodyTrack,
    utcMs: number,
    label: ((text: string, x: number, y: number, style: string, force?: boolean) => void) | null,
  ): void {
    if (!this.inTrack(track, utcMs)) {
      return;
    }
    const p = this.place(this.bodyPosition(track, utcMs));
    if (!p) {
      return;
    }
    const g = this.overlay;
    const lit = this.isSunlit(track, utcMs);
    const rgb = lit ? '226, 238, 255' : '110, 118, 134';
    let previous: ScreenPoint = p;
    g.lineWidth = 1.5;
    g.lineCap = 'round';
    for (let k = 1; k <= SATELLITE_TRAIL_STEPS; k++) {
      const t = utcMs - k * SATELLITE_TRAIL_STEP_MS;
      if (t < track.startMs) {
        break;
      }
      const q = this.place(this.bodyPosition(track, t));
      if (!q) {
        break;
      }
      g.strokeStyle = `rgba(${rgb}, ${(0.6 * (1 - k / (SATELLITE_TRAIL_STEPS + 1))).toFixed(3)})`;
      g.beginPath();
      g.moveTo(previous.x, previous.y);
      g.lineTo(q.x, q.y);
      g.stroke();
      previous = q;
    }
    g.fillStyle = `rgb(${rgb})`;
    g.beginPath();
    g.arc(p.x, p.y, lit ? 2.4 : 1.6, 0, Math.PI * 2);
    g.fill();
    if (label && lit) {
      g.textAlign = 'left';
      label(track.body.name, p.x + 6, p.y - 4, palette.bodyLabel);
    }
  }

  /**
   * A comet: a fuzzy head and a short tail pointing away from the Sun (brightLimbAngleDegrees is
   * the position angle of the Sun's direction, as for the Moon's bright limb).
   */
  private drawComet(p: ScreenPoint, radius: number, j2000: Vec3, body: MovingBody): void {
    const g = this.overlay;
    const axes = this.screenAxes(j2000, p);
    const chi = (body.brightLimbAngleDegrees * Math.PI) / 180 + Math.PI;
    const tail = axes
      ? {
          x: Math.cos(chi) * axes.north.x + Math.sin(chi) * axes.east.x,
          y: Math.cos(chi) * axes.north.y + Math.sin(chi) * axes.east.y,
        }
      : { x: 0, y: -1 };
    const length = Math.hypot(tail.x, tail.y) || 1;
    const end = { x: p.x + (tail.x / length) * radius * 9, y: p.y + (tail.y / length) * radius * 9 };

    const gradient = g.createLinearGradient(p.x, p.y, end.x, end.y);
    gradient.addColorStop(0, 'rgba(160, 232, 216, 0.75)');
    gradient.addColorStop(1, 'rgba(160, 232, 216, 0)');
    g.strokeStyle = gradient;
    g.lineWidth = radius * 1.2;
    g.lineCap = 'round';
    g.beginPath();
    g.moveTo(p.x, p.y);
    g.lineTo(end.x, end.y);
    g.stroke();

    const head = g.createRadialGradient(p.x, p.y, 0, p.x, p.y, radius * 2);
    head.addColorStop(0, 'rgba(214, 250, 240, 1)');
    head.addColorStop(1, 'rgba(160, 232, 216, 0)');
    g.fillStyle = head;
    g.beginPath();
    g.arc(p.x, p.y, radius * 2, 0, Math.PI * 2);
    g.fill();
  }

  /** The Moon with its phase: the bright limb points at position angle χ, the terminator is an ellipse. */
  private drawMoon(p: ScreenPoint, radius: number, j2000: Vec3, body: MovingBody): void {
    const g = this.overlay;
    g.fillStyle = '#20242c';
    g.beginPath();
    g.arc(p.x, p.y, radius, 0, Math.PI * 2);
    g.fill();

    const axes = this.screenAxes(j2000, p);
    const chi = (body.brightLimbAngleDegrees * Math.PI) / 180;
    const direction = axes
      ? {
          x: Math.cos(chi) * axes.north.x + Math.sin(chi) * axes.east.x,
          y: Math.cos(chi) * axes.north.y + Math.sin(chi) * axes.east.y,
        }
      : { x: 1, y: 0 };
    const cosI = Math.cos((body.phaseAngleDegrees * Math.PI) / 180);

    g.save();
    g.translate(p.x, p.y);
    g.rotate(Math.atan2(direction.y, direction.x));
    g.fillStyle = '#e8e3c8';
    g.beginPath();
    g.moveTo(0, -radius);
    g.arc(0, 0, radius, -Math.PI / 2, Math.PI / 2, false);
    g.ellipse(0, 0, Math.max(0.01, radius * Math.abs(cosI)), radius, 0, Math.PI / 2, -Math.PI / 2, cosI < 0);
    g.closePath();
    g.fill();
    g.restore();
  }

  // ---- picking -----------------------------------------------------------------------------

  private starVector(i: number): Vec3 {
    const base = i * Star.stride;
    const f = this.data.stars.float32;
    return [f[base + Star.x] ?? 0, f[base + Star.y] ?? 0, f[base + Star.z] ?? 0];
  }

  private dsoVector(i: number): Vec3 {
    const base = i * Dso.stride;
    const f = this.data.dsos.float32;
    return [f[base + Dso.x] ?? 0, f[base + Dso.y] ?? 0, f[base + Dso.z] ?? 0];
  }

  private dsoVisible(i: number): boolean {
    const base = i * Dso.stride;
    const magnitude = this.data.dsos.float32[base + Dso.magnitude] ?? UNKNOWN_MAGNITUDE;
    const major = this.data.dsos.float32[base + Dso.major] ?? 0;
    return magnitude < UNKNOWN_MAGNITUDE - 1
      ? magnitude <= this.dsoLimit
      : (major / 60) * this.pixelsPerDegree >= 10;
  }

  private selectionPosition(selection: Selection, utcMs: number): Vec3 | null {
    if (selection.kind === 'body') {
      const track = this.bodies.find((t) => t.body.id === selection.id);
      return track ? this.bodyPosition(track, utcMs) : null;
    }
    if (selection.kind === 'star') {
      const id = Number(selection.id);
      for (let i = 0; i < this.data.stars.count; i++) {
        if (this.data.stars.uint32[i * Star.stride + Star.hygId] === id) {
          return this.starVector(i);
        }
      }
      return null;
    }
    const index = this.dsoIdIndex.get(selection.id);
    return index === undefined ? null : this.dsoVector(index);
  }

  /** J2000 direction of a selectable object. */
  positionOf(selection: Selection, utcMs: number): Vec3 | null {
    return this.selectionPosition(selection, utcMs);
  }

  *candidates(utcMs: number): Generator<PickCandidate> {
    const pixelsPerDegree = this.pixelsPerDegree;
    for (const track of this.bodies) {
      const satellite = track.body.kind === 'satellite';
      if (satellite ? !this.layers.satellites || !this.inTrack(track, utcMs) : !this.layers.bodies) {
        continue;
      }
      const p = this.place(this.bodyPosition(track, utcMs), 0);
      if (p) {
        const radius = Math.max(4, ((track.body.diameterArcsec / 3600) * pixelsPerDegree) / 2);
        yield {
          kind: 'body',
          id: track.body.id,
          label: track.body.name,
          x: p.x,
          y: p.y,
          weight: 20,
          radius,
        };
      }
    }
    if (this.layers.deepSky) {
      for (let i = 0; i < this.data.dsos.count; i++) {
        const id = this.data.dsoIndex[i]?.[0] ?? '';
        const highlighted = this.plan.has(id);
        if (!highlighted && !this.dsoVisible(i)) {
          continue;
        }
        const p = this.place(this.dsoVector(i), 0);
        if (p) {
          const major = this.data.dsos.float32[i * Dso.stride + Dso.major] ?? 0;
          const name = this.data.dsoIndex[i]?.[1];
          yield {
            kind: 'dso',
            id,
            label: name ? `${id} · ${name}` : id,
            x: p.x,
            y: p.y,
            weight: highlighted ? 12 : 6,
            radius: Math.min(40, ((major / 60) * pixelsPerDegree) / 2),
          };
        }
      }
    }
    if (this.layers.stars) {
      const limit = this.starLimit;
      for (let i = 0; i < this.data.stars.count; i++) {
        const magnitude = this.data.stars.float32[i * Star.stride + Star.magnitude] ?? 99;
        if (magnitude > limit) {
          break;
        }
        const p = this.place(this.starVector(i), 0);
        if (p) {
          const hygId = this.data.stars.uint32[i * Star.stride + Star.hygId] ?? 0;
          const hip = this.data.stars.uint32[i * Star.stride + Star.hip] ?? 0;
          const name = this.data.starNames.get(hygId) ?? (hip > 0 ? `HIP ${hip}` : `HYG ${hygId}`);
          yield {
            kind: 'star',
            id: String(hygId),
            label: `${name} (${magnitude.toFixed(1)})`,
            x: p.x,
            y: p.y,
            weight: Math.max(0, (limit - magnitude) * 0.6),
            radius: 0,
          };
        }
      }
    }
  }

  pick(x: number, y: number, utcMs: number): PickCandidate | null {
    return pickNearest(this.candidates(utcMs), x, y);
  }

  /** Current dome camera for viewChanged. */
  view(): { azimuthDegrees: number; altitudeDegrees: number; fovDegrees: number } {
    return { azimuthDegrees: this.azimuth % 360, altitudeDegrees: this.altitude, fovDegrees: this.fov };
  }

  /** Angular distance of a J2000 direction from the view center (diagnostics). */
  distanceFromCenter(j2000: Vec3): number {
    return separationDegrees(this.viewCenter(), j2000);
  }
}
