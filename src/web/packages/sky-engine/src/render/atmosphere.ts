// The colour and the angular spread of daylight and twilight (spec §8.2): single scattering of
// sunlight (Rayleigh + Mie) through a spherical atmosphere, after Nishita et al. (1993) and the
// MIT-licensed glsl-atmosphere (R. Barnett) and three.js `Sky`. Only the *shape* is used: the
// level comes from Core (twilight at the zenith, SkyBrightness.Twilight). The shader evaluates
// it into a small table (view altitude × azimuth from the Sun) whenever the Sun moves; this
// TypeScript twin is the reference the tests check (monotonic with the Sun, blue zenith, warm
// horizon at sunset).
import * as THREE from 'three';

const DEG = Math.PI / 180;

export type SkyQuality = 'low' | 'medium' | 'high';

/** Table size and integration samples per quality level (Low must stay cheap on integrated GPUs). */
export const QUALITY: Readonly<
  Record<
    SkyQuality,
    {
      readonly width: number;
      readonly height: number;
      readonly viewSteps: number;
      readonly lightSteps: number;
    }
  >
> = {
  low: { width: 32, height: 16, viewSteps: 8, lightSteps: 4 },
  medium: { width: 64, height: 32, viewSteps: 16, lightSteps: 6 },
  high: { width: 128, height: 64, viewSteps: 32, lightSteps: 8 },
};

/** The twilight shape is frozen below this Sun altitude (the Earth's shadow leaves the zenith unlit). */
export const SHAPE_MIN_SUN_ALTITUDE = -6;

const EARTH_RADIUS = 6371e3;
const ATMOSPHERE_RADIUS = 6471e3;
const RAYLEIGH_HEIGHT = 8e3;
const MIE_HEIGHT = 1.2e3;
const RAYLEIGH: readonly [number, number, number] = [5.8e-6, 13.5e-6, 33.1e-6];
const MIE = 21e-6;
const MIE_G = 0.76;
/**
 * Ozone (Chappuis band) absorbs green and red: without it a twilight zenith turns grey instead of
 * deep blue. A layer peaking at 25 km, 15 km of equivalent column: τ ≈ (0.012, 0.035, 0.002).
 */
const OZONE: readonly [number, number, number] = [0.82e-6, 2.33e-6, 0.105e-6];

function ozoneDensity(heightMetres: number): number {
  return Math.max(0, 1 - Math.abs(heightMetres - 25e3) / 15e3);
}

type Vec3 = readonly [number, number, number];

/** Table coordinates → view altitude (−5°…90°, denser near the horizon) and azimuth from the Sun (0…180°). */
export function tableDirection(
  u: number,
  v: number,
): { altitudeDegrees: number; azimuthFromSunDegrees: number } {
  return { altitudeDegrees: -5 + 95 * u * u, azimuthFromSunDegrees: 180 * v };
}

/** Inverse of {@link tableDirection}. */
export function tableCoordinates(
  altitudeDegrees: number,
  azimuthFromSunDegrees: number,
): { u: number; v: number } {
  return {
    u: Math.sqrt(Math.min(1, Math.max(0, (altitudeDegrees + 5) / 95))),
    v: Math.min(1, Math.max(0, Math.abs(azimuthFromSunDegrees) / 180)),
  };
}

function sphereExit(origin: Vec3, direction: Vec3, radius: number): number | null {
  const b = 2 * (origin[0] * direction[0] + origin[1] * direction[1] + origin[2] * direction[2]);
  const c = origin[0] ** 2 + origin[1] ** 2 + origin[2] ** 2 - radius * radius;
  const d = b * b - 4 * c;
  if (d < 0) {
    return null;
  }
  const far = (-b + Math.sqrt(d)) / 2;
  return far > 0 ? far : null;
}

