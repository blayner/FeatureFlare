import { describe, expect, it } from 'vitest';
import { ConfigCache } from '../src/cache.js';
import type { FlagConfig, SdkConfig } from '../src/types.js';
import { sampleConfig } from './fixtures.js';

const flag = (key: string, version: number, enabled = true): FlagConfig => ({
  ...sampleConfig().flags[1]!,
  key,
  version,
  enabled,
});

const config = (...flags: FlagConfig[]): SdkConfig => ({ ...sampleConfig(), flags });

describe('ConfigCache snapshot merging', () => {
  it('keeps a streamed version that is newer than the snapshot', () => {
    const cache = new ConfigCache(60_000);
    const since = cache.mark();
    cache.upsertFlag(flag('a', 5, false));

    cache.replaceAll(config(flag('a', 3)), since);

    expect(cache.getFlag('a')).toMatchObject({ version: 5, enabled: false });
  });

  it('does not resurrect an item deleted on the stream', () => {
    const cache = new ConfigCache(60_000);
    cache.replaceAll(config(flag('a', 1)));
    const since = cache.mark();
    cache.deleteFlag({ key: 'a', version: 2 });

    cache.replaceAll(config(flag('a', 1)), since);

    expect(cache.getFlag('a')).toBeUndefined();
  });

  it('remembers deletions that arrive before the item itself', () => {
    const cache = new ConfigCache(60_000);
    const since = cache.mark();
    expect(cache.deleteFlag({ key: 'a', version: 2 })).toBe(false); // nothing to delete yet

    cache.replaceAll(config(flag('a', 1)), since);

    expect(cache.getFlag('a')).toBeUndefined();
  });

  it('keeps items created on the stream after the snapshot was requested', () => {
    const cache = new ConfigCache(60_000);
    cache.replaceAll(config(flag('a', 1)));
    const since = cache.mark();
    cache.upsertFlag(flag('b', 1));

    cache.replaceAll(config(flag('a', 1)), since);

    expect(cache.getFlag('b')).toBeDefined();
  });

  it('removes items missing from a snapshot taken after their last change', () => {
    const cache = new ConfigCache(60_000);
    cache.replaceAll(config(flag('a', 1), flag('b', 1)));

    const changed = cache.replaceAll(config(flag('a', 2)), cache.mark());

    expect(cache.getFlag('b')).toBeUndefined();
    expect(changed.sort()).toEqual(['a', 'b']);
  });

  it('a recreated item with a higher version replaces the tombstone', () => {
    const cache = new ConfigCache(60_000);
    cache.deleteFlag({ key: 'a', version: 2 });

    expect(cache.upsertFlag(flag('a', 3))).toBe(true);
    expect(cache.getFlag('a')?.version).toBe(3);
  });
});
