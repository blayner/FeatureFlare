import { describe, expect, it } from 'vitest';
import { SseParser } from '../src/sse-parser.js';

describe('SseParser', () => {
  it('parses events split across arbitrary chunks, including CRLF boundaries', () => {
    const parser = new SseParser();
    const stream = 'retry: 3000\r\n: ping\r\n\r\nevent: flag.updated\r\nid: 7\r\ndata: {"a":\r\ndata: 1}\r\n\r\n';

    const messages = [];
    for (const ch of stream) messages.push(...parser.push(ch));

    expect(messages).toEqual([{ event: 'flag.updated', data: '{"a":\n1}', id: '7' }]);
    expect(parser.retry).toBe(3000);
    expect(parser.lastEventId).toBe('7');
  });

  it('defaults the event name to "message" and ignores blocks without data', () => {
    const parser = new SseParser();
    expect(parser.push('event: noop\n\ndata: hello\n\n')).toEqual([{ event: 'message', data: 'hello' }]);
  });

  it('ignores unknown fields and invalid retry values', () => {
    const parser = new SseParser();
    parser.push('foo: bar\nretry: soon\ndata: x\n\n');
    expect(parser.retry).toBeUndefined();
  });
});
