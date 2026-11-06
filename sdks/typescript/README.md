# @featureflare/sdk

TypeScript SDK for FeatureFlare. It evaluates flags **locally** (no network call per evaluation), receives
changes over **SSE**, and batches experiment exposures and conversions.

- Node.js ≥ 22 (CI runs 22, 24 and 26) and modern browsers. Other runtimes with standard etch/streams should work but are not tested.
- No runtime dependencies; uses the platform's `fetch`, `TextDecoderStream` and, when available, `EventSource`.
- Strict types: flag values are typed from the flag type, and stream events are a discriminated union.

## Install

```bash
npm install @featureflare/sdk
```

## Usage

```ts
import { FeatureFlareClient, consoleLogger } from '@featureflare/sdk';

const client = new FeatureFlareClient({
  apiKey: process.env.FEATUREFLARE_SDK_KEY!, // SDK key (read-only), never an admin key
  baseUrl: 'https://flags.example.com',
  logger: consoleLogger,
});

await client.initialize(); // resolves after the first config download or `initTimeout` (5s)

const context = { userId: 'u-123', attributes: { plan: 'pro', country: 'BR', appVersion: '2.4.1' } };

client.getBooleanValue('new-checkout', false, context);
client.getStringValue('banner-text', 'Welcome', context);
client.getNumberValue('max-cart-items', 20, context);
client.getJsonValue('pricing-config', { currency: 'BRL' }, context);

// Why did this user get this value?
client.getDetail('new-checkout', 'boolean', false, context);
// → { value: true, variation: 'on', reason: 'ruleMatch', ruleId: 'pro-users' }
```

Evaluation methods **never throw**. Unknown flags, type mismatches or a missing configuration return the
default value, with the reason available through `getDetail`.

### Experiments

```ts
const variant = client.getExperimentVariant('checkout-flow', context); // 'control' | 'one-page' | null
const payload = client.getExperimentPayload<{ steps: number }>('checkout-flow', context);

// later, when the user converts
client.trackConversion('checkout-flow', { userId: 'u-123', value: 149.9, metric: 'purchase' });
```

`getExperimentVariant` records an **exposure** the first time a user sees a variant (disable this with
`trackExposures: false`). Events are queued in memory and sent in batches every `flushInterval`. The queue
is bounded, and the oldest events are dropped first.

### Reacting to changes

```ts
const off = client.on('flagChanged', (key, flag) => {
  console.log(`${key} changed`, flag?.version);
});
client.on('streamDisconnected', () => console.warn('falling back to polling'));
off(); // unsubscribe
```

### Shutdown

```ts
await client.close(); // stops streaming/polling and flushes pending events
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `apiKey` | required | SDK key |
| `baseUrl` | required | FeatureFlare server URL |
| `enableStreaming` | `true` | Receive changes over SSE |
| `streamTransport` | `'auto'` | `'eventsource'` (native, key sent as `?sdkKey=`), `'fetch'` (key in `Authorization` header) or `'auto'` (native when available) |
| `refreshInterval` | `30000` | Polling interval while the stream is disconnected or disabled (ms) |
| `cacheTtl` | `300000` | Max snapshot age before a refresh is forced, even while streaming (ms) |
| `flushInterval` | `10000` | Event batch interval (ms) |
| `maxQueueSize` | `10000` | Max buffered events |
| `trackExposures` | `true` | Record exposures automatically |
| `initTimeout` | `5000` | Max wait in `initialize()` (ms) |
| `fetch` | `globalThis.fetch` | Custom fetch (proxies, tests) |
| `logger` | silent | `consoleLogger` or any `{ debug, info, warn, error }` object |

## How it stays consistent

- Every configuration object carries a `version`. Updates older than the cached version are ignored, so a
  late poll response can't undo a streamed change.
- On every stream (re)connection the SDK downloads the full configuration, so changes missed while
  disconnected are recovered.
- Hashing and evaluation follow [`spec/README.md`](../../spec/README.md) and are tested against the
  vectors shared with the server and the .NET SDK, so a user gets the same variation everywhere.

## Development

```bash
npm ci
npm run typecheck
npm test
npm run build
```
