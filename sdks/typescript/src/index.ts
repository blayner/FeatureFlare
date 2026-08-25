export { FeatureFlareClient } from './client.js';
export type { ClientEvents, FeatureFlareOptions } from './client.js';
export { Evaluator } from './evaluator.js';
export type { ConfigSource } from './evaluator.js';
export { ConfigCache } from './cache.js';
export { murmur3 } from './murmur3.js';
export { bucket, pick, BUCKET_SCALE } from './bucketing.js';
export { SseParser } from './sse-parser.js';
export type { SseMessage } from './sse-parser.js';
export type { StreamTransport } from './sse-listener.js';
export { consoleLogger, silentLogger } from './logger.js';
export type { Logger } from './logger.js';
export * from './types.js';

export const SDK_VERSION = '0.1.0';
