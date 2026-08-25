const encoder = new TextEncoder();

const C1 = 0xcc9e2d51;
const C2 = 0x1b873593;

const rotl = (x: number, r: number): number => ((x << r) | (x >>> (32 - r))) >>> 0;

/**
 * MurmurHash3 x86 32-bit over the UTF-8 bytes of `input`. Returns an unsigned 32-bit integer.
 *
 * Must stay byte-for-byte compatible with the server and the .NET SDK — verified against
 * spec/hash-vectors.json. `Math.imul` keeps multiplications in 32-bit space; `>>> 0` converts back
 * to unsigned.
 */
export function murmur3(input: string | Uint8Array, seed = 0): number {
  const data = typeof input === 'string' ? encoder.encode(input) : input;
  const length = data.length;
  const blocks = length >>> 2;
  let h1 = seed >>> 0;

  for (let i = 0; i < blocks; i++) {
    const o = i * 4;
    let k1 = (data[o]! | (data[o + 1]! << 8) | (data[o + 2]! << 16) | (data[o + 3]! << 24)) >>> 0;
    k1 = Math.imul(k1, C1);
    k1 = rotl(k1, 15);
    k1 = Math.imul(k1, C2);

    h1 ^= k1;
    h1 = rotl(h1, 13);
    h1 = (Math.imul(h1, 5) + 0xe6546b64) >>> 0;
  }

  const tail = blocks * 4;
  const remaining = length & 3;
  if (remaining > 0) {
    let k1 = 0;
    if (remaining === 3) k1 ^= data[tail + 2]! << 16;
    if (remaining >= 2) k1 ^= data[tail + 1]! << 8;
    k1 ^= data[tail]!;
    k1 = Math.imul(k1, C1);
    k1 = rotl(k1, 15);
    k1 = Math.imul(k1, C2);
    h1 ^= k1;
  }

  h1 ^= length;
  h1 ^= h1 >>> 16;
  h1 = Math.imul(h1, 0x85ebca6b);
  h1 ^= h1 >>> 13;
  h1 = Math.imul(h1, 0xc2b2ae35);
  h1 ^= h1 >>> 16;
  return h1 >>> 0;
}
