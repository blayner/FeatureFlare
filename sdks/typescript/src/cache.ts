import type { Deletion, ExperimentConfig, FlagConfig, SdkConfig, SegmentConfig } from './types.js';

interface Versioned {
  key: string;
  version: number;
}

/**
 * In-memory copy of the server configuration used for local evaluation.
 *
 * - Updates carry a version; older or equal versions are ignored, so out-of-order delivery (a poll
 *   response racing a stream event) can never roll a flag back.
 * - `isStale()` reports when the snapshot is older than `ttlMs`, which the client uses to trigger a refresh
 *   when streaming is unavailable.
 */
export class ConfigCache {
  private flags = new Map<string, FlagConfig>();
  private segments = new Map<string, SegmentConfig>();
  private experiments = new Map<string, ExperimentConfig>();
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

  /**
   * Replaces the whole snapshot. Returns the keys of flags whose version changed (including removed flags)
   * so the client can emit change notifications.
   */
  replaceAll(config: SdkConfig): string[] {
    const changed: string[] = [];
    const nextFlags = toMap(config.flags);

    for (const [key, flag] of nextFlags) {
      if (this.flags.get(key)?.version !== flag.version) changed.push(key);
    }
    for (const key of this.flags.keys()) {
      if (!nextFlags.has(key)) changed.push(key);
    }

    this.flags = nextFlags;
    this.segments = toMap(config.segments);
    this.experiments = toMap(config.experiments ?? []);
    this.touch();
    this.initialized = true;
    return changed;
  }

  upsertFlag(flag: FlagConfig): boolean {
    return upsert(this.flags, flag) && this.touch();
  }

  upsertSegment(segment: SegmentConfig): boolean {
    return upsert(this.segments, segment) && this.touch();
  }

  upsertExperiment(experiment: ExperimentConfig): boolean {
    return upsert(this.experiments, experiment) && this.touch();
  }

  deleteFlag(deletion: Deletion): boolean {
    return remove(this.flags, deletion) && this.touch();
  }

  deleteSegment(deletion: Deletion): boolean {
    return remove(this.segments, deletion) && this.touch();
  }

  deleteExperiment(deletion: Deletion): boolean {
    return remove(this.experiments, deletion) && this.touch();
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

function toMap<T extends Versioned>(items: readonly T[]): Map<string, T> {
  return new Map(items.map((item) => [item.key, item]));
}

function upsert<T extends Versioned>(map: Map<string, T>, item: T): boolean {
  const current = map.get(item.key);
  if (current && current.version >= item.version) return false;
  map.set(item.key, item);
  return true;
}

function remove<T extends Versioned>(map: Map<string, T>, deletion: Deletion): boolean {
  const current = map.get(deletion.key);
  if (!current || current.version >= deletion.version) return false;
  map.delete(deletion.key);
  return true;
}
