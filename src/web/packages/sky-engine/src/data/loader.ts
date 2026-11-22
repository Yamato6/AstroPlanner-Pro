// Loads the static sky data from the data host (https://data.astroplanner/ in the app).
import { z } from 'zod';
import {
  decodeTable,
  DSO_RECORD_BYTES,
  SEGMENT_RECORD_BYTES,
  STAR_RECORD_BYTES,
  type Table,
} from './buffers';

const dsoIndexSchema = z.strictObject({
  types: z.array(z.string()),
  objects: z.array(z.tuple([z.string(), z.string(), z.string()])),
});

const constellationSchema = z.array(
  z.strictObject({
    abbr: z.string(),
    name: z.string(),
    genitive: z.string(),
    rank: z.number(),
    label: z.tuple([z.number(), z.number(), z.number()]),
  }),
);

export type Constellation = z.infer<typeof constellationSchema>[number];

export interface SkyData {
  readonly stars: Table;
  readonly starNames: ReadonlyMap<number, string>;
  readonly dsos: Table;
  readonly dsoTypes: readonly string[];
  /** id, common name, constellation; same order as `dsos`. */
  readonly dsoIndex: readonly (readonly [string, string, string])[];
  readonly constellationLines: Table;
  readonly constellationBounds: Table;
  readonly milkyWay: Table;
  readonly constellations: readonly Constellation[];
}

async function fetchChecked(url: string): Promise<Response> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`sky data: ${url} → HTTP ${response.status}`);
  }
  return response;
}

export async function loadSkyData(baseUrl: string): Promise<SkyData> {
  const url = (file: string): string => new URL(file, baseUrl).toString();
  const binary = async (file: string, recordBytes: number): Promise<Table> =>
    decodeTable(await (await fetchChecked(url(file))).arrayBuffer(), recordBytes);
  const json = async (file: string): Promise<unknown> => (await fetchChecked(url(file))).json();

  const [stars, dsos, lines, bounds, milkyWay, names, dsoIndex, constellations] = await Promise.all([
    binary('stars.bin', STAR_RECORD_BYTES),
    binary('dsos.bin', DSO_RECORD_BYTES),
    binary('constellation-lines.bin', SEGMENT_RECORD_BYTES),
    binary('constellation-bounds.bin', SEGMENT_RECORD_BYTES),
    binary('milkyway.bin', SEGMENT_RECORD_BYTES),
    json('star-names.json'),
    json('dsos.json'),
    json('constellation-labels.json'),
  ]);

  const parsedNames = z.record(z.string(), z.string()).parse(names);
  const index = dsoIndexSchema.parse(dsoIndex);
  if (index.objects.length !== dsos.count) {
    throw new Error('sky data: dsos.json and dsos.bin disagree');
  }

  return {
    stars,
    starNames: new Map(Object.entries(parsedNames).map(([id, label]) => [Number(id), label])),
    dsos,
    dsoTypes: index.types,
    dsoIndex: index.objects,
    constellationLines: lines,
    constellationBounds: bounds,
    milkyWay,
    constellations: constellationSchema.parse(constellations),
  };
}
