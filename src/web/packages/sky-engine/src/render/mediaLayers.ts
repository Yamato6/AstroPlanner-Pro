// The media folder's images on the GPU (spec §8.3, §8.5): the Milky Way panorama, sampled by the
// sky pass, and the site's landscape, a full-screen pass over the stars and lines.
import * as THREE from 'three';
import type { Layers } from '../bridge/schema';
import {
  equirectangularLod,
  loadMediaImage,
  panoramaFieldFade,
  type LandscapeEntry,
  type MediaManifest,
} from './media';
import type { ViewMode } from './projection';
import { landscapeFragment, landscapeVertex } from './shaders';
import type { Rgb } from './skyStyle';

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
  readonly kind: 'panorama' | 'landscape' | 'survey';
  readonly author: string;
  readonly license: string;
}

/** What the media layers need of the view each frame. */
export interface MediaFrame {
  readonly mode: ViewMode;
  readonly fov: number;
  readonly layers: Layers;
  readonly degreesPerPixel: number;
  /** Light of the sky at the horizon: 0 at night, 1 by day. */
  readonly groundLight: number;
}

export class MediaLayers {
  /** Uniforms of the sky pass's panorama (shared with its material). */
  readonly panoramaUniforms = {
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

  /**
   * @param viewUniforms the dome projection, mode, chart scale and viewport uniforms of the view
   * (the same objects, so the view's updates reach the landscape pass).
   * @param onError called when an image could not be loaded (logged by the engine).
   */
  constructor(
    private readonly renderer: THREE.WebGLRenderer,
    scene: THREE.Scene,
    viewUniforms: Record<string, THREE.IUniform>,
    private readonly onError: (error: unknown) => void,
  ) {
    // The landscape: a full-screen pass over the stars and lines (renderOrder), once its image is loaded.
    this.landscapePass = new THREE.Mesh(
      new THREE.PlaneGeometry(2, 2),
      new THREE.ShaderMaterial({
        vertexShader: landscapeVertex,
        fragmentShader: landscapeFragment,
        uniforms: { ...viewUniforms, ...this.landscapeUniforms },
        transparent: true,
        depthTest: false,
        depthWrite: false,
      }),
    );
    this.landscapePass.renderOrder = 20;
    this.landscapePass.frustumCulled = false;
    this.landscapePass.visible = false;
    scene.add(this.landscapePass);
  }

  /** The media folder's manifest: loads the panorama (the landscape comes with {@link selectLandscape}). */
  setMedia(baseUrl: string, manifest: MediaManifest): void {
    this.media = { baseUrl, manifest };
    loadMediaImage(baseUrl, manifest.panorama.file, this.textureWidth(manifest.panorama.width))
      .then((image) => {
        const imageWidth = image.width;
        this.panorama = { texture: this.equirectangularTexture(image), width: imageWidth };
        this.panoramaUniforms.uPanorama.value = this.panorama.texture;
      })
      .catch(this.onError);
  }

  /** The landscapes of the media folder (empty without it). */
  get landscapes(): readonly LandscapeEntry[] {
    return this.media?.manifest.landscapes ?? [];
  }

  /** True once the panorama is on the GPU (it then replaces the outline Milky Way). */
  get hasPanorama(): boolean {
    return this.panorama !== null;
  }

  /** True once the landscape's image is on the GPU (until then its outline is drawn). */
  get hasLandscapeImage(): boolean {
    return Boolean(this.landscape?.texture);
  }

  /**
   * Shows the landscape `id` of the media folder (loading its image), or none for null, "flat"
   * or an id the folder lacks. Returns the landscape shown.
   */
  selectLandscape(id: string | null): LandscapeEntry | null {
    const entry = id === null ? null : (this.media?.manifest.landscapes.find((l) => l.id === id) ?? null);
    if (entry?.id !== this.landscape?.entry.id) {
      this.landscape?.texture?.dispose();
      this.landscapeUniforms.uLandscape.value = null;
      this.landscape = entry ? { entry, texture: null, width: 0 } : null;
      if (entry && this.media) {
        const requested = this.landscape;
        loadMediaImage(this.media.baseUrl, entry.file, this.textureWidth(entry.width))
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
          .catch(this.onError);
      }
    }
    return entry;
  }

  /** Panorama and landscape uniforms for this frame (after the sky light is known). */
  update(frame: MediaFrame): void {
    const finder = frame.mode === 'finder';
    const p = this.panoramaUniforms;
    p.uPanoramaOn.value = this.panorama && frame.layers.milkyWay && !finder ? 1 : 0;
    p.uPanoramaGain.value = PANORAMA_GAIN * (frame.mode === 'dome' ? panoramaFieldFade(frame.fov) : 1);
    p.uPanoramaLod.value = this.panorama ? equirectangularLod(this.panorama.width, frame.degreesPerPixel) : 0;

    const l = this.landscapeUniforms;
    const ready = this.landscape?.texture ? this.landscape : null;
    this.landscapePass.visible = ready !== null && frame.layers.ground && !finder;
    if (ready) {
      l.uLandscapeLod.value = equirectangularLod(ready.width, frame.degreesPerPixel);
      // Daylight shows the photo as taken; the night darkens it to a faint, nearly grey shape
      // tinted by the sky at the horizon (moonlight and the towns' glow raise it).
      const k = frame.groundLight;
      l.uLight.value.set(
        LANDSCAPE_NIGHT[0] * (1 - k) + k,
        LANDSCAPE_NIGHT[1] * (1 - k) + k,
        LANDSCAPE_NIGHT[2] * (1 - k) + k,
      );
      l.uSaturation.value = 0.25 + 0.75 * k;
    }
  }

  /**
   * The images on screen now, whose credits the page shows (spec §8.3: CC BY). The panorama
   * counts only when `milkyWayVisible` (the sky is dark enough to show it at all).
   */
  credits(milkyWayVisible: boolean): MediaCredit[] {
    const credits: MediaCredit[] = [];
    const panorama = this.media?.manifest.panorama;
    if (
      panorama &&
      this.panoramaUniforms.uPanoramaOn.value > 0 &&
      this.panoramaUniforms.uPanoramaGain.value > 0 &&
      milkyWayVisible
    ) {
      credits.push({ kind: 'panorama', author: panorama.credit.author, license: panorama.credit.license });
    }
    if (this.landscape?.texture && this.landscapePass.visible) {
      const { credit } = this.landscape.entry;
      credits.push({ kind: 'landscape', author: credit.author, license: credit.license });
    }
    return credits;
  }

  private textureWidth(wanted: number): number {
    return Math.min(wanted, MEDIA_MAX_WIDTH, this.renderer.capabilities.maxTextureSize);
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
}
