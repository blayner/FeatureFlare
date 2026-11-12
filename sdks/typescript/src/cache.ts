import type { Deletion, ExperimentConfig, FlagConfig, SdkConfig, SegmentConfig } from './types.js';

interface Versioned {
  key: string;
  version: number;
}

/**
 * Versioned key/value store that tolerates out-of-order delivery between the SSE stream and full snapshot
 * downloads.
 *
 * The stream and `GET /api/sdk/config` race: a snapshot requested at time T can arrive *after* stream
 * events newer than T have been applied. Blindly replacing the map would roll those changes back (and
 * resurrect deleted items), so a snapshot is merged instead:
 *
 * - an existing item with a higher version than the snapshot's copy is kept;
 * - a deletion (tombstone) with a version >= the snapshot's copy keeps the item deleted;
 * - an item missing from the snapshot is removed, unless the stream touched it after the snapshot was
 *   requested (it was created after the snapshot was generated).
 */
class VersionedMap<T extends Versioned> {
  private items = new Map<string, T>();
  private tombstones = new Map<string, { version: number; seq: number }>();
  private touched = new Map<string, number>();

  get(key: string): T | undefined {
    return this.items.get(key);
  }

  values(): IterableIterator<T> {
    return this.items.values();
  }

  upsert(item: T, seq: number): boolean {
    const current = this.items.get(item.key);
    if (current && current.version >= item.version) return false;
    const tombstone = this.tombstones.get(item.key);
    if (tombstone && tombstone.version >= item.version) return false;

    this.items.set(item.key, item);
    this.tombstones.delete(item.key);
    this.touched.set(item.key, seq);
    return true;
  }

  /** Returns true when an item was actually removed. A tombstone is recorded either way. */
  delete(deletion: Deletion, seq: number): boolean {
    const current = this.items.get(deletion.key);
    if (current && current.version >= deletion.version) return false;

    const tombstone = this.tombstones.get(deletion.key);
    if (!tombstone || tombstone.version < deletion.version) {
      this.tombstones.set(deletion.key, { version: deletion.version, seq });
    }
    this.touched.set(deletion.key, seq);
    return this.items.delete(deletion.key);
  }

  /**
   * Merges a full snapshot requested at sequence `since`. Returns the keys whose visible state changed.
   */
  replaceAll(snapshot: readonly T[], since: number): string[] {
    const next = new Map<string, T>();
    const inSnapshot = new Set(snapshot.map((s) => s.key));

    for (const incoming of snapshot) {
      const current = this.items.get(incoming.key);
      const tombstone = this.tombstones.get(incoming.key);
      if (current && current.version > incoming.version) {
        next.set(incoming.key, current);
      } else if (tombstone && tombstone.version >= incoming.version) {
        continue;
      } else {
        next.set(incoming.key, incoming);
      }
    }

    for (const [key, current] of this.items) {
      if (!next.has(key) && !inSnapshot.has(key) && (this.touched.get(key) ?? -1) > since) {
        next.set(key, current); // created by the stream after the snapshot was requested
      }
    }

    const changed: string[] = [];
    for (const [key, item] of next) {
      if (this.items.get(key)?.version !== item.version) changed.push(key);
    }
    for (const key of this.items.keys()) {
      if (!next.has(key)) changed.push(key);
    }

    this.items = next;
    // Tombstones and touch marks older than the snapshot are now reflected by it; drop them so the
    // bookkeeping does not grow without bound.
    for (const [key, t] of this.tombstones) if (t.seq <= since) this.tombstones.delete(key);
    for (const [key, s] of this.touched) if (s <= since) this.touched.delete(key);
    return changed;
  }
}

/**
 * In-memory copy of the server configuration used for local evaluation.
 *
 * `isStale()` reports when the snapshot is older than `ttlMs`, which the client uses to trigger a refresh
 * when streaming is unavailable.
 */
export class ConfigCache {
  private readonly flags = new VersionedMap<FlagConfig>();
  private readonly segments = new VersionedMap<SegmentConfig>();
  private readonly experiments = new VersionedMap<ExperimentConfig>();
  private seq = 0;
  private updatedAt = 0;
  private initialized = false;

  constructor(
    private readonly ttlMs: number,
    private readonly now: () => number = Date.now,
  ) {}

  get isInitialized(): boolean {
    return this.initialized;
  }

  isStale(): boolean {
    return !this.initialized || this.now() - this.updatedAt > this.ttlMs;
  }

  /** Call right before requesting a snapshot and pass the result to {@link replaceAll}. */
  mark(): number {
    return this.seq;
  }

  /**
   * Merges a full snapshot (see {@link VersionedMap}). Returns the keys of flags whose value may have
   * changed, including removed flags, so the client can emit change notifications.
   */
  replaceAll(config: SdkConfig, since: number = this.seq): string[] {
    const changed = this.flags.replaceAll(config.flags, since);
    this.segments.replaceAll(config.segments, since);
    this.experiments.replaceAll(config.experiments ?? [], since);
    this.touch();
    this.initialized = true;
    return changed;
  }

  upsertFlag(flag: FlagConfig): boolean {
    return this.flags.upsert(flag, ++this.seq) && this.touch();
  }

  upsertSegment(segment: SegmentConfig): boolean {
    return this.segments.upsert(segment, ++this.seq) && this.touch();
  }

  upsertExperiment(experiment: ExperimentConfig): boolean {
    return this.experiments.upsert(experiment, ++this.seq) && this.touch();
  }

  deleteFlag(deletion: Deletion): boolean {
    return this.flags.delete(deletion, ++this.seq) && this.touch();
  }

  deleteSegment(deletion: Deletion): boolean {
    return this.segments.delete(deletion, ++this.seq) && this.touch();
  }

  deleteExperiment(deletion: Deletion): boolean {
    return this.experiments.delete(deletion, ++this.seq) && this.touch();
  }

  getFlag(key: string): FlagConfig | undefined {
    return this.flags.get(key);
  }

  getSegment(key: string): SegmentConfig | undefined {
    return this.segments.get(key);
  }

  getExperiment(key: string): ExperimentConfig | undefined {
    return this.experiments.get(key);
  }

  allFlags(): FlagConfig[] {
    return [...this.flags.values()];
  }

  /** Flags whose rules reference `segmentKey`; used to notify about indirect changes. */
  flagsUsingSegment(segmentKey: string): string[] {
    return this.allFlags()
      .filter((f) =>
        f.rules.some((r) => r.clauses.some((c) => c.operator === 'inSegment' && c.values.includes(segmentKey))),
      )
      .map((f) => f.key);
  }

  private touch(): true {
    this.updatedAt = this.now();
    return true;
  }
}
