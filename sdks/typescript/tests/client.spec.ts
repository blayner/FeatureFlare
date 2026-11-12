import { afterEach, describe, expect, it } from 'vitest';
import { FeatureFlareClient } from '../src/client.js';
import type { FlagConfig } from '../src/types.js';
import { sampleConfig } from './fixtures.js';

interface Recorded {
  url: string;
  method: string;
  body?: string;
  headers: Record<string, string>;
}

interface FakeServerOptions {
  /** SSE payload written as a single chunk once `streamGate` resolves. */
  stream?: string;
  /** The config response is held until this resolves. */
  configGate?: Promise<void>;
  streamGate?: Promise<void>;
}

function fakeFetch(options: FakeServerOptions = {}) {
  const calls: Recorded[] = [];
  const impl: typeof fetch = async (input, init) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    const headers = (init?.headers ?? {}) as Record<string, string>;
    calls.push({ url, method: init?.method ?? 'GET', body: init?.body as string | undefined, headers });

    if (url.endsWith('/api/sdk/config')) {
      await options.configGate;
      return Response.json(sampleConfig());
    }
    if (url.endsWith('/api/events')) return new Response(null, { status: 202 });
    if (url.endsWith('/api/stream') && options.stream !== undefined) {
      const payload = options.stream;
      const body = new ReadableStream<Uint8Array>({
        async start(controller) {
          await options.streamGate;
          // All events in one chunk, as proxies and HTTP/2 frames often coalesce them. The connection is
          // left open, like a real server would.
          controller.enqueue(new TextEncoder().encode(payload));
        },
      });
      return new Response(body, { headers: { 'Content-Type': 'text/event-stream' } });
    }
    return new Response(null, { status: 404 });
  };
  return { impl, calls };
}

function streamedChanges(): string {
  const updated: FlagConfig = { ...sampleConfig().flags[0]!, enabled: false, version: 4 };
  const stale: FlagConfig = { ...sampleConfig().flags[0]!, enabled: true, version: 2 };
  return (
    `event: flag.updated\ndata: ${JSON.stringify(updated)}\n\n` +
    `event: flag.updated\ndata: ${JSON.stringify(stale)}\n\n` +
    `event: flag.deleted\ndata: {"key":"max-items","version":2}\n\n`
  );
}

const clients: FeatureFlareClient[] = [];

function createClient(fetchImpl: typeof fetch, streaming = false): FeatureFlareClient {
  const client = new FeatureFlareClient({
    apiKey: 'sdk-test',
    baseUrl: 'http://featureflare.test/',
    enableStreaming: streaming,
    streamTransport: 'fetch',
    fetch: fetchImpl,
  });
  clients.push(client);
  return client;
}

afterEach(async () => {
  await Promise.all(clients.splice(0).map((c) => c.close()));
});

describe('FeatureFlareClient', () => {
  it('downloads the config once and evaluates locally', async () => {
    const { impl, calls } = fakeFetch();
    const client = createClient(impl);

    await client.initialize();

    expect(client.isReady).toBe(true);
    expect(client.getBooleanValue('new-ui', false, { userId: 'ana' })).toBe(true);
    expect(client.getNumberValue('max-items', 0, { userId: 'u1' })).toBe(10);
    expect(calls.filter((c) => c.url.endsWith('/api/sdk/config'))).toHaveLength(1);
    expect(calls[0]!.headers.Authorization).toBe('Bearer sdk-test');
  });

  it('returns defaults with an explanation for wrong types and unknown flags', async () => {
    const client = createClient(fakeFetch().impl);
    await client.initialize();

    expect(client.getStringValue('new-ui', 'fallback', { userId: 'ana' })).toBe('fallback');
    expect(client.getDetail('new-ui', 'string', 'fallback', { userId: 'ana' })).toMatchObject({ reason: 'wrongType' });
    expect(client.getDetail('ghost', 'boolean', true, { userId: 'ana' })).toMatchObject({ reason: 'flagNotFound', value: true });
  });

  it('deduplicates exposures and sends conversions with the assigned variant', async () => {
    const { impl, calls } = fakeFetch();
    const client = createClient(impl);
    await client.initialize();

    const variant = client.getExperimentVariant('checkout', { userId: 'user-7' });
    client.getExperimentVariant('checkout', { userId: 'user-7' });
    client.trackConversion('checkout', { userId: 'user-7', value: 59.9 });
    await client.flush();

    const posts = calls.filter((c) => c.method === 'POST');
    expect(posts).toHaveLength(1);
    const { events } = JSON.parse(posts[0]!.body!) as { events: { kind: string; variant: string }[] };
    expect(events.map((e) => e.kind)).toEqual(['exposure', 'conversion']);
    expect(events.every((e) => e.variant === variant)).toBe(true);
  });

  it('applies streamed updates after initialization, ignores stale versions and emits flagChanged', async () => {
    let releaseStream!: () => void;
    const streamGate = new Promise<void>((resolve) => (releaseStream = resolve));
    const client = createClient(fakeFetch({ stream: streamedChanges(), streamGate }).impl, true);
    const changed: string[] = [];
    client.on('flagChanged', (key) => changed.push(key));

    await client.initialize();
    releaseStream();
    await expect.poll(() => changed.length).toBe(2);

    expect(client.getBooleanValue('new-ui', true, { userId: 'ana' })).toBe(false);
    expect(client.getNumberValue('max-items', -1, { userId: 'u1' })).toBe(-1);
    expect(changed).toEqual(['new-ui', 'max-items']);
  });

  it('does not let the initial snapshot roll back stream events that arrived before it', async () => {
    // Regression: the stream body can be read before the config response is parsed (it happened
    // consistently on Node 22). The older snapshot used to overwrite the streamed v4 update and bring
    // back the flag deleted on the stream.
    let releaseConfig!: () => void;
    const configGate = new Promise<void>((resolve) => (releaseConfig = resolve));
    const client = createClient(fakeFetch({ stream: streamedChanges(), configGate }).impl, true);
    client.on('flagChanged', (key) => {
      // The whole chunk is applied synchronously, so the snapshot resolves only after all three events.
      if (key === 'new-ui') releaseConfig();
    });

    await client.initialize();

    expect(client.isReady).toBe(true);
    expect(client.getBooleanValue('new-ui', true, { userId: 'ana' })).toBe(false);
    expect(client.getNumberValue('max-items', -1, { userId: 'u1' })).toBe(-1);
  });

  it('keeps serving defaults when the server is unreachable', async () => {
    const failing: typeof fetch = async () => {
      throw new Error('ECONNREFUSED');
    };
    const client = createClient(failing);
    const errors: unknown[] = [];
    client.on('error', (e) => errors.push(e));

    await client.initialize();

    expect(client.isReady).toBe(false);
    expect(client.getBooleanValue('new-ui', true, { userId: 'ana' })).toBe(true);
    expect(errors).toHaveLength(1);
  });
});
