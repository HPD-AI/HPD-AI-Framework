import { describe, it, expect } from 'vitest';
import { SseParser } from '../src/parser.js';

const route = '"route":{"origin":{"sessionId":"s","threadId":"main"},"path":[{"sessionId":"s","threadId":"main"}]}}';

describe('SseParser', () => {
  it('should parse a single complete event', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"},${route}\n\n`
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({
      kind: 'agent-event',
      id: null,
      delivery: {
        event: {
        version: '1.0',
        type: 'TEXT_DELTA',
        text: 'Hello',
        messageId: 'msg-1',
        },
        route: {
          origin: { sessionId: 's', threadId: 'main' },
          path: [{ sessionId: 's', threadId: 'main' }],
        },
      },
    });
  });

  it('should parse multiple events in one chunk', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"},${route}\n\n` +
        `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":" World","messageId":"msg-1"},${route}\n\n`
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(2);
    expect((events[0].delivery.event as any).text).toBe('Hello');
    expect((events[1].delivery.event as any).text).toBe(' World');
  });

  it('should handle events split across chunks', () => {
    const parser = new SseParser();

    // First chunk - incomplete
    const chunk1 = new TextEncoder().encode('data: {"event":{"version":"1.0","type":"TEXT_');
    const events1 = parser.processChunk(chunk1);
    expect(events1).toHaveLength(0);

    // Second chunk - completes the event
    const chunk2 = new TextEncoder().encode(`DELTA","text":"Hello","messageId":"msg-1"},${route}\n\n`);
    const events2 = parser.processChunk(chunk2);
    expect(events2).toHaveLength(1);
    expect((events2[0].delivery.event as any).text).toBe('Hello');
  });

  it('should handle UTF-8 split across chunks', () => {
    const parser = new SseParser();
    const fullText =
      `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Hello 世界","messageId":"msg-1"},${route}\n\n`;
    const bytes = new TextEncoder().encode(fullText);

    // Split in the middle of a multi-byte character
    const splitPoint = bytes.length - 5;
    const chunk1 = bytes.slice(0, splitPoint);
    const chunk2 = bytes.slice(splitPoint);

    const events1 = parser.processChunk(chunk1);
    expect(events1).toHaveLength(0);

    const events2 = parser.processChunk(chunk2);
    expect(events2).toHaveLength(1);
    expect((events2[0].delivery.event as any).text).toBe('Hello 世界');
  });

  it('should handle multi-line data fields', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data: {"event":{"version":"1.0",\n' +
        'data: "type":"TEXT_DELTA",\n' +
        `data: "text":"Hello","messageId":"msg-1"},${route}\n\n`
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0].delivery.event.type).toBe('TEXT_DELTA');
  });

  it('should flush remaining data on stream end', () => {
    const parser = new SseParser();

    // Send incomplete event without final newlines
    const chunk = new TextEncoder().encode(
      `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Final","messageId":"msg-1"},${route}`
    );
    parser.processChunk(chunk);

    // Flush should return the event
    const events = parser.flush();
    expect(events).toHaveLength(1);
    expect((events[0].delivery.event as any).text).toBe('Final');
  });

  it('should ignore invalid JSON', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode('data: not valid json\n\n');

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(0);
  });

  it('should ignore JSON values that are not event objects', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'data: null\n\n' +
        'data: true\n\n' +
        'data: {"version":"1.0","text":"missing type"}\n\n'
    );

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(0);
  });

  it('should parse durable thread update events with threadMetadata payloads', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      `data: {"event":{"version":"1.0","type":"THREAD_UPDATED","defaultAgentId":"reviewer-agent","name":"Reviewer","threadKind":"SubAgent","visibility":"Hidden","parentSessionId":"session-1","parentThreadId":"main","subAgentName":"Reviewer","invocationId":"run-1","subAgentSourceKind":"SuppliedAgentConfiguration","parentToolCallId":"call-1","contextPolicy":"Fork","forkedFrom":"main","forkedAtMessageId":"message-1","forkedAtMessageIndex":0,"childThreads":["child-1"],"ancestors":{"main":"message-1"},"threadMetadata":{"purpose":"review"}},${route}\n\n`
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0]).toEqual({
      kind: 'agent-event',
      id: null,
      delivery: { event: {
        version: '1.0',
        type: 'THREAD_UPDATED',
        name: 'Reviewer',
        threadKind: 'SubAgent',
        visibility: 'Hidden',
        parentSessionId: 'session-1',
        parentThreadId: 'main',
        subAgentName: 'Reviewer',
        defaultAgentId: 'reviewer-agent',
        invocationId: 'run-1',
        subAgentSourceKind: 'SuppliedAgentConfiguration',
        parentToolCallId: 'call-1',
        contextPolicy: 'Fork',
        forkedFrom: 'main',
        forkedAtMessageId: 'message-1',
        forkedAtMessageIndex: 0,
        childThreads: ['child-1'],
        ancestors: {
          main: 'message-1',
        },
        threadMetadata: {
          purpose: 'review',
        },
      }, route: { origin: { sessionId: 's', threadId: 'main' }, path: [{ sessionId: 's', threadId: 'main' }] } },
    });
  });

  it('should distinguish live events that do not carry a journal cursor', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'event: live-agent-event\n' +
      `data: {"event":{"type":"TEXT_DELTA","text":"Live","messageId":"msg-live"},${route}\n\n`
    );

    expect(parser.processChunk(chunk)).toEqual([{
      kind: 'live-agent-event',
      id: null,
      delivery: { event: {
        type: 'TEXT_DELTA',
        text: 'Live',
        messageId: 'msg-live',
      }, route: { origin: { sessionId: 's', threadId: 'main' }, path: [{ sessionId: 's', threadId: 'main' }] } },
    }]);
  });

  it('should retain the committed SSE id and ignore unrelated fields', () => {
    const parser = new SseParser();
    const chunk = new TextEncoder().encode(
      'event: message\n' +
        'id: 123\n' +
        'retry: 1000\n' +
        `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"},${route}\n\n`
    );

    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect(events[0].id).toBe('123');
    expect((events[0].delivery.event as any).text).toBe('Hello');
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
      `data:{"event":{"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"},${route}\n\n`
    );

    const events = parser.processChunk(chunk);
    expect(events).toHaveLength(1);
    expect((events[0].delivery.event as any).text).toBe('Hello');
  });

  it('should reset parser state', () => {
    const parser = new SseParser();

    // Add partial data
    parser.processChunk(new TextEncoder().encode('data: {"partial":'));

    // Reset
    parser.reset();

    // New complete event should parse correctly
    const chunk = new TextEncoder().encode(
      `data: {"event":{"version":"1.0","type":"TEXT_DELTA","text":"Fresh","messageId":"msg-1"},${route}\n\n`
    );
    const events = parser.processChunk(chunk);

    expect(events).toHaveLength(1);
    expect((events[0].delivery.event as any).text).toBe('Fresh');
  });
});
