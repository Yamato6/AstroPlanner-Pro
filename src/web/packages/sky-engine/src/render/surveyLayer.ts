// The survey layer on the GPU (spec §8.3): HiPS tiles from the host's tile proxy, drawn over the
// sky and under everything else as the field narrows. Each frame it picks the tiles of the view
// (surveyTiles.ts), asks the proxy for the missing ones (a few at a time, nearest the centre
// first), uploads at most two per frame, and draws each tile from its own image once loaded —
// fading in over its parent — or else from the nearest loaded ancestor or the order-3 Allsky
// atlas. Textures are kept least-recently-used within a budget.
import * as THREE from 'three';
import type { Mat3, Vec3 } from '../math/frame';
import type { TileCap } from '../math/healpix';
import { surveyFragment, surveyVertex } from './shaders';
import {
  allskyRect,
  allskyUrl,
  ancestorOf,
  ancestorRect,
  SURVEY_MIN_ORDER,
  surveyOrder,
  tileGrid,
  tileUrl,
  visibleTiles,
  type SurveyConfig,
  type UvRect,
} from './surveyTiles';

export type SurveyMode = 'realistic' | 'survey';

/** Tile textures kept on the GPU: 512² RGBA without mipmaps is 1 MiB, so ~256 MiB (spec §8.3). */
const TEXTURE_BUDGET = 256;
/** Requests to the proxy at once (it downloads at most 4 at a time itself). */
const MAX_REQUESTS = 6;
/** Decoded tiles uploaded per frame: an upload takes ~1 ms, a frame must stay under 50 ms. */
const UPLOADS_PER_FRAME = 2;
/** A tile that failed (no connection, not in the survey) is asked again after this long. */
const RETRY_MS = 30_000;
const FADE_MS = 300;
/** The plates' sky background (sRGB), subtracted so that the survey does not grey the sky. */
const BLACK_LEVEL = 0.07;
const GEOMETRY_CACHE = 400;

interface TileEntry {
  texture: THREE.Texture | null;
  readyAt: number;
  failedAt: number;
  loading: boolean;
  usedAt: number;
}

/** What the layer needs of the view this frame. */
export interface SurveyFrame {
  readonly nowMs: number;
  /** 0 hides the layer (wide fields, the all-sky chart). */
  readonly opacity: number;
  /** View centre (J2000) and the angle that holds the whole screen, degrees. */
  readonly center: Vec3;
  readonly radiusDegrees: number;
  /** Size of a device pixel at the centre, degrees. */
  readonly degreesPerPixel: number;
  /** J2000 → north/east/up when tiles below the horizon can be skipped (dome, ground on); else null. */
  readonly horizon: Mat3 | null;
}

export class SurveyLayer {
  private config: SurveyConfig | null = null;
  private generation = 0;
  private readonly tiles = new Map<string, TileEntry>();
  private allsky: { texture: THREE.Texture; width: number; height: number } | null = null;
  private allskyRequested = false;
  private requests = 0;
  private readonly decoded: { key: string; generation: number; image: ImageBitmap }[] = [];
  private readonly geometries = new Map<string, THREE.BufferGeometry>();
  private readonly meshes: THREE.Mesh<THREE.BufferGeometry, THREE.ShaderMaterial>[] = [];
  private readonly shared = {
    uOpacity: { value: 0 },
    uRealistic: { value: 1 },
    uBlack: { value: BLACK_LEVEL },
  };
  private drawn = false;
  private realistic = true;

  constructor(
    private readonly renderer: THREE.WebGLRenderer,
    private readonly scene: THREE.Scene,
    private readonly viewUniforms: Record<string, THREE.IUniform>,
    private readonly onError: (error: unknown) => void,
  ) {}

  /** The survey to show (null: none). Changing it drops the previous survey's tiles. */
  setSurvey(config: SurveyConfig | null): void {
    if (config?.id === this.config?.id && config?.baseUrl === this.config?.baseUrl) {
      return;
    }
    this.generation++;
    this.config = config;
    for (const entry of this.tiles.values()) {
      entry.texture?.dispose();
    }
    this.tiles.clear();
    for (const item of this.decoded.splice(0)) {
      item.image.close();
    }
    this.allsky?.texture.dispose();
    this.allsky = null;
    this.allskyRequested = false;
  }

  setMode(mode: SurveyMode): void {
    this.realistic = mode === 'realistic';
    this.shared.uRealistic.value = this.realistic ? 1 : 0;
  }

  /**
   * How much the survey replaces the sky this frame: in the Survey mode the sky's own light goes
   * as the photographs come in (full contrast, even by day); the realistic mode adds them to it.
   */
  get skyCover(): number {
    return this.drawn && !this.realistic ? this.shared.uOpacity.value : 0;
  }

  /** True in the realistic mode (the survey dimmed by the sky's brightness). */
  get isRealistic(): boolean {
    return this.realistic;
  }

