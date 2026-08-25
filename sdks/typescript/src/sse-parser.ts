export interface SseMessage {
  event: string;
  data: string;
  id?: string;
}

/**
 * Incremental parser for the `text/event-stream` format (WHATWG HTML §9.2). Chunks may split lines or
 * even CRLF pairs arbitrarily; complete messages are returned as soon as their terminating blank line
 * arrives.
 */
export class SseParser {
  private buffer = '';
  private event = '';
  private data: string[] = [];
  private id: string | undefined;

  /** Last `retry:` value received, in milliseconds. */
  retry: number | undefined;

  /** Last `id:` value received, to be sent as `Last-Event-ID` on reconnect. */
  lastEventId: string | undefined;

  push(chunk: string): SseMessage[] {
    this.buffer += chunk;
    const messages: SseMessage[] = [];

    let newline: number;
    while ((newline = this.findLineEnd()) >= 0) {
      const line = this.buffer.slice(0, newline);
      const skip = this.buffer[newline] === '\r' && this.buffer[newline + 1] === '\n' ? 2 : 1;
      this.buffer = this.buffer.slice(newline + skip);

      const message = this.processLine(line);
      if (message) messages.push(message);
    }

    return messages;
  }

  reset(): void {
    this.buffer = '';
    this.event = '';
    this.data = [];
    this.id = undefined;
  }

  private findLineEnd(): number {
    for (let i = 0; i < this.buffer.length; i++) {
      const ch = this.buffer[i];
      if (ch === '\n') return i;
      if (ch === '\r') {
        // A trailing CR may be the first half of CRLF: wait for the next chunk.
        return i === this.buffer.length - 1 ? -1 : i;
      }
    }
    return -1;
  }

  private processLine(line: string): SseMessage | null {
    if (line === '') return this.dispatch();
    if (line.startsWith(':')) return null; // comment / heartbeat

    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);

    switch (field) {
      case 'event':
        this.event = value;
        break;
      case 'data':
        this.data.push(value);
        break;
      case 'id':
        if (!value.includes('\0')) this.id = value;
        break;
      case 'retry':
        if (/^\d+$/.test(value)) this.retry = Number.parseInt(value, 10);
        break;
      default:
        break;
    }
    return null;
  }

  private dispatch(): SseMessage | null {
    if (this.id !== undefined) this.lastEventId = this.id;

    if (this.data.length === 0) {
      this.event = '';
      return null;
    }

    const message: SseMessage = { event: this.event || 'message', data: this.data.join('\n') };
    if (this.id !== undefined) message.id = this.id;

    this.event = '';
    this.data = [];
    this.id = undefined;
    return message;
  }
}
