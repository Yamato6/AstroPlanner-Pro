// The survey layer's geometry (spec §8.3), without WebGL: which HiPS tiles a view needs, at which
// order, where each one's pixels are (a tile of its own, part of an ancestor tile, or a cell of
// the order-3 Allsky atlas) and the subdivided grid that places it on the sky. HiPS tiles are
// HEALPix NESTED pixels; inside a tile image the row follows the face's x and the column its y
// (as Core's FramingPreview reads them, checked against DSS stars).
import { faceToVector, nestedToFaceXy, tileCap, type TileCap } from '../math/healpix';
import type { Vec3 } from '../math/frame';

/** A HiPS survey served by the host's tile proxy (`survey.get`). */
export interface SurveyConfig {
  readonly id: string;
  /** The proxy's root, e.g. https://tiles.astroplanner/ */
  readonly baseUrl: string;
  readonly maxOrder: number;
  readonly tileWidth: number;
  readonly credit: string;
}

/** HiPS tiles start at order 3; below it the layer shows the order-3 Allsky atlas. */
export const SURVEY_MIN_ORDER = 3;
/** The Allsky image: every order-3 tile reduced and laid out 27 to a row. */
export const ALLSKY_COLUMNS = 27;
/** At most this many tiles at once (a 4K screen at the matching order needs ~60). */
export const MAX_VISIBLE_TILES = 160;

/** Mean side of a HEALPix pixel at `order`, degrees (√(4π/12) rad at order 0). */
export function pixelSideDegrees(order: number): number {
  return 58.6323 / 2 ** order;
}

/**
 * The order whose texels match the screen's pixels (`degreesPerPixel`, device pixels): a tile of
 * `tileWidth` texels covers one pixel of that order. Clamped to the survey's orders.
 */
export function surveyOrder(degreesPerPixel: number, tileWidth: number, maxOrder: number): number {
  const ideal = Math.log2(pixelSideDegrees(0) / (tileWidth * Math.max(degreesPerPixel, 1e-9)));
  return Math.min(maxOrder, Math.max(SURVEY_MIN_ORDER, Math.round(ideal)));
}

/** How much of the survey shows at a field of view: none above 30°, all below 15° (spec §8.3). */
export function surveyOpacity(fovDegrees: number): number {
  const t = Math.min(1, Math.max(0, (30 - fovDegrees) / 15));
  return t * t * (3 - 2 * t);
}

function angleDegrees(a: Vec3, b: Vec3): number {
  const c = a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
  return (Math.acos(Math.min(1, Math.max(-1, c))) * 180) / Math.PI;
}

/**
 * The NESTED tiles at `order` that meet the view cone (`center`, `radiusDegrees`), nearest the
 * centre first; `accept` can drop more (below the horizon). Walks down from the 12 base pixels,
 * keeping only branches whose bounding cap meets the cone.
 */
export function visibleTiles(
  order: number,
  center: Vec3,
  radiusDegrees: number,
  accept: (cap: TileCap) => boolean = () => true,
): number[] {
  const found: { nested: number; distance: number }[] = [];
  const walk = (level: number, nested: number): void => {
    const cap = tileCap(level, nested);
    const distance = angleDegrees(cap.center, center);
    if (distance > radiusDegrees + cap.radiusDegrees || !accept(cap)) {
      return;
    }
    if (level === order) {
      found.push({ nested, distance });
      return;
    }
    for (let child = 0; child < 4; child++) {
      walk(level + 1, nested * 4 + child);
    }
  };
  for (let face = 0; face < 12; face++) {
    walk(0, face);
  }
  found.sort((a, b) => a.distance - b.distance);
  return found.slice(0, MAX_VISIBLE_TILES).map((f) => f.nested);
}

/** The proxy URL of a tile (HiPS 1.0 layout). */
export function tileUrl(config: SurveyConfig, order: number, nested: number): string {
  const directory = Math.floor(nested / 10000) * 10000;
  return `${config.baseUrl}${config.id}/Norder${order}/Dir${directory}/Npix${nested}.jpg`;
}

export function allskyUrl(config: SurveyConfig): string {
  return `${config.baseUrl}${config.id}/Norder${SURVEY_MIN_ORDER}/Allsky.jpg`;
}

/** A texture rectangle: u0, v0 (from the top-left), width, height. */
export type UvRect = readonly [number, number, number, number];

/** Where tile (order, nested) lies inside its ancestor at `ancestorOrder`, in the ancestor's texture. */
export function ancestorRect(
  order: number,
  nested: number,
  ancestorOrder: number,
  ancestor: UvRect = [0, 0, 1, 1],
): UvRect {
  const levels = order - ancestorOrder;
  const scale = 2 ** -levels;
  const { x, y } = nestedToFaceXy(order, nested);
  const fx = (x - Math.floor(x / 2 ** levels) * 2 ** levels) * scale;
  const fy = (y - Math.floor(y / 2 ** levels) * 2 ** levels) * scale;
  // u (columns) follows the face's y, v (rows) its x.
  return [
    ancestor[0] + ancestor[2] * fy,
    ancestor[1] + ancestor[3] * fx,
    ancestor[2] * scale,
    ancestor[3] * scale,
  ];
}

/** The cell of order-3 tile `nested3` in an Allsky image of `width`×`height` pixels. */
export function allskyRect(nested3: number, width: number, height: number): UvRect {
  const cell = width / ALLSKY_COLUMNS;
  const column = nested3 % ALLSKY_COLUMNS;
  const row = Math.floor(nested3 / ALLSKY_COLUMNS);
  return [(column * cell) / width, (row * cell) / height, cell / width, cell / height];
}

/** The ancestor of a tile at `ancestorOrder`. */
export function ancestorOf(order: number, nested: number, ancestorOrder: number): number {
  return Math.floor(nested / 4 ** (order - ancestorOrder));
}

/** A tile's grid: J2000 positions (3 per vertex), its own texture coordinates (2) and triangles. */
export interface TileGrid {
  readonly positions: Float32Array;
  readonly uvs: Float32Array;
  readonly indices: Uint16Array;
}

/** Grid cells per side: about one every 1.5° (so the projection bends long edges), 2–16. */
export function tileCells(order: number): number {
  return Math.min(16, Math.max(2, Math.ceil(pixelSideDegrees(order) / 1.5)));
}

export function tileGrid(order: number, nested: number, cells = tileCells(order)): TileGrid {
  const { face, x, y } = nestedToFaceXy(order, nested);
  const side = cells + 1;
  const positions = new Float32Array(side * side * 3);
  const uvs = new Float32Array(side * side * 2);
  for (let i = 0; i < side; i++) {
    for (let j = 0; j < side; j++) {
      const fx = i / cells;
      const fy = j / cells;
      const k = i * side + j;
      positions.set(faceToVector(order, face, x + fx, y + fy), k * 3);
      uvs[k * 2] = fy;
      uvs[k * 2 + 1] = fx;
    }
  }
  const indices = new Uint16Array(cells * cells * 6);
  let n = 0;
  for (let i = 0; i < cells; i++) {
    for (let j = 0; j < cells; j++) {
      const a = i * side + j;
      const b = a + 1;
      const c = a + side;
      const d = c + 1;
      indices.set([a, c, b, b, c, d], n);
      n += 6;
    }
  }
  return { positions, uvs, indices };
}
