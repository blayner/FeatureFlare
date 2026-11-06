// Minimal Node.js service using the FeatureFlare TypeScript SDK.
//
//   cd sdks/typescript && npm ci && npm run build
//   cd ../../examples/node-http && npm install ../../sdks/typescript
//   FEATUREFLARE_SDK_KEY=dev-sdk-key node server.mjs
//
// Then: curl "http://localhost:3000/checkout?user=u-1&plan=enterprise"

import { createServer } from 'node:http';
import { FeatureFlareClient, consoleLogger } from '@featureflare/sdk';

const flags = new FeatureFlareClient({
  apiKey: process.env.FEATUREFLARE_SDK_KEY ?? 'dev-sdk-key',
  baseUrl: process.env.FEATUREFLARE_URL ?? 'http://localhost:5000',
  logger: consoleLogger,
});

flags.on('flagChanged', (key, flag) => console.log(`[flags] ${key} changed (enabled: ${flag?.enabled ?? 'deleted'})`));
flags.on('streamDisconnected', () => console.warn('[flags] stream disconnected, polling until it reconnects'));

// Never block start-up on the flag service: initialize() resolves after `initTimeout` at the latest,
// and evaluations fall back to defaults until the first config arrives.
await flags.initialize();

const server = createServer((req, res) => {
  const url = new URL(req.url ?? '/', 'http://localhost');
  const context = {
    userId: url.searchParams.get('user') ?? 'anonymous',
    attributes: { plan: url.searchParams.get('plan') ?? 'free', country: url.searchParams.get('country') ?? undefined },
  };

  if (url.pathname === '/checkout') {
    const newCheckout = flags.getBooleanValue('new-checkout', false, context);
    const variant = flags.getExperimentVariant('checkout-flow', context); // records an exposure
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ newCheckout, experimentVariant: variant }));
    return;
  }

  if (url.pathname === '/purchase' && req.method === 'POST') {
    flags.trackConversion('checkout-flow', { userId: context.userId, value: Number(url.searchParams.get('total') ?? 0) });
    res.writeHead(204).end();
    return;
  }

  if (url.pathname === '/flags') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify(flags.getAllFlags(context)));
    return;
  }

  res.writeHead(404).end();
});

server.listen(Number(process.env.PORT ?? 3000), () => console.log(`listening on :${server.address().port}`));

// Flush queued analytics events before exiting.
for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    server.close();
    await flags.close();
    process.exit(0);
  });
}
