// Contract tests (spec §5.4): the golden samples of tests/fixtures/bridge, shared with C#.
import { describe, expect, it } from 'vitest';
import { hostMessageSchema, rendererMessageSchema } from '../src/bridge/schema';
import { jsonFiles, readJson } from './fixtures';

describe('C# to renderer messages', () => {
  const files = jsonFiles('outgoing');

  it('cover every host message type', () => {
    const types = files.map((f) => f.replace(/^.*\//, '').replace('.json', '')).sort();
    const schemaTypes = hostMessageSchema.options.map((o) => o.shape.type.value).sort();
    expect(types).toEqual(schemaTypes);
  });

  it.each(files)('%s is valid', (file) => {
    const result = hostMessageSchema.safeParse(readJson(file));
    expect(result.success, JSON.stringify(result.error?.issues)).toBe(true);
  });
});

describe('renderer to C# messages', () => {
  it.each(jsonFiles('incoming'))('%s is accepted', (file) => {
    const result = rendererMessageSchema.safeParse(readJson(file));
    expect(result.success, JSON.stringify(result.error?.issues)).toBe(true);
  });

  it.each(jsonFiles('invalid'))('%s is rejected', (file) => {
    expect(rendererMessageSchema.safeParse(readJson(file)).success).toBe(false);
  });

  it('covers every renderer message type with an accepted sample', () => {
    const types = new Set(jsonFiles('incoming').map((f) => (readJson(f) as { type: string }).type));
    for (const option of rendererMessageSchema.options) {
      expect(types).toContain(option.shape.type.value);
    }
  });
});
