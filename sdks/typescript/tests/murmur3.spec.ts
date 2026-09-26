import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { bucket, pick } from '../src/bucketing.js';
import { murmur3 } from '../src/murmur3.js';

interface Vectors {
  hashVectors: { input: string; seed: number; hash: number }[];
  bucketVectors: { key: string; salt: string; value: string; bucket: number }[];
}

// Shared with the server and the .NET SDK: identical results guarantee identical user assignment.
const vectors = JSON.parse(
  readFileSync(new URL('../../../spec/hash-vectors.json', import.meta.url), 'utf8'),
) as Vectors;

describe('murmur3 (spec/hash-vectors.json)', () => {
  it.each(vectors.hashVectors)('hash($input, seed $seed) = $hash', ({ input, seed, hash }) => {
    expect(murmur3(input, seed)).toBe(hash);
  });

  it.each(vectors.bucketVectors)('bucket($key, $salt, $value) = $bucket', ({ key, salt, value, bucket: expected }) => {
    expect(bucket(key, salt, value)).toBe(expected);
  });

  it('hashes Uint8Array input identically to strings', () => {
    expect(murmur3(new TextEncoder().encode('ação'))).toBe(murmur3('ação'));
  });
});

describe('pick', () => {
  it('walks cumulative weights and sends the remainder to the last slot', () => {
    const weights = [2500, 2500, 4000];
    expect(pick(weights, (w) => w, 0)).toBe(0);
    expect(pick(weights, (w) => w, 2500)).toBe(1);
    expect(pick(weights, (w) => w, 8999)).toBe(2);
    expect(pick(weights, (w) => w, 9999)).toBe(2);
  });
});
