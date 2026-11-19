import { readdirSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export const repoRoot = fileURLToPath(new URL('../../../', import.meta.url));
export const bridgeFixtures = `${repoRoot}tests/fixtures/bridge/`;

export function readJson(path: string): unknown {
  return JSON.parse(readFileSync(path, 'utf8')) as unknown;
}

export function jsonFiles(directory: string): string[] {
  return readdirSync(`${bridgeFixtures}${directory}`)
    .filter((name) => name.endsWith('.json'))
    .map((name) => `${bridgeFixtures}${directory}/${name}`);
}
