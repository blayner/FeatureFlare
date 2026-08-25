import { SseParser, type SseMessage } from './sse-parser.js';
import type { Logger } from './logger.js';
import { STREAM_EVENT_TYPES } from './types.js';

export type StreamTransport = 'auto' | 'eventsource' | 'fetch';

export interface SseListenerOptions {
  url: string;
  apiKey: string;
  transport: StreamTransport;
  fetch: typeof fetch;
  logger: Logger;
  onMessage: (message: SseMessage) => void;
  /** Called on every successful (re)connection. The client re-syncs the full config here. */
  onOpen: () => void;
  onDisconnect: (error?: unknown) => void;
  initialDelayMs?: number;
  maxDelayMs?: number;
}

/** Minimal structural type for the WHATWG EventSource, so we don't depend on DOM typings. */
interface EventSourceLike {
  readonly readyState: number;
  onopen: ((ev: unknown) => void) | null;
  onerror: ((ev: unknown) => void) | null;
  addEventListener(type: string, listener: (ev: { data: string; lastEventId?: string }) => void): void;
  close(): void;
}

type EventSourceCtor = new (url: string) => EventSourceLike;

const EVENT_SOURCE_CLOSED = 2;

function nativeEventSource(): EventSourceCtor | undefined {
  const candidate = (globalThis as { EventSource?: unknown }).EventSource;
  return typeof candidate === 'function' ? (candidate as EventSourceCtor) : undefined;
}

/**
 * Keeps a server-sent events connection open and reconnects with exponential backoff and full jitter.
 *
 * Transports:
 * - `eventsource`: the platform's native `EventSource` (browsers, Deno, Bun, Node with EventSource
 *   enabled). It cannot send headers, so the SDK key travels in the query string — acceptable for SDK
 *   keys, which are read-only, but keep that in mind if your proxies log URLs.
 * - `fetch`: streams the response body via `fetch`, sending the key in the `Authorization` header.
 * - `auto` (default): `eventsource` when available, otherwise `fetch`.
 */
export class SseListener {
  private readonly initialDelay: number;
  private readonly maxDelay: number;
  private attempt = 0;
  private stopped = true;
  private abort: AbortController | undefined;
  private source: EventSourceLike | undefined;
  private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  private serverRetry: number | undefined;

  constructor(private readonly options: SseListenerOptions) {
    this.initialDelay = options.initialDelayMs ?? 1_000;
    this.maxDelay = options.maxDelayMs ?? 30_000;
  }

  get usesNativeEventSource(): boolean {
    return this.options.transport === 'eventsource' || (this.options.transport === 'auto' && !!nativeEventSource());
  }

  start(): void {
    if (!this.stopped) return;
    this.stopped = false;
    this.connect();
  }

  stop(): void {
    this.stopped = true;
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer);
    this.reconnectTimer = undefined;
    this.abort?.abort();
    this.abort = undefined;
    this.source?.close();
    this.source = undefined;
  }

  private connect(): void {
    if (this.stopped) return;
    if (this.usesNativeEventSource) {
      this.connectEventSource();
    } else {
      void this.connectFetch();
    }
  }

  private connectEventSource(): void {
    const EventSourceImpl = nativeEventSource();
    if (!EventSourceImpl) {
      this.options.logger.warn('EventSource is not available in this runtime; falling back to fetch streaming');
      void this.connectFetch();
      return;
    }

    const url = new URL(this.options.url);
    url.searchParams.set('sdkKey', this.options.apiKey);
    const source = new EventSourceImpl(url.toString());
    this.source = source;

    source.onopen = () => {
      this.attempt = 0;
      this.options.onOpen();
    };

    source.onerror = (error) => {
      // The native implementation retries by itself while readyState is CONNECTING. Once it gives up
      // (CLOSED, e.g. after a 401/5xx) we take over with our own backoff.
      this.options.onDisconnect(error);
      if (source.readyState === EVENT_SOURCE_CLOSED) {
        source.close();
        this.scheduleReconnect();
      }
    };

    for (const type of STREAM_EVENT_TYPES) {
      source.addEventListener(type, (ev) => this.options.onMessage({ event: type, data: ev.data }));
    }
  }

  private async connectFetch(): Promise<void> {
    const abort = new AbortController();
    this.abort = abort;
    const parser = new SseParser();

    try {
      const response = await this.options.fetch(this.options.url, {
        headers: {
          Accept: 'text/event-stream',
          Authorization: `Bearer ${this.options.apiKey}`,
          'Cache-Control': 'no-cache',
        },
        signal: abort.signal,
      });

      if (!response.ok || !response.body) {
        throw new Error(`Stream request failed with HTTP ${response.status}`);
      }

      this.attempt = 0;
      this.options.onOpen();

      const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        for (const message of parser.push(value)) this.options.onMessage(message);
        if (parser.retry !== undefined) this.serverRetry = parser.retry;
      }

      this.options.onDisconnect();
    } catch (error) {
      if (abort.signal.aborted) return;
      this.options.logger.warn('Stream connection failed', error);
      this.options.onDisconnect(error);
    }

    this.scheduleReconnect();
  }

  private scheduleReconnect(): void {
    if (this.stopped) return;
    const delay = this.nextDelay();
    this.options.logger.debug(`Reconnecting stream in ${delay}ms`);
    this.reconnectTimer = setTimeout(() => this.connect(), delay);
    unref(this.reconnectTimer);
  }

  /** Exponential backoff with full jitter, floored by the server's `retry:` hint. */
  private nextDelay(): number {
    const base = Math.max(this.initialDelay, this.serverRetry ?? 0);
    const cap = Math.min(this.maxDelay, base * 2 ** this.attempt);
    this.attempt = Math.min(this.attempt + 1, 16);
    return Math.round(base / 2 + Math.random() * Math.max(0, cap - base / 2));
  }
}

/** Don't keep a Node.js process alive just for SDK timers. */
export function unref(timer: unknown): void {
  if (typeof timer === 'object' && timer !== null && 'unref' in timer) {
    const fn = (timer as { unref?: () => void }).unref;
    if (typeof fn === 'function') fn.call(timer);
  }
}
