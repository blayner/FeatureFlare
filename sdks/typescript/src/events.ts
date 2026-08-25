import type { Logger } from './logger.js';
import { unref } from './sse-listener.js';
import type { TrackEvent } from './types.js';

export interface EventQueueOptions {
  url: string;
  apiKey: string;
  fetch: typeof fetch;
  logger: Logger;
  flushIntervalMs: number;
  maxQueueSize: number;
  batchSize?: number;
}

/**
 * Buffers exposure/conversion events and ships them in batches. Tracking must never slow down or break
 * the host application: pushes are synchronous, the queue is bounded (oldest events are dropped first)
 * and failed batches are re-queued while there is room.
 */
export class EventQueue {
  private queue: TrackEvent[] = [];
  private timer: ReturnType<typeof setInterval> | undefined;
  private flushing: Promise<void> | undefined;
  private dropped = 0;

  constructor(private readonly options: EventQueueOptions) {}

  get size(): number {
    return this.queue.length;
  }

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => void this.flush(), this.options.flushIntervalMs);
    unref(this.timer);
  }

  async stop(): Promise<void> {
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
    await this.flush();
  }

  push(event: TrackEvent): void {
    this.queue.push(event);
    if (this.queue.length > this.options.maxQueueSize) {
      this.queue.shift();
      this.dropped++;
    }
    if (this.queue.length >= (this.options.batchSize ?? 100)) void this.flush();
  }

  /** Concurrent callers share the in-flight flush. */
  flush(): Promise<void> {
    this.flushing ??= this.drain().finally(() => (this.flushing = undefined));
    return this.flushing;
  }

  private async drain(): Promise<void> {
    const batchSize = this.options.batchSize ?? 100;
    while (this.queue.length > 0) {
      const batch = this.queue.splice(0, batchSize);
      try {
        const response = await this.options.fetch(this.options.url, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.options.apiKey}` },
          body: JSON.stringify({ events: batch }),
        });
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
      } catch (error) {
        this.options.logger.warn(`Failed to send ${batch.length} events; will retry`, error);
        const room = this.options.maxQueueSize - this.queue.length;
        this.queue.unshift(...batch.slice(Math.max(0, batch.length - room)));
        return;
      }
    }

    if (this.dropped > 0) {
      this.options.logger.warn(`${this.dropped} events were dropped because the queue was full`);
      this.dropped = 0;
    }
  }
}
