// The sky view: one three.js scene whose shaders apply the C#-computed frame in three projections
// (3D dome, all-sky chart, finder chart), plus a 2D canvas overlay (overlay.ts) for the Sun, Moon,
// planets, labels, cardinal points and the camera field-of-view overlay. The scene's catalog
// objects come from catalogLayers.ts, the panorama and the landscapes from mediaLayers.ts.
import * as THREE from 'three';
import type { Fov, Layers, MovingBody, Observer, SkyAppearance } from '../bridge/schema';
import { Dso, Star, UNKNOWN_MAGNITUDE } from '../data/buffers';
import type { SkyData } from '../data/loader';
import {
  apparentHorizontal,
  fromHorizontal,
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
  DARK_SKY,
  deepSkyLoss,
  displayLevel,
  milkyWayFactor,
  skyColor,
  starCap,
  toTrack,
  type AppearanceNow,
  type AppearanceTrack,
} from './appearance';
import { AtmosphereTable, type SkyQuality } from './atmosphere';
import { bodyPosition, dsoVector, inTrack, starVector, toBodyTrack, type BodyTrack } from './bodies';
import { Flight, HOME_VIEW, MAX_ALTITUDE, MIN_ALTITUDE, type DomeView } from './camera';
import { LayerFactory } from './catalogLayers';
import { palette } from './colors';
import { altAzGrid, ecliptic, equatorialGrid, meridian } from './grids';
import { hasObstruction, maskAltitude, silhouetteTriangles, type HorizonPoint } from './horizon';
import { landscapeHorizon, type LandscapeEntry, type MediaManifest } from './media';
import { MediaLayers, type MediaCredit } from './mediaLayers';
import { SkyOverlay, type FovState, type OverlayView, type Placed, type Selection } from './overlay';
import { extinctionAt, fluxOf, nakedEyeLimit, skyBrightnessAt } from './photometry';
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
  gnomonic,
  gnomonicInverse,
  insidePolygon,
  rotateTowards,
  separationDegrees,
  tangentBasis,
  type ChartGeometry,
  type DomeCamera,
  type ScreenPoint,
  type ViewMode,
} from './projection';
import {
  groundFragment,
  groundVertex,
  silhouetteFragment,
  silhouetteVertex,
  skyPassFragment,
  skyPassVertex,
} from './shaders';
import {
  clampFov,
  color,
  DAY_GROUND,
  DAY_HORIZON,
  displayLevelGlsl,
  domeStarScale,
  MAX_FINDER_FOV,
  MAX_FOV,
  MIN_FINDER_FOV,
  MIN_FOV,
  MOON_CHROMA,
  TINTS,
  unitChroma,
  type LightPollutionTint,
  type Rgb,
} from './skyStyle';

export type { BodyTrack } from './bodies';
export type { MediaCredit } from './mediaLayers';
export type { Selection } from './overlay';
export type { ScreenPoint, ViewMode } from './projection';

/** Why tracking stopped: a manual pan, the selection changed or vanished, or another mode. */
export type TrackingStop = 'manual' | 'selection' | 'lost' | 'mode';

type FlightTarget =
  | { readonly kind: 'direction'; readonly j2000: Vec3; readonly fov: number }
  | { readonly kind: 'view'; readonly view: DomeView }
  | { readonly kind: 'selection'; readonly fov: number };

