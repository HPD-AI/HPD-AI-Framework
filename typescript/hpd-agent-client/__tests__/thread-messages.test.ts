import { describe, expect, it } from 'vitest';
import {
  EventTypes,
  formatToolResultPayload,
  mapThreadMessage,
  mapThreadMessages,
  projectThreadEventsToMessages,
  type ThreadEvent,
  type ThreadMessage,
} from '../src/index.js';

describe('thread message helpers', () => {
  it('projects durable thread events into materialized thread messages', () => {
    const events: ThreadEvent[] = [
      {
        type: EventTypes.MESSAGE_STARTED,
        messageId: 'm1',
        role: 'assistant',
        createdAt: '2026-01-01T00:00:00.000Z',
        authorName: 'Agent',
        additionalProperties: {
          quote: {
            text: 'quoted context',
            messageId: 'source-1',
          },
        },
      },
      {
        type: EventTypes.CONTENT_ADDED,
        messageId: 'm1',
        content: { $type: 'text', text: 'hello' },
      },
    ];

    expect(projectThreadEventsToMessages(events)).toEqual([
      {
        id: 'm1',
        role: 'assistant',
        contents: [{ $type: 'text', text: 'hello' }],
        additionalProperties: {
          quote: {
            text: 'quoted context',
            messageId: 'source-1',
          },
        },
        timestamp: '2026-01-01T00:00:00.000Z',
        authorName: 'Agent',
      },
    ]);
  });

  it('projects streaming text events into materialized thread messages', () => {
    const events: ThreadEvent[] = [
      {
        type: EventTypes.MESSAGE_STARTED,
        messageId: 'u1',
        role: 'user',
        createdAt: '2026-01-01T00:00:00.000Z',
      },
      {
        type: EventTypes.TEXT_MESSAGE_START,
        messageId: 'u1',
        role: 'user',
      },
      {
        type: EventTypes.TEXT_DELTA,
        messageId: 'u1',
        text: 'who ',
      },
      {
        type: EventTypes.TEXT_DELTA,
        messageId: 'u1',
        text: 'are you',
      },
      {
        type: EventTypes.REASONING_MESSAGE_START,
        messageId: 'a1',
        role: 'assistant',
        timestamp: '2026-01-01T00:00:01.000Z',
      },
      {
        type: EventTypes.REASONING_DELTA,
        messageId: 'a1',
        text: 'thinking',
      },
      {
        type: EventTypes.TEXT_MESSAGE_START,
        messageId: 'a1',
        role: 'assistant',
      },
      {
        type: EventTypes.TEXT_DELTA,
        messageId: 'a1',
        text: 'I am HPD-OS.',
      },
    ];

    expect(mapThreadMessages(projectThreadEventsToMessages(events))).toEqual([
      {
        id: 'u1',
        role: 'user',
        text: 'who are you',
        contents: [
          { $type: 'text', text: 'who ' },
          { $type: 'text', text: 'are you' },
        ],
        reasoningText: undefined,
        timestamp: '2026-01-01T00:00:00.000Z',
        toolCalls: [],
        authorName: undefined,
      },
      {
        id: 'a1',
        role: 'assistant',
        text: 'I am HPD-OS.',
        contents: [
          { $type: 'reasoning', text: 'thinking' },
          { $type: 'text', text: 'I am HPD-OS.' },
        ],
        reasoningText: 'thinking',
        timestamp: '2026-01-01T00:00:01.000Z',
        toolCalls: [],
        authorName: undefined,
      },
    ]);
  });

  it('maps structured thread contents into a client read model', () => {
    const message: ThreadMessage = {
      id: 'm1',
      role: 'assistant',
      timestamp: '2026-01-01T00:00:00.000Z',
      contents: [
        { $type: 'reasoning', text: 'think ' },
        { $type: 'reasoning', text: 'more' },
        { $type: 'text', text: 'hel' },
        { $type: 'text', text: 'lo' },
        {
          $type: 'functionCall',
          callId: 'call1',
          name: 'lookup',
          arguments: { q: 'HPD' },
        },
        {
          $type: 'functionResult',
          callId: 'call1',
          result: { ok: true },
        },
      ],
    };

    expect(mapThreadMessage(message)).toEqual({
      id: 'm1',
      role: 'assistant',
      text: 'hello',
      contents: [
        { $type: 'reasoning', text: 'think ' },
        { $type: 'reasoning', text: 'more' },
        { $type: 'text', text: 'hel' },
        { $type: 'text', text: 'lo' },
        {
          $type: 'functionCall',
          callId: 'call1',
          name: 'lookup',
          arguments: { q: 'HPD' },
        },
        {
          $type: 'functionResult',
          callId: 'call1',
          result: { ok: true },
        },
      ],
      reasoningText: 'think more',
      timestamp: '2026-01-01T00:00:00.000Z',
      toolCalls: [{
        callId: 'call1',
        name: 'lookup',
        messageId: 'm1',
        args: { q: 'HPD' },
        informationalOnly: undefined,
        resultText: '{"ok":true}',
      }],
      authorName: undefined,
    });
  });

  it('filters tool messages from mapped transcript read models', () => {
    const messages: ThreadMessage[] = [
      {
        id: 'u1',
        role: 'user',
        timestamp: '2026-01-01T00:00:00.000Z',
        contents: [{ $type: 'text', text: 'hi' }],
      },
      {
        id: 't1',
        role: 'tool',
        timestamp: '2026-01-01T00:00:01.000Z',
        contents: [{ $type: 'text', text: 'tool chatter' }],
      },
    ];

    expect(mapThreadMessages(messages).map((message) => message.id)).toEqual(['u1']);
  });

  it('formats tool result payloads in protocol priority order', () => {
    expect(formatToolResultPayload({ text: 'plain', json: { ignored: true } })).toBe('plain');
    expect(formatToolResultPayload({ json: { ok: true } })).toBe('{"ok":true}');
    expect(formatToolResultPayload({ content: [{ type: 'text', text: 'nested' }] }))
      .toBe('[{"type":"text","text":"nested"}]');
    expect(formatToolResultPayload({})).toBe('');
  });
});
