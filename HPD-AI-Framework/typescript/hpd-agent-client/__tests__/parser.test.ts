import { describe, it, expect } from 'vitest';
import { SseParser } from '../src/parser.js';

describe('SseParser', () => {
  it('should parse a single complete event', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n'
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({
      version: '1.0',
      type: 'TEXT_DELTA',
      text: 'Hello',
      messageId: 'msg-1',
    });
  });

  it('should parse multiple events in one chunk', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n' +
        'data: {"version":"1.0","type":"TEXT_DELTA","text":" World","messageId":"msg-1"}\n\n'
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(2);
    expect((events[0] as any).text).toBe('Hello');
    expect((events[1] as any).text).toBe(' World');
  });

  it('should handle events split across chunks', () => {
    const parser = new SseParser();

    // First chunk - incomplete
    const chunk1 = new TextEncoder().encode('data: {"version":"1.0","type":"TEXT_');
    const events1 = parser.processChunk(chunk1);
    expect(events1).toHaveLength(0);

    // Second chunk - completes the event
    const chunk2 = new TextEncoder().encode('DELTA","text":"Hello","messageId":"msg-1"}\n\n');
    const events2 = parser.processChunk(chunk2);
    expect(events2).toHaveLength(1);
    expect((events2[0] as any).text).toBe('Hello');
  });

  it('should handle UTF-8 split across chunks', () => {
    const parser = new SseParser();
    const fullText =
      'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello 世界","messageId":"msg-1"}\n\n';
    const bytes = new TextEncoder().encode(fullText);

    // Split in the middle of a multi-byte character
    const splitPoint = bytes.length - 5;
    const chunk1 = bytes.slice(0, splitPoint);
    const chunk2 = bytes.slice(splitPoint);

    const events1 = parser.processChunk(chunk1);
    expect(events1).toHaveLength(0);

    const events2 = parser.processChunk(chunk2);
    expect(events2).toHaveLength(1);
    expect((events2[0] as any).text).toBe('Hello 世界');
  });

  it('should handle multi-line data fields', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data: {"version":"1.0",\n' +
        'data: "type":"TEXT_DELTA",\n' +
        'data: "text":"Hello","messageId":"msg-1"}\n\n'
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0].type).toBe('TEXT_DELTA');
  });

  it('should flush remaining data on stream end', () => {
    const parser = new SseParser();

    // Send incomplete event without final newlines
    const chunk = new TextEncoder().encode(
      'data: {"version":"1.0","type":"TEXT_DELTA","text":"Final","messageId":"msg-1"}'
    );
    parser.processChunk(chunk);

    // Flush should return the event
    const events = parser.flush();
    expect(events).toHaveLength(1);
    expect((events[0] as any).text).toBe('Final');
  });

  it('should ignore invalid JSON', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode('data: not valid json\n\n');

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(0);
  });

  it('should ignore non-data lines', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'event: message\n' +
        'id: 123\n' +
        'retry: 1000\n' +
        'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n'
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect((events[0] as any).text).toBe('Hello');
  });

  it('should handle empty chunks', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode('');

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(0);
  });

  it('should handle data: without space', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data:{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n'
    );

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(1);
    expect((events[0] as any).text).toBe('Hello');
  });

  it('should reset parser state', () => {
    const parser = new SseParser();

    // Add partial data
    parser.processChunk(new TextEncoder().encode('data: {"partial":'));

    // Reset
    parser.reset();

    // New complete event should parse correctly
    const chunk = new TextEncoder().encode(
      'data: {"version":"1.0","type":"TEXT_DELTA","text":"Fresh","messageId":"msg-1"}\n\n'
    );
    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect((events[0] as any).text).toBe('Fresh');
  });
});