const IDENTITY: Mat3 = [1, 0, 0, 0, 1, 0, 0, 0, 1];
const MILKY_WAY_OPACITY = 0.22;

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
  private readonly mediaLayers: MediaLayers;
  private readonly layerFactory: LayerFactory;
  private readonly overlayLayer: SkyOverlay;
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
    this.overlayLayer = new SkyOverlay(context);
    this.layerFactory = new LayerFactory(this.scene, this.shared, this.pixelRatio);

    // The panorama (sampled by the sky pass) and the landscape, a full-screen pass drawn over
    // the stars and lines once its image is loaded.
    this.mediaLayers = new MediaLayers(
      this.renderer,
      this.scene,
      {
        ...this.domeUniforms(),
        uMode: this.shared.uMode,
        uChartScale: this.shared.uChartScale,
        uViewport: this.skyPassUniforms.uViewport,
      },
      (error) => {
        this.onMediaError?.(error);
      },
    );

    // The sky: a full-screen pass in the dome and the all-sky chart (the finder has a flat
    // background), drawn before everything else.
    this.atmosphere = new AtmosphereTable(this.quality);
    this.shared.uAtmosphere = this.atmosphere.texture;
    this.skyPass = new THREE.Mesh(
      new THREE.PlaneGeometry(2, 2),
      new THREE.ShaderMaterial({
        vertexShader: skyPassVertex,
        fragmentShader: skyPassFragment(displayLevelGlsl()),
        uniforms: { ...this.shared, ...this.skyPassUniforms, ...this.mediaLayers.panoramaUniforms },
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

    const f = this.layerFactory;
    this.milkyWay = f.segments(data.milkyWay, palette.milkyWay, this.milkyWayOpacity, 0.15);
    this.milkyWay.renderOrder = -5;
    this.constellationBounds = f.segments(data.constellationBounds, palette.constellationBounds, {
      value: 0.35,
    });
    this.constellationLines = f.segments(data.constellationLines, palette.constellationLines, {
      value: 0.55,
    });
    this.altAz = f.lines(altAzGrid(), palette.altAzGrid, 0.35, this.horizonFrame);
    this.meridianLine = f.lines(meridian(), palette.meridian, 0.6, this.horizonFrame);
    this.eclipticLine = f.lines(ecliptic(), palette.ecliptic, 0.5);

    const built = f.stars(data, starMultiplier, {
      uLimit: this.starLimitUniform,
      uStarScale: this.starScaleUniform,
    });
    this.stars = built.points;
    this.starCount = built.count;
    const dso = f.dsos(data, this.dsoUniforms, this.dsoIdIndex);
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
      this.equatorial = this.layerFactory.lines(
        equatorialGrid(frame.precession),
        palette.equatorialGrid,
        0.3,
      );
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
    this.mediaLayers.setMedia(baseUrl, manifest);
    this.applyLandscape();
  }

  /** Called when an image of the media folder could not be loaded (logged by the engine). */
  onMediaError: ((error: unknown) => void) | null = null;

  /** The landscapes of the media folder (empty without it). */
  get landscapes(): readonly LandscapeEntry[] {
    return this.mediaLayers.landscapes;
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
    return this.mediaLayers.credits(this.milkyWayVisible);
  }

  /**
   * The site's landscape (setObserver.landscape): a landscape of the media folder replaces the
   * horizon profile (its own horizon cuts the labels; its outline is drawn until the image is
   * loaded), "flat" shows none, null the site's profile.
   */
  private applyLandscape(): void {
    const id = this.observer?.landscape ?? null;
    const entry = this.mediaLayers.selectLandscape(id);
    this.horizonPoints = entry
      ? landscapeHorizon(entry)
      : id === 'flat'
        ? []
        : [...(this.observer?.horizon ?? [])].sort((a, b) => a.azimuth - b.azimuth);
    this.buildSilhouette();
  }

  setBodies(bodies: MovingBody[]): void {
    this.bodies = bodies.map(toBodyTrack);
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
    this.milkyWay.visible = l.milkyWay && !(this.mediaLayers.hasPanorama && !finder);
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
      this.silhouette.visible = !finder && l.ground && !this.mediaLayers.hasLandscapeImage;
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
    return track ? transform(this.currentH, bodyPosition(track, utcMs)) : null;
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
    this.mediaLayers.update({
      mode: this.mode,
      fov: this.fov,
      layers: this.layers,
      degreesPerPixel: 1 / Math.max(1e-6, this.pixelsPerDegree * this.pixelRatio),
      groundLight: light,
    });
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
    this.fovScreen = this.overlayLayer.draw(this.overlayView(), utcMs, this.fovState);
  }

  /** The view as the overlay reads it this frame. */
  private overlayView(): OverlayView {
    return {
      data: this.data,
      layers: this.layers,
      mode: this.mode,
      width: this.width,
      height: this.height,
      effectiveFov: this.effectiveFov,
      starLimit: this.starLimit,
      pixelsPerDegree: this.pixelsPerDegree,
      chart: this.chart,
      finderCenter: this.finderCenter,
      finderFov: this.finderFov,
      minimumAltitudeDegrees: this.observer?.minimumAltitudeDegrees ?? 0,
      plan: this.plan,
      selection: this.selection,
      bodies: this.bodies,
      stats: this.showStats && this.statsText ? this.statsText : null,
      place: (j2000, margin, cut) => this.place(j2000, margin, cut),
      placeHorizontal: (altitude, azimuth, margin) => this.placeHorizontal(altitude, azimuth, margin),
      selectionPosition: (selection, utcMs) => this.selectionPosition(selection, utcMs),
      dsoVisible: (i) => this.dsoVisible(i),
    };
  }

  // ---- picking -----------------------------------------------------------------------------

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
      return track ? bodyPosition(track, utcMs) : null;
    }
    if (selection.kind === 'star') {
      const id = Number(selection.id);
      for (let i = 0; i < this.data.stars.count; i++) {
        if (this.data.stars.uint32[i * Star.stride + Star.hygId] === id) {
          return starVector(this.data, i);
        }
      }
      return null;
    }
    const index = this.dsoIdIndex.get(selection.id);
    return index === undefined ? null : dsoVector(this.data, index);
  }

  /** J2000 direction of a selectable object. */
  positionOf(selection: Selection, utcMs: number): Vec3 | null {
    return this.selectionPosition(selection, utcMs);
  }

  *candidates(utcMs: number): Generator<PickCandidate> {
    const pixelsPerDegree = this.pixelsPerDegree;
    for (const track of this.bodies) {
      const satellite = track.body.kind === 'satellite';
      if (satellite ? !this.layers.satellites || !inTrack(track, utcMs) : !this.layers.bodies) {
        continue;
      }
      const p = this.place(bodyPosition(track, utcMs), 0);
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
        const p = this.place(dsoVector(this.data, i), 0);
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
        const p = this.place(starVector(this.data, i), 0);
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
