import { describe, expect, it, vi } from 'vitest';
import { ConversationState, readBranchMessage } from '../src/conversation.js';
import { EventTypes } from '../src/types/events.js';
import type { BranchMessage } from '../src/types/session.js';

describe('ConversationState', () => {
  it('reduces streamed text and tool events into stable items', () => {
    const conversation = new ConversationState();
    const listener = vi.fn();
    conversation.onChange(listener);

    conversation.applyEvent({ type: EventTypes.TEXT_MESSAGE_START, messageId: 'm1', role: 'assistant' });
    conversation.applyEvent({ type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'Hel' });
    conversation.applyEvent({ type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'lo' });
    conversation.applyEvent({ type: EventTypes.TEXT_MESSAGE_END, messageId: 'm1' });
    conversation.applyEvent({ type: EventTypes.TOOL_CALL_START, callId: 'c1', messageId: 'm1', name: 'lookup' });
    conversation.applyEvent({ type: EventTypes.TOOL_CALL_ARGS, callId: 'c1', argsJson: '{"q":"x"}' });
    conversation.applyEvent({ type: EventTypes.TOOL_CALL_RESULT, callId: 'c1', result: { text: 'done' } });

    expect(conversation.items).toHaveLength(2);
    expect(conversation.items[0]).toMatchObject({
      kind: 'message',
      id: 'm1',
      role: 'assistant',
      text: 'Hello',
      status: 'complete',
    });
    expect(conversation.items[1]).toMatchObject({
      kind: 'tool',
      callId: 'c1',
      name: 'lookup',
      args: { q: 'x' },
      result: { text: 'done' },
      status: 'complete',
    });
    expect(listener).toHaveBeenCalled();
  });

  it('hydrates branch messages into the same conversation item model', () => {
    const message: BranchMessage = {
      id: 'history-1',
      role: 'assistant',
      timestamp: '2026-01-01T00:00:00Z',
      contents: [
        { $type: 'text', text: 'Answer' },
        { $type: 'functionCall', callId: 'call-1', name: 'tool', arguments: { value: 1 } },
        { $type: 'functionResult', callId: 'call-1', result: { ok: true } },
      ],
    };

    const history = readBranchMessage(message);
    const conversation = new ConversationState();
    conversation.applyBranchMessage(message);

    expect(history.map((item) => item.kind)).toEqual(['text', 'toolCall', 'toolResult']);
    expect(conversation.items).toHaveLength(2);
    expect(conversation.items[0]).toMatchObject({ kind: 'message', text: 'Answer', source: 'history' });
    expect(conversation.items[1]).toMatchObject({ kind: 'tool', callId: 'call-1', status: 'complete' });
  });

  it('hydrates tool calls from tolerant stored JSON shapes', () => {
    const message = {
      id: 'history-2',
      role: 'assistant',
      timestamp: '2026-01-01T00:00:00Z',
      contents: [
        { type: 'function_call', call_id: 'call-2', function_name: 'write_artifact', arguments: '{"id":"a1","content":"hi"}' },
        { type: 'function_result', call_id: 'call-2', value: { text: 'ok' } },
      ],
    } as unknown as BranchMessage;

    const conversation = new ConversationState();
    conversation.applyBranchMessage(message);

    expect(conversation.items).toHaveLength(1);
    expect(conversation.items[0]).toMatchObject({
      kind: 'tool',
      callId: 'call-2',
      name: 'write_artifact',
      args: { id: 'a1', content: 'hi' },
      result: { text: 'ok' },
      source: 'history',
      status: 'complete',
    });
  });

  it('emits error items for message turn errors', () => {
    const conversation = new ConversationState();
    conversation.applyEvent({ type: EventTypes.MESSAGE_TURN_ERROR, message: 'failed' });
    expect(conversation.items).toEqual([
      expect.objectContaining({ kind: 'error', message: 'failed', status: 'error' }),
    ]);
  });

});
