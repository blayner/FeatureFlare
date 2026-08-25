import { ConfigCache } from './cache.js';
import { TypedEmitter } from './emitter.js';
import { Evaluator } from './evaluator.js';
import { EventQueue } from './events.js';
import { silentLogger, type Logger } from './logger.js';
import { SseListener, unref, type StreamTransport } from './sse-listener.js';
import type { SseMessage } from './sse-parser.js';
import type {
  Deletion,
  EvaluationContext,
  EvaluationDetail,
  ExperimentAssignment,
  ExperimentConfig,
  FlagConfig,
  FlagType,
  FlagValueMap,
  JsonValue,
  SdkConfig,
  SegmentConfig,
  StreamEvent,
} from './types.js';

export interface FeatureFlareOptions {
  /** SDK key (read-only). Never ship an admin key in an application. */
  apiKey: string;
  baseUrl: string;
  /** Receive changes in real time over SSE. Default: true. */
  enableStreaming?: boolean;
  /** Which SSE implementation to use. Default: 'auto' (native EventSource when available). */
  streamTransport?: StreamTransport;
  /** Polling interval used when streaming is disabled or disconnected. Default: 30s. */
  refreshInterval?: number;
  /** Max age of the local snapshot before a refresh is forced. Default: 5 min. */
  cacheTtl?: number;
  /** How often tracked events are sent. Default: 10s. */
  flushInterval?: number;
  maxQueueSize?: number;
  /** Automatically record an exposure the first time a user is assigned to an experiment. Default: true. */
  trackExposures?: boolean;
  /** Max time `initialize()` waits for the first config download. Default: 5s. */
  initTimeout?: number;
  fetch?: typeof fetch;
  logger?: Logger;
}

export type ClientEvents = {
  ready: [];
  /** A flag changed (or was deleted, `flag === undefined`). Re-evaluate to get the new value for a user. */
  flagChanged: [key: string, flag: FlagConfig | undefined];
  experimentChanged: [key: string, experiment: ExperimentConfig | undefined];
  streamConnected: [];
  streamDisconnected: [error?: unknown];
  error: [error: unknown];
};

const MAX_EXPOSURE_CACHE = 50_000;

/**
 * FeatureFlare client. Downloads the configuration once, evaluates flags locally (no network call per
 * evaluation) and keeps the snapshot up to date through SSE, with polling as a fallback.
 *
 * Evaluation methods never throw: on any problem they return the supplied default value.
 */
export class FeatureFlareClient extends TypedEmitter<ClientEvents> {
  private readonly options: Required<Omit<FeatureFlareOptions, 'logger' | 'fetch'>>;
  private readonly logger: Logger;
  private readonly fetchImpl: typeof fetch;
  private readonly cache: ConfigCache;
  private readonly evaluator: Evaluator;
  private readonly events: EventQueue;
  private stream: SseListener | undefined;
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  private streamConnected = false;
  private initPromise: Promise<void> | undefined;
  private refreshing: Promise<void> | undefined;
  private closed = false;
  private readonly exposures = new Set<string>();

