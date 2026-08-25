import { murmur3 } from './murmur3.js';

export const BUCKET_SCALE = 10_000;

/** `murmur3(utf8("{key}.{salt}.{value}")) % 10000` — identical on the server and in every SDK. */
export function bucket(key: string, salt: string, value: string): number {
  return murmur3(`${key}.${salt}.${value}`) % BUCKET_SCALE;
}

/**
 * Returns the index of the slot containing `bucketValue` when walking cumulative weights.
 * Remainders (weights summing to less than 10000) fall into the last slot.
 */
export function pick<T>(slots: readonly T[], weight: (slot: T) => number, bucketValue: number): number {
  if (slots.length === 0) return -1;

  let cumulative = 0;
  for (let i = 0; i < slots.length; i++) {
    cumulative += weight(slots[i]!);
    if (bucketValue < cumulative) return i;
  }
  return slots.length - 1;
}
