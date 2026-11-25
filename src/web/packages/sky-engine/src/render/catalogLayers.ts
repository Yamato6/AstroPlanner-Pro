// The sky view's scene objects built from the sky data: star and deep-sky points, catalog lines
// (constellations, Milky Way outline) and computed grids, all drawn by the projection shaders.
import * as THREE from 'three';
import { Dso, Star, UNKNOWN_MAGNITUDE } from '../data/buffers';
import type { SkyData } from '../data/loader';
import { dsoShape, palette, starColor } from './colors';
import { segmentVertices } from './grids';
import { dsoFragment, dsoVertex, lineFragment, lineVertex, starFragment, starVertex } from './shaders';
import { color, type Rgb } from './skyStyle';

type Uniforms = Record<string, THREE.IUniform>;

/** Builds the scene objects of the sky view; every material shares the view's uniforms. */
export class LayerFactory {
  constructor(
    private readonly scene: THREE.Scene,
    private readonly shared: Uniforms,
    private readonly pixelRatio: number,
  ) {}

  private material(vertexShader: string, fragmentShader: string, uniforms: Uniforms, frame: Uniforms = {}) {
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

  private lineMaterial(rgb: Rgb, opacity: THREE.IUniform, groupFade: number, frame: Uniforms) {
    return this.material(
      lineVertex,
      lineFragment,
      { uColor: { value: color(rgb) }, uOpacity: opacity, uGroupFade: { value: groupFade } },
      frame,
    );
  }

  /** Line segments of a catalog table (each group fades as a whole at the edge of the dome). */
  segments(table: SkyData['milkyWay'], rgb: Rgb, opacity: THREE.IUniform, groupFade = 0): THREE.LineSegments {
    const { positions, groups } = segmentVertices(table);
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('group', new THREE.BufferAttribute(groups, 1));
    return this.add(new THREE.LineSegments(geometry, this.lineMaterial(rgb, opacity, groupFade, {})));
  }

  /** Line segments of computed vertices (grids), in the J2000 frame or the one `frame` gives. */
  lines(positions: Float32Array, rgb: Rgb, opacity: number, frame: Uniforms = {}): THREE.LineSegments {
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
    geometry.setAttribute('group', new THREE.BufferAttribute(new Float32Array(positions.length / 3), 1));
    return this.add(new THREE.LineSegments(geometry, this.lineMaterial(rgb, { value: opacity }, 0, frame)));
  }

  /** The stars; `multiplier` copies of the catalog for the stress benchmark. */
  stars(data: SkyData, multiplier: number, uniforms: Uniforms): { points: THREE.Points; count: number } {
    const { stars } = data;
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
      ...uniforms,
      uPixelRatio: { value: this.pixelRatio },
    });
    return { points: this.add(new THREE.Points(geometry, material)), count };
  }

  /**
   * The deep-sky objects, with a highlight per object (0 none, 1 planned, 2 selected) the view
   * updates; fills `idIndex` (catalog id → index).
   */
  dsos(
    data: SkyData,
    uniforms: Uniforms,
    idIndex: Map<string, number>,
  ): { points: THREE.Points; highlight: Float32Array } {
    const { dsos, dsoIndex, dsoTypes } = data;
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
        idIndex.set(id, i);
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
      ...uniforms,
      uPixelRatio: { value: this.pixelRatio },
      uColors: { value: palette.dsoShapes.map(color) },
      uPlanColor: { value: color(palette.plan) },
      uSelectedColor: { value: color(palette.selected) },
    });
    material.blending = THREE.NormalBlending;
    const points = new THREE.Points(geometry, material);
    points.renderOrder = 5;
    return { points: this.add(points), highlight };
  }

  private add<T extends THREE.Object3D>(object: T): T {
    object.frustumCulled = false;
    this.scene.add(object);
    return object;
  }
}
