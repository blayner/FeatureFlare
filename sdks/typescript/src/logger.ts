export interface Logger {
  debug(message: string, ...args: unknown[]): void;
  info(message: string, ...args: unknown[]): void;
  warn(message: string, ...args: unknown[]): void;
  error(message: string, ...args: unknown[]): void;
}

const noop = (): void => {};

export const silentLogger: Logger = { debug: noop, info: noop, warn: noop, error: noop };

export const consoleLogger: Logger = {
  debug: (m, ...a) => console.debug(`[featureflare] ${m}`, ...a),
  info: (m, ...a) => console.info(`[featureflare] ${m}`, ...a),
  warn: (m, ...a) => console.warn(`[featureflare] ${m}`, ...a),
  error: (m, ...a) => console.error(`[featureflare] ${m}`, ...a),
};
