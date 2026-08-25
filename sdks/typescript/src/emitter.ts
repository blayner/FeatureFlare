export type Listener<Args extends unknown[]> = (...args: Args) => void;

type ListenerMap<Events extends { [K in keyof Events]: unknown[] }> = {
  [K in keyof Events]?: Set<Listener<Events[K]>>;
};

/** Small, dependency-free typed event emitter that works in Node and browsers. */
export class TypedEmitter<Events extends { [K in keyof Events]: unknown[] }> {
  private listeners: ListenerMap<Events> = {};

  /** Subscribes and returns an unsubscribe function. */
  on<K extends keyof Events>(event: K, listener: Listener<Events[K]>): () => void {
    const set = (this.listeners[event] ??= new Set<Listener<Events[K]>>());
    set.add(listener);
    return () => this.off(event, listener);
  }

  once<K extends keyof Events>(event: K, listener: Listener<Events[K]>): () => void {
    const wrapper: Listener<Events[K]> = (...args) => {
      off();
      listener(...args);
    };
    const off = this.on(event, wrapper);
    return off;
  }

  off<K extends keyof Events>(event: K, listener: Listener<Events[K]>): void {
    this.listeners[event]?.delete(listener);
  }

  protected emit<K extends keyof Events>(event: K, ...args: Events[K]): void {
    const set = this.listeners[event];
    if (!set) return;
    for (const listener of [...set]) {
      try {
        listener(...args);
      } catch {
        // A faulty listener must never break flag updates for the rest of the application.
      }
    }
  }

  removeAllListeners(): void {
    this.listeners = {};
  }
}