function hitsGround(origin: Vec3, direction: Vec3): number | null {
  const b = 2 * (origin[0] * direction[0] + origin[1] * direction[1] + origin[2] * direction[2]);
  const c = origin[0] ** 2 + origin[1] ** 2 + origin[2] ** 2 - EARTH_RADIUS * EARTH_RADIUS;
  const d = b * b - 4 * c;
  if (d < 0) {
    return null;
  }
  const near = (-b - Math.sqrt(d)) / 2;
  return near > 0 ? near : null;
}

/**
 * Scattered sunlight (relative RGB) seen at the ground toward a direction, for a Sun at
 * `sunAltitudeDegrees`. The same integral as the shader's `scatter` (shaders.ts).
 */
export function scatteredLight(
  altitudeDegrees: number,
  azimuthFromSunDegrees: number,
  sunAltitudeDegrees: number,
  viewSteps = QUALITY.medium.viewSteps,
  lightSteps = QUALITY.medium.lightSteps,
): [number, number, number] {
  const alt = altitudeDegrees * DEG;
  const az = azimuthFromSunDegrees * DEG;
  const sunAlt = sunAltitudeDegrees * DEG;
  const view: Vec3 = [Math.cos(alt) * Math.cos(az), Math.sin(alt), Math.cos(alt) * Math.sin(az)];
  const sun: Vec3 = [Math.cos(sunAlt), Math.sin(sunAlt), 0];
  const origin: Vec3 = [0, EARTH_RADIUS + 10, 0];
  const exit = sphereExit(origin, view, ATMOSPHERE_RADIUS) ?? 0;
  const length = hitsGround(origin, view) ?? exit;
  const ds = length / viewSteps;
  let depthR = 0;
  let depthM = 0;
  let depthO = 0;
  const sumR = [0, 0, 0];
  let sumM = 0;
  for (let i = 0; i < viewSteps; i++) {
    const t = (i + 0.5) * ds;
    const p: Vec3 = [origin[0] + view[0] * t, origin[1] + view[1] * t, origin[2] + view[2] * t];
    const height = Math.hypot(p[0], p[1], p[2]) - EARTH_RADIUS;
    const r = Math.exp(-height / RAYLEIGH_HEIGHT) * ds;
    const m = Math.exp(-height / MIE_HEIGHT) * ds;
    depthR += r;
    depthM += m;
    depthO += ozoneDensity(height) * ds;
    if (hitsGround(p, sun) !== null) {
      continue; // in the Earth's shadow
    }
    const lightLength = sphereExit(p, sun, ATMOSPHERE_RADIUS) ?? 0;
    const dl = lightLength / lightSteps;
    let lightR = 0;
    let lightM = 0;
    let lightO = 0;
    for (let j = 0; j < lightSteps; j++) {
      const tl = (j + 0.5) * dl;
      const q: Vec3 = [p[0] + sun[0] * tl, p[1] + sun[1] * tl, p[2] + sun[2] * tl];
      const h = Math.hypot(q[0], q[1], q[2]) - EARTH_RADIUS;
      lightR += Math.exp(-h / RAYLEIGH_HEIGHT) * dl;
      lightM += Math.exp(-h / MIE_HEIGHT) * dl;
      lightO += ozoneDensity(h) * dl;
    }
    for (let c = 0; c < 3; c++) {
      const tau =
        (RAYLEIGH[c] ?? 0) * (depthR + lightR) +
        MIE * 1.1 * (depthM + lightM) +
        (OZONE[c] ?? 0) * (depthO + lightO);
      const attenuation = Math.exp(-tau);
      sumR[c] = (sumR[c] ?? 0) + r * attenuation;
      if (c === 1) {
        sumM += m * attenuation;
      }
    }
  }
  const mu = view[0] * sun[0] + view[1] * sun[1] + view[2] * sun[2];
  const phaseR = (3 / (16 * Math.PI)) * (1 + mu * mu);
  const g = MIE_G;
  const phaseM =
    ((3 / (8 * Math.PI)) * ((1 - g * g) * (1 + mu * mu))) / ((2 + g * g) * (1 + g * g - 2 * g * mu) ** 1.5);
  return [0, 1, 2].map((c) => (sumR[c] ?? 0) * (RAYLEIGH[c] ?? 0) * phaseR + sumM * MIE * phaseM) as [
    number,
    number,
    number,
  ];
}

