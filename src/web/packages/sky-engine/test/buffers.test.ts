import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import {
  decodeTable,
  DSO_RECORD_BYTES,
  SEGMENT_RECORD_BYTES,
  Star,
  STAR_RECORD_BYTES,
} from '../src/data/buffers';
import { repoRoot } from './fixtures';

function load(file: string): ArrayBuffer {
  const bytes = readFileSync(`${repoRoot}data/build/sky/${file}`);
  return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength);
}

function header(count: number, recordBytes: number, magic = 'APSB', version = 1): ArrayBuffer {
  const buffer = new ArrayBuffer(16 + count * recordBytes);
  const view = new DataView(buffer);
  for (let i = 0; i < magic.length; i++) {
    view.setUint8(i, magic.charCodeAt(i));
  }
  view.setUint32(4, version, true);
  view.setUint32(8, count, true);
  view.setUint32(12, recordBytes, true);
  return buffer;
}

describe('sky data buffers', () => {
  it('decode the built star catalogue, brightest first (Sirius)', () => {
    const stars = decodeTable(load('stars.bin'), STAR_RECORD_BYTES);
    expect(stars.count).toBeGreaterThan(80_000);
    expect(stars.uint32[Star.hygId]).toBe(32263);
    expect(stars.float32[Star.magnitude]).toBeCloseTo(-1.44, 2);
    const [x = 0, y = 0, z = 0] = stars.float32.subarray(0, 3);
    expect(Math.hypot(x, y, z)).toBeCloseTo(1, 5);
  });

  it('decode the other tables', () => {
    expect(decodeTable(load('dsos.bin'), DSO_RECORD_BYTES).count).toBeGreaterThan(13_000);
    expect(decodeTable(load('constellation-lines.bin'), SEGMENT_RECORD_BYTES).count).toBeGreaterThan(600);
    expect(decodeTable(load('milkyway.bin'), SEGMENT_RECORD_BYTES).count).toBeGreaterThan(1000);
  });

  it('reject bad headers and layouts', () => {
    expect(() => decodeTable(new ArrayBuffer(4), 28)).toThrow(/too short/);
    expect(() => decodeTable(header(1, 28, 'XXXX'), 28)).toThrow(/bad header/);
    expect(() => decodeTable(header(1, 28, 'APSB', 2), 28)).toThrow(/bad header/);
    expect(() => decodeTable(header(1, 32), 28)).toThrow(/layout/);
    expect(decodeTable(header(2, 28), 28).count).toBe(2);
  });
});