  /** The survey's credit while its tiles are on screen (spec §10), else null. */
  get credit(): string | null {
    return this.drawn && this.config ? this.config.credit : null;
  }

  /** Picks, loads and draws this frame's tiles. */
  update(frame: SurveyFrame): void {
    const config = this.config;
    this.drawn = false;
    let used = 0;
    if (config && frame.opacity > 0.001) {
      this.shared.uOpacity.value = frame.opacity;
      this.uploadDecoded(frame.nowMs);
      if (!this.allskyRequested) {
        this.allskyRequested = true;
        this.requestAllsky(config);
      }
      const order = surveyOrder(frame.degreesPerPixel, config.tileWidth, config.maxOrder);
      const horizon = frame.horizon;
      const accept = horizon ? (cap: TileCap) => aboveHorizon(horizon, cap) : undefined;
      const wanted = visibleTiles(order, frame.center, frame.radiusDegrees, accept);
      for (const nested of wanted) {
        used = this.drawTile(config, order, nested, frame.nowMs, used);
      }
      this.drawn = used > 0;
      this.evict(frame.nowMs);
    }
    for (let i = used; i < this.meshes.length; i++) {
      const mesh = this.meshes[i];
      if (mesh) {
        mesh.visible = false;
      }
    }
  }

  /** Draws one tile (its own image, fading in over the best ancestor) and asks for it if needed. */
  private drawTile(config: SurveyConfig, order: number, nested: number, nowMs: number, used: number): number {
    const key = `${order}/${nested}`;
    const entry = this.entry(key, nowMs);
    if (!entry.texture && !entry.loading && nowMs - entry.failedAt > RETRY_MS) {
      this.request(config, key, tileUrl(config, order, nested));
    }
    const weight = entry.texture ? Math.min(1, (nowMs - entry.readyAt) / FADE_MS) : 0;
    let next = used;
    if (weight < 1) {
      const fallback = this.ancestor(order, nested, nowMs);
      if (fallback) {
        next = this.place(next, order, nested, fallback.texture, fallback.rect, 1 - weight);
      }
    }
    if (entry.texture && weight > 0) {
      next = this.place(next, order, nested, entry.texture, [0, 0, 1, 1], weight);
    }
    return next;
  }

  /** The nearest loaded ancestor of a tile (down to order 3), else the Allsky atlas. */
  private ancestor(
    order: number,
    nested: number,
    nowMs: number,
  ): { texture: THREE.Texture; rect: UvRect } | null {
    for (let level = order - 1; level >= SURVEY_MIN_ORDER; level--) {
      const parent = ancestorOf(order, nested, level);
      const entry = this.tiles.get(`${level}/${parent}`);
      if (entry?.texture) {
        entry.usedAt = nowMs;
        return { texture: entry.texture, rect: ancestorRect(order, nested, level) };
      }
    }
    if (!this.allsky) {
      return null;
    }
    const cell = allskyRect(
      ancestorOf(order, nested, SURVEY_MIN_ORDER),
      this.allsky.width,
      this.allsky.height,
    );
    return { texture: this.allsky.texture, rect: ancestorRect(order, nested, SURVEY_MIN_ORDER, cell) };
  }

  /** Puts a tile's grid on screen with a texture rectangle and a weight; returns the meshes used. */
  private place(
    index: number,
    order: number,
    nested: number,
    texture: THREE.Texture,
    rect: UvRect,
    weight: number,
  ): number {
    const mesh = this.meshes[index] ?? this.newMesh();
    mesh.geometry = this.geometry(order, nested);
    const u = mesh.material.uniforms;
    (u.uTile as THREE.IUniform<THREE.Texture | null>).value = texture;
    (u.uRect as THREE.IUniform<THREE.Vector4>).value.set(rect[0], rect[1], rect[2], rect[3]);
    (u.uWeight as THREE.IUniform<number>).value = weight;
    mesh.visible = true;
    return index + 1;
  }

  private newMesh(): THREE.Mesh<THREE.BufferGeometry, THREE.ShaderMaterial> {
    const material = new THREE.ShaderMaterial({
      vertexShader: surveyVertex,
      fragmentShader: surveyFragment,
      uniforms: {
        ...this.viewUniforms,
        ...this.shared,
        uTile: { value: null },
        uRect: { value: new THREE.Vector4(0, 0, 1, 1) },
        uWeight: { value: 1 },
      },
      // Seen from inside the sphere (and mirrored in the charts): both windings are front.
      side: THREE.DoubleSide,
      transparent: true,
      depthTest: false,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
    });
    const mesh = new THREE.Mesh(new THREE.BufferGeometry(), material);
    // Over the sky pass (−20), under the Milky Way outline (−5), the stars and everything else.
    mesh.renderOrder = -15;
    mesh.frustumCulled = false;
    this.scene.add(mesh);
    this.meshes.push(mesh);
    return mesh;
  }

