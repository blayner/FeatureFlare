# Examples

Start the server first (`dotnet run --project server/src/FeatureFlare.Api`, which listens on port 5000
with the `dev-admin-key` / `dev-sdk-key` keys), then create a couple of flags:

```bash
curl -X POST http://localhost:5000/api/flags -H "X-Api-Key: dev-admin-key" -H "Content-Type: application/json" -d '{
  "key": "new-checkout", "name": "New checkout", "type": "boolean", "enabled": true,
  "variations": [{ "key": "on", "value": true }, { "key": "off", "value": false }],
  "offVariation": "off", "fallthrough": { "variation": "off" },
  "rules": [{ "id": "enterprise", "serve": { "variation": "on" },
              "clauses": [{ "attribute": "plan", "operator": "in", "values": ["enterprise"] }] }]
}'

curl -X POST http://localhost:5000/api/experiments -H "X-Api-Key: dev-admin-key" -H "Content-Type: application/json" -d '{
  "key": "checkout-flow", "name": "Checkout flow", "primaryMetric": "purchase", "controlVariant": "control",
  "variants": [{ "key": "control", "weight": 5000 }, { "key": "one-page", "weight": 5000 }]
}'
curl -X POST http://localhost:5000/api/experiments/checkout-flow/start -H "X-Api-Key: dev-admin-key"
```

| Example | Run |
|---------|-----|
| [`node-http`](node-http) | `cd sdks/typescript && npm ci && npm run build && cd ../../examples/node-http && npm install ../../sdks/typescript && node server.mjs` |
| [`dotnet-minimal-api`](dotnet-minimal-api) | `dotnet run --project examples/dotnet-minimal-api` |

Against the `docker compose` stack instead, swap the keys (`local-admin-key` for the curl calls) and
pass the SDK key to the examples: `FEATUREFLARE_SDK_KEY=local-sdk-key node server.mjs` or
`FeatureFlare__ApiKey=local-sdk-key dotnet run --project examples/dotnet-minimal-api`.

Then toggle a flag and watch the example log the change, delivered over SSE:

```bash
curl -X PATCH http://localhost:5000/api/flags/new-checkout/toggle -H "X-Api-Key: dev-admin-key" \
  -H "Content-Type: application/json" -d '{ "enabled": false }'
```

The Node example also ships a Dockerfile (`node:26-alpine`). Build it from the repository root:

```bash
docker build -f examples/node-http/Dockerfile -t featureflare-example-node .
```
