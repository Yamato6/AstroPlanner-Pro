import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const repoRoot = fileURLToPath(new URL('../../../../../', import.meta.url));
const appFixtures = `${repoRoot}tests/fixtures/bridge/app/`;

export function readJson(path: string): unknown {
  return JSON.parse(readFileSync(path, 'utf8')) as unknown;
}

/** The golden files of one direction (outgoing, incoming, invalid), written or read by C# too. */
export function jsonFiles(directory: string): string[] {
  return readdirSync(`${appFixtures}${directory}`)
    .filter((name) => name.endsWith('.json'))
    .map((name) => `${appFixtures}${directory}/${name}`);
}