  private geometry(order: number, nested: number): THREE.BufferGeometry {
    const key = `${order}/${nested}`;
    let geometry = this.geometries.get(key);
    if (geometry) {
      // Most recently used last.
      this.geometries.delete(key);
    } else {
      const grid = tileGrid(order, nested);
      geometry = new THREE.BufferGeometry();
      geometry.setAttribute('position', new THREE.BufferAttribute(grid.positions, 3));
      geometry.setAttribute('tileUv', new THREE.BufferAttribute(grid.uvs, 2));
      geometry.setIndex(new THREE.BufferAttribute(grid.indices, 1));
      if (this.geometries.size >= GEOMETRY_CACHE) {
        const oldest = this.geometries.keys().next().value;
        if (oldest !== undefined) {
          this.geometries.get(oldest)?.dispose();
          this.geometries.delete(oldest);
        }
      }
    }
    this.geometries.set(key, geometry);
    return geometry;
  }

  private entry(key: string, nowMs: number): TileEntry {
    let entry = this.tiles.get(key);
    if (!entry) {
      entry = { texture: null, readyAt: 0, failedAt: -Infinity, loading: false, usedAt: nowMs };
      this.tiles.set(key, entry);
    }
    entry.usedAt = nowMs;
    return entry;
  }

  private request(config: SurveyConfig, key: string, url: string): void {
    if (this.requests >= MAX_REQUESTS) {
      return;
    }
    const entry = this.tiles.get(key);
    if (!entry) {
      return;
    }
    const generation = this.generation;
    entry.loading = true;
    this.requests++;
    void fetchImage(url)
      .then((image) => {
        if (generation !== this.generation || config !== this.config) {
          image?.close();
          return;
        }
        if (image) {
          this.decoded.push({ key, generation, image });
        } else {
          entry.failedAt = performance.now();
        }
      })
      .catch((error: unknown) => {
        entry.failedAt = performance.now();
        this.onError(error);
      })
      .finally(() => {
        entry.loading = false;
        this.requests--;
      });
  }

  private requestAllsky(config: SurveyConfig): void {
    const generation = this.generation;
    void fetchImage(allskyUrl(config))
      .then((image) => {
        if (!image) {
          return;
        }
        if (generation !== this.generation) {
          image.close();
          return;
        }
        const texture = this.texture(image);
        this.allsky = { texture, width: image.width, height: image.height };
        image.close();
      })
      .catch(this.onError);
  }

  private uploadDecoded(nowMs: number): void {
    for (let n = 0; n < UPLOADS_PER_FRAME && this.decoded.length > 0; n++) {
      const item = this.decoded.shift();
      if (!item) {
        break;
      }
      const entry = this.tiles.get(item.key);
      if (item.generation !== this.generation || !entry) {
        item.image.close();
        continue;
      }
      entry.texture = this.texture(item.image);
      entry.readyAt = nowMs;
      item.image.close();
    }
  }

  private texture(image: ImageBitmap): THREE.Texture {
    const texture = new THREE.Texture(image);
    texture.flipY = false; // rows top-down, as HiPS tiles are read (v = 0 at the top)
    texture.wrapS = THREE.ClampToEdgeWrapping;
    texture.wrapT = THREE.ClampToEdgeWrapping;
    texture.minFilter = THREE.LinearFilter;
    texture.magFilter = THREE.LinearFilter;
    texture.generateMipmaps = false;
    texture.needsUpdate = true;
    this.renderer.initTexture(texture);
    return texture;
  }

  /** Drops the least recently used textures beyond the budget (never one used this frame). */
  private evict(nowMs: number): void {
    const loaded = [...this.tiles.entries()].filter(([, e]) => e.texture);
    if (loaded.length > TEXTURE_BUDGET) {
      loaded.sort((a, b) => a[1].usedAt - b[1].usedAt);
      for (const [key, entry] of loaded.slice(0, loaded.length - TEXTURE_BUDGET)) {
        if (entry.usedAt >= nowMs) {
          break;
        }
        entry.texture?.dispose();
        this.tiles.delete(key);
      }
    }
    // Entries without a texture that nobody asked for in a minute go too.
    if (this.tiles.size > 4 * TEXTURE_BUDGET) {
      for (const [key, entry] of this.tiles) {
        if (!entry.texture && !entry.loading && nowMs - entry.usedAt > 60_000) {
          this.tiles.delete(key);
        }
      }
    }
  }
}

/** Is any part of a tile above −2° of altitude? */
function aboveHorizon(horizon: Mat3, cap: TileCap): boolean {
  const c = cap.center;
  const up = horizon[6] * c[0] + horizon[7] * c[1] + horizon[8] * c[2];
  const altitude = (Math.asin(Math.min(1, Math.max(-1, up))) * 180) / Math.PI;
  return altitude + cap.radiusDegrees > -2;
}

/** A tile image decoded off the main thread, or null when the proxy has none (404). */
async function fetchImage(url: string): Promise<ImageBitmap | null> {
  const response = await fetch(url);
  if (!response.ok) {
    return null;
  }
  return createImageBitmap(await response.blob());
}