/** Rec. 709 luminance of a linear colour. */
export function luminance(rgb: readonly [number, number, number]): number {
  return 0.2126 * rgb[0] + 0.7152 * rgb[1] + 0.0722 * rgb[2];
}

/** GLSL of the scattering integral (the twin of {@link scatteredLight}). */
const scatterGlsl = /* glsl */ `
const float EARTH_RADIUS = ${EARTH_RADIUS.toFixed(1)};
const float ATMOSPHERE_RADIUS = ${ATMOSPHERE_RADIUS.toFixed(1)};
const vec3 RAYLEIGH = vec3(${RAYLEIGH.map((x) => x.toExponential(3)).join(', ')});
const float MIE = ${MIE.toExponential(3)};
const float MIE_G = ${MIE_G.toFixed(3)};
const vec3 OZONE = vec3(${OZONE.map((x) => x.toExponential(3)).join(', ')});

float ozoneDensity(float height) {
  return max(0.0, 1.0 - abs(height - 25000.0) / 15000.0);
}
const float PI = 3.141592653589793;

float sphereExit(vec3 origin, vec3 direction, float radius) {
  float b = 2.0 * dot(origin, direction);
  float c = dot(origin, origin) - radius * radius;
  float d = b * b - 4.0 * c;
  if (d < 0.0) return -1.0;
  return (-b + sqrt(d)) / 2.0;
}

float groundHit(vec3 origin, vec3 direction) {
  float b = 2.0 * dot(origin, direction);
  float c = dot(origin, origin) - EARTH_RADIUS * EARTH_RADIUS;
  float d = b * b - 4.0 * c;
  if (d < 0.0) return -1.0;
  return (-b - sqrt(d)) / 2.0;
}

vec3 scatter(vec3 view, vec3 sun) {
  vec3 origin = vec3(0.0, EARTH_RADIUS + 10.0, 0.0);
  float exitT = max(sphereExit(origin, view, ATMOSPHERE_RADIUS), 0.0);
  float ground = groundHit(origin, view);
  float len = ground > 0.0 ? ground : exitT;
  float ds = len / float(VIEW_STEPS);
  float depthR = 0.0;
  float depthM = 0.0;
  float depthO = 0.0;
  vec3 sumR = vec3(0.0);
  float sumM = 0.0;
  for (int i = 0; i < VIEW_STEPS; i++) {
    float t = (float(i) + 0.5) * ds;
    vec3 p = origin + view * t;
    float height = length(p) - EARTH_RADIUS;
    float r = exp(-height / ${RAYLEIGH_HEIGHT.toFixed(1)}) * ds;
    float m = exp(-height / ${MIE_HEIGHT.toFixed(1)}) * ds;
    depthR += r;
    depthM += m;
    depthO += ozoneDensity(height) * ds;
    if (groundHit(p, sun) > 0.0) continue;
    float dl = max(sphereExit(p, sun, ATMOSPHERE_RADIUS), 0.0) / float(LIGHT_STEPS);
    float lightR = 0.0;
    float lightM = 0.0;
    float lightO = 0.0;
    for (int j = 0; j < LIGHT_STEPS; j++) {
      vec3 q = p + sun * ((float(j) + 0.5) * dl);
      float h = length(q) - EARTH_RADIUS;
      lightR += exp(-h / ${RAYLEIGH_HEIGHT.toFixed(1)}) * dl;
      lightM += exp(-h / ${MIE_HEIGHT.toFixed(1)}) * dl;
      lightO += ozoneDensity(h) * dl;
    }
    vec3 attenuation = exp(-(RAYLEIGH * (depthR + lightR) + MIE * 1.1 * (depthM + lightM) + OZONE * (depthO + lightO)));
    sumR += r * attenuation;
    sumM += m * attenuation.g;
  }
  float mu = dot(view, sun);
  float phaseR = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
  float g = MIE_G;
  float phaseM = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + mu * mu)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * mu, 1.5));
  return sumR * RAYLEIGH * phaseR + sumM * MIE * phaseM;
}
`;

