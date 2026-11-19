// Decoding of the binary sky files written by tools/AstroPlanner.DataBuilder (SkyDataBuilder).
// Header: "APSB", format version, record count, record size (uint32 little-endian), then records.

export const FORMAT_VERSION = 1;
export const HEADER_BYTES = 16;
export const STAR_RECORD_BYTES = 28;
export const DSO_RECORD_BYTES = 32;
export const SEGMENT_RECORD_BYTES = 28;

export interface Table {
  readonly count: number;
  readonly recordBytes: number;
  /** All records as float32 (stride = recordBytes / 4). Integer fields must be read via `uint32`. */
  readonly float32: Float32Array;
  readonly uint32: Uint32Array;
}

export function decodeTable(buffer: ArrayBuffer, expectedRecordBytes: number): Table {
  if (buffer.byteLength < HEADER_BYTES) {
    throw new Error('sky data: file too short');
  }
  const view = new DataView(buffer);
  const magic = String.fromCharCode(view.getUint8(0), view.getUint8(1), view.getUint8(2), view.getUint8(3));
  const version = view.getUint32(4, true);
  const count = view.getUint32(8, true);
  const recordBytes = view.getUint32(12, true);
  if (magic !== 'APSB' || version !== FORMAT_VERSION) {
    throw new Error(`sky data: bad header (${magic} v${version})`);
  }
  if (recordBytes !== expectedRecordBytes || buffer.byteLength !== HEADER_BYTES + count * recordBytes) {
    throw new Error(`sky data: unexpected layout (${count} × ${recordBytes} bytes)`);
  }
  const body = buffer.slice(HEADER_BYTES);
  return { count, recordBytes, float32: new Float32Array(body), uint32: new Uint32Array(body) };
}

/** Star fields: x, y, z, magnitude, B−V (float32); HYG id, HIP (uint32). */
export const Star = { x: 0, y: 1, z: 2, magnitude: 3, colorIndex: 4, hygId: 5, hip: 6, stride: 7 } as const;

/** DSO fields: x, y, z, magnitude (99 = unknown), major, minor (arcmin), PA (−1 = unknown); type (uint32). */
export const Dso = {
  x: 0,
  y: 1,
  z: 2,
  magnitude: 3,
  major: 4,
  minor: 5,
  positionAngle: 6,
  type: 7,
  stride: 8,
} as const;

/** Segment fields: x1, y1, z1, x2, y2, z2 (float32); group (uint32). */
export const Segment = { group: 6, stride: 7 } as const;

export const UNKNOWN_MAGNITUDE = 99;