  constructor(options: FeatureFlareOptions) {
    super();
    if (!options.apiKey) throw new Error('FeatureFlare: apiKey is required');
    if (!options.baseUrl) throw new Error('FeatureFlare: baseUrl is required');

    this.options = {
      apiKey: options.apiKey,
      baseUrl: options.baseUrl.replace(/\/+$/, ''),
      enableStreaming: options.enableStreaming ?? true,
      streamTransport: options.streamTransport ?? 'auto',
      refreshInterval: options.refreshInterval ?? 30_000,
      cacheTtl: options.cacheTtl ?? 5 * 60_000,
      flushInterval: options.flushInterval ?? 10_000,
      maxQueueSize: options.maxQueueSize ?? 10_000,
      trackExposures: options.trackExposures ?? true,
      initTimeout: options.initTimeout ?? 5_000,
    };
    this.logger = options.logger ?? silentLogger;
    this.fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis);
    this.cache = new ConfigCache(this.options.cacheTtl);
    this.evaluator = new Evaluator(this.cache);
    this.events = new EventQueue({
      url: `${this.options.baseUrl}/api/events`,
      apiKey: this.options.apiKey,
      fetch: this.fetchImpl,
      logger: this.logger,
      flushIntervalMs: this.options.flushInterval,
      maxQueueSize: this.options.maxQueueSize,
    });
  }

  get isReady(): boolean {
    return this.cache.isInitialized;
  }

  /**
   * Downloads the configuration and starts streaming/polling. Resolves once flags are available or the
   * init timeout elapses (evaluations then return defaults until the first successful download).
   */
  initialize(): Promise<void> {
    this.initPromise ??= this.doInitialize();
    return this.initPromise;
  }

  // ------------------------------------------------------------------------------------------------------
  // Flags

  getBooleanValue(key: string, defaultValue: boolean, context: EvaluationContext): boolean {
    return this.getValue(key, 'boolean', defaultValue, context);
  }

  getStringValue(key: string, defaultValue: string, context: EvaluationContext): string {
    return this.getValue(key, 'string', defaultValue, context);
  }

  getNumberValue(key: string, defaultValue: number, context: EvaluationContext): number {
    return this.getValue(key, 'number', defaultValue, context);
  }

  getJsonValue<T extends JsonValue = JsonValue>(key: string, defaultValue: T, context: EvaluationContext): T {
    return this.getValue(key, 'json', defaultValue, context) as T;
  }

  getValue<K extends FlagType>(key: string, type: K, defaultValue: FlagValueMap[K], context: EvaluationContext): FlagValueMap[K] {
    return this.getDetail(key, type, defaultValue, context).value;
  }

  /** Like {@link getValue} but also explains why the value was chosen. */
  getDetail<K extends FlagType>(
    key: string,
    type: K,
    defaultValue: FlagValueMap[K],
    context: EvaluationContext,
  ): EvaluationDetail<FlagValueMap[K]> {
    try {
      const flag = this.cache.getFlag(key);
      if (flag && flag.type !== type) {
        return { value: defaultValue, variation: null, reason: 'wrongType', error: `Flag '${key}' is ${flag.type}, not ${type}` };
      }

      const detail = this.evaluator.evaluate(key, context);
      if (detail.variation === null || !matchesType(detail.value, type)) {
        return {
          value: defaultValue,
          variation: null,
          reason: detail.variation === null ? detail.reason : 'wrongType',
          error: detail.variation === null ? detail.error : `Flag '${key}' returned a non-${type} value`,
        };
      }

      return { ...detail, value: detail.value as FlagValueMap[K] };
    } catch (error) {
      this.logger.error(`Evaluation of '${key}' failed`, error);
      return { value: defaultValue, variation: null, reason: 'error', error: String(error) };
    }
  }

  /** Evaluates every flag for a context, e.g. to bootstrap a front-end. */
  getAllFlags(context: EvaluationContext): Record<string, JsonValue> {
    const result: Record<string, JsonValue> = {};
    for (const flag of this.cache.allFlags()) {
      const detail = this.evaluator.evaluateFlag(flag, context);
      if (detail.variation !== null && detail.value !== undefined) result[flag.key] = detail.value;
    }
    return result;
  }

  // ------------------------------------------------------------------------------------------------------
  // Experiments

  /** Returns the assigned variant key, or `null` when the user is not part of the experiment. */
  getExperimentVariant(experimentKey: string, context: EvaluationContext): string | null {
    return this.assign(experimentKey, context).variant;
  }

  getExperimentAssignment(experimentKey: string, context: EvaluationContext): ExperimentAssignment {
    return this.assign(experimentKey, context);
  }

  getExperimentPayload<T extends JsonValue = JsonValue>(experimentKey: string, context: EvaluationContext): T | null {
    const variant = this.assign(experimentKey, context).variant;
    if (variant === null) return null;
    const payload = this.cache.getExperiment(experimentKey)?.variants.find((v) => v.key === variant)?.payload;
    return (payload ?? null) as T | null;
  }

  trackConversion(
    experimentKey: string,
    options: { userId: string; value?: number; metric?: string },
  ): void {
    const assignment = this.evaluator.assign(experimentKey, options.userId);
    this.events.push({
      kind: 'conversion',
      experimentKey,
      variant: assignment.variant,
      userId: options.userId,
      metric: options.metric ?? null,
      value: options.value ?? null,
      timestamp: new Date().toISOString(),
    });
  }

  /** Sends buffered events now. */
  flush(): Promise<void> {
    return this.events.flush();
  }

  /** Stops streaming/polling and flushes pending events. */
  async close(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    this.stream?.stop();
    if (this.pollTimer) clearInterval(this.pollTimer);
    await this.events.stop();
    this.removeAllListeners();
  }

  // ------------------------------------------------------------------------------------------------------
  // Internals

  private async doInitialize(): Promise<void> {
    this.events.start();
    this.startPolling();
    if (this.options.enableStreaming) this.startStreaming();

    const timeout = new Promise<void>((resolve) => {
      const t = setTimeout(() => {
        if (!this.isReady) this.logger.warn(`Not initialized after ${this.options.initTimeout}ms; serving defaults`);
        resolve();
      }, this.options.initTimeout);
      unref(t);
    });

    await Promise.race([this.refresh(), timeout]);
    if (this.isReady) this.emit('ready');
  }

  private assign(experimentKey: string, context: EvaluationContext): ExperimentAssignment {
    try {
      const assignment = this.evaluator.assign(experimentKey, context.userId);
      if (assignment.variant !== null && this.options.trackExposures) this.recordExposure(assignment, context.userId);
      return assignment;
    } catch (error) {
      this.logger.error(`Assignment for '${experimentKey}' failed`, error);
      return { experimentKey, variant: null, reason: 'experimentNotFound' };
    }
  }

  private recordExposure(assignment: ExperimentAssignment, userId: string): void {
    const dedupeKey = `${assignment.experimentKey}\u0000${assignment.variant}\u0000${userId}`;
    if (this.exposures.has(dedupeKey)) return;
    if (this.exposures.size >= MAX_EXPOSURE_CACHE) this.exposures.clear();
    this.exposures.add(dedupeKey);

    this.events.push({
      kind: 'exposure',
      experimentKey: assignment.experimentKey,
      variant: assignment.variant!,
      userId,
      timestamp: new Date().toISOString(),
    });
  }

  /** Full config download. Concurrent callers share one request. */
  private refresh(): Promise<void> {
    this.refreshing ??= this.download().finally(() => (this.refreshing = undefined));
    return this.refreshing;
  }

  private async download(): Promise<void> {
    try {
      const response = await this.fetchImpl(`${this.options.baseUrl}/api/sdk/config`, {
        headers: { Accept: 'application/json', Authorization: `Bearer ${this.options.apiKey}` },
      });
      if (!response.ok) throw new Error(`Config request failed with HTTP ${response.status}`);

      const config = (await response.json()) as SdkConfig;
      const wasReady = this.isReady;
      const changed = this.cache.replaceAll(config);
      if (wasReady) {
        for (const key of changed) this.emit('flagChanged', key, this.cache.getFlag(key));
      }
    } catch (error) {
      this.logger.warn('Failed to download configuration', error);
      this.emit('error', error);
    }
  }

  private startPolling(): void {
    this.pollTimer = setInterval(() => {
      // While the stream is healthy it delivers every change; polling only covers outages and staleness.
      if (!this.streamConnected || this.cache.isStale()) void this.refresh();
    }, this.options.refreshInterval);
    unref(this.pollTimer);
  }

  private startStreaming(): void {
    this.stream = new SseListener({
      url: `${this.options.baseUrl}/api/stream`,
      apiKey: this.options.apiKey,
      transport: this.options.streamTransport,
      fetch: this.fetchImpl,
      logger: this.logger,
      onMessage: (message) => this.handleStreamMessage(message),
      onOpen: () => {
        this.streamConnected = true;
        this.emit('streamConnected');
        // Changes may have been missed while disconnected: re-sync.
        if (this.isReady) void this.refresh();
      },
      onDisconnect: (error) => {
        this.streamConnected = false;
        this.emit('streamDisconnected', error);
      },
    });
    this.stream.start();
  }

  private handleStreamMessage(message: SseMessage): void {
    let event: StreamEvent;
    try {
      event = { type: message.event, data: JSON.parse(message.data) } as StreamEvent;
    } catch (error) {
      this.logger.warn(`Ignoring malformed stream message '${message.event}'`, error);
      return;
    }
    this.apply(event);
  }

  /** Applies a stream event to the cache and emits change notifications. */
  apply(event: StreamEvent): void {
    switch (event.type) {
      case 'flag.updated':
        if (this.cache.upsertFlag(event.data)) this.emit('flagChanged', event.data.key, event.data);
        break;
      case 'flag.deleted':
        if (this.cache.deleteFlag(event.data)) this.emit('flagChanged', event.data.key, undefined);
        break;
      case 'segment.updated':
        if (this.cache.upsertSegment(event.data)) this.notifySegment(event.data);
        break;
      case 'segment.deleted':
        this.deleteSegment(event.data);
        break;
      case 'experiment.updated':
        if (this.cache.upsertExperiment(event.data)) this.emit('experimentChanged', event.data.key, event.data);
        break;
      case 'experiment.deleted':
        if (this.cache.deleteExperiment(event.data)) this.emit('experimentChanged', event.data.key, undefined);
        break;
      default:
        this.logger.debug(`Ignoring unknown stream event '${(event as { type: string }).type}'`);
    }
  }

  private notifySegment(segment: SegmentConfig): void {
    for (const key of this.cache.flagsUsingSegment(segment.key)) this.emit('flagChanged', key, this.cache.getFlag(key));
  }

  private deleteSegment(deletion: Deletion): void {
    const affected = this.cache.flagsUsingSegment(deletion.key);
    if (this.cache.deleteSegment(deletion)) {
      for (const key of affected) this.emit('flagChanged', key, this.cache.getFlag(key));
    }
  }
}

function matchesType(value: JsonValue | undefined, type: FlagType): boolean {
  switch (type) {
    case 'boolean':
      return typeof value === 'boolean';
    case 'string':
      return typeof value === 'string';
    case 'number':
      return typeof value === 'number';
    case 'json':
      return value !== undefined;
  }
}