const tableVertex = /* glsl */ `
varying vec2 vUv;
void main() {
  vUv = uv;
  gl_Position = vec4(position.xy, 0.0, 1.0);
}
`;

const tableFragment = /* glsl */ `
uniform float uSunAltitude; // radians
varying vec2 vUv;
${scatterGlsl}
void main() {
  float alt = radians(-5.0 + 95.0 * vUv.x * vUv.x);
  float az = radians(180.0 * vUv.y);
  vec3 view = vec3(cos(alt) * cos(az), sin(alt), cos(alt) * sin(az));
  vec3 sun = vec3(cos(uSunAltitude), sin(uSunAltitude), 0.0);
  gl_FragColor = vec4(scatter(view, sun), 1.0);
}
`;

/**
 * The scattering table on the GPU: rendered again when the Sun has moved (or the quality
 * changed). Sampled by the sky pass and by the stars (twilight shape and colour).
 */
export class AtmosphereTable {
  private target: THREE.WebGLRenderTarget;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1);
  private readonly material: THREE.ShaderMaterial;
  private renderedSun = Number.NaN;
  private quality: SkyQuality;

  constructor(quality: SkyQuality) {
    this.quality = quality;
    this.target = AtmosphereTable.createTarget(quality);
    this.material = this.createMaterial(quality);
    const quad = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), this.material);
    quad.frustumCulled = false;
    this.scene.add(quad);
  }

  private static createTarget(quality: SkyQuality): THREE.WebGLRenderTarget {
    const { width, height } = QUALITY[quality];
    return new THREE.WebGLRenderTarget(width, height, {
      type: THREE.HalfFloatType,
      format: THREE.RGBAFormat,
      minFilter: THREE.LinearFilter,
      magFilter: THREE.LinearFilter,
      wrapS: THREE.ClampToEdgeWrapping,
      wrapT: THREE.ClampToEdgeWrapping,
      depthBuffer: false,
    });
  }

  private createMaterial(quality: SkyQuality): THREE.ShaderMaterial {
    const { viewSteps, lightSteps } = QUALITY[quality];
    return new THREE.ShaderMaterial({
      vertexShader: tableVertex,
      fragmentShader: tableFragment,
      defines: { VIEW_STEPS: viewSteps, LIGHT_STEPS: lightSteps },
      uniforms: { uSunAltitude: { value: 0 } },
      depthTest: false,
      depthWrite: false,
    });
  }

  /** The table texture (the same object across quality changes). */
  readonly texture = { value: null as THREE.Texture | null };

  setQuality(quality: SkyQuality): void {
    if (quality === this.quality) {
      return;
    }
    this.quality = quality;
    this.target.dispose();
    this.target = AtmosphereTable.createTarget(quality);
    const { viewSteps, lightSteps } = QUALITY[quality];
    this.material.defines = { VIEW_STEPS: viewSteps, LIGHT_STEPS: lightSteps };
    this.material.needsUpdate = true;
    this.renderedSun = Number.NaN;
  }

  /** Renders the table for this Sun altitude if it moved by more than 0.05° since the last one. */
  update(renderer: THREE.WebGLRenderer, sunAltitudeDegrees: number): void {
    const sun = Math.max(SHAPE_MIN_SUN_ALTITUDE, sunAltitudeDegrees);
    if (Math.abs(sun - this.renderedSun) < 0.05) {
      return;
    }
    this.renderedSun = sun;
    (this.material.uniforms.uSunAltitude as THREE.IUniform<number>).value = sun * DEG;
    const previous = renderer.getRenderTarget();
    renderer.setRenderTarget(this.target);
    renderer.render(this.scene, this.camera);
    renderer.setRenderTarget(previous);
    this.texture.value = this.target.texture;
  }

  dispose(): void {
    this.target.dispose();
    this.material.dispose();
  }
}
