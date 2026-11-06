# Build context: repository root.
FROM node:26-alpine AS sdk
WORKDIR /sdk
COPY sdks/typescript/package.json sdks/typescript/package-lock.json ./
RUN npm ci
COPY sdks/typescript/ ./
RUN npm run build && npm pack --pack-destination /tmp

FROM node:26-alpine
ENV NODE_ENV=production
WORKDIR /app
COPY examples/node-http/package.json ./
COPY --from=sdk /tmp/featureflare-sdk-*.tgz /tmp/
RUN npm install --omit=dev --no-audit --no-fund /tmp/featureflare-sdk-*.tgz && rm /tmp/featureflare-sdk-*.tgz
COPY examples/node-http/server.mjs ./
USER node
EXPOSE 3000
CMD ["node", "server.mjs"]
