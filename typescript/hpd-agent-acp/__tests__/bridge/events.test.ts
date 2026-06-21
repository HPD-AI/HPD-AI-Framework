/**
 * Unit tests for bridge/events.ts — HPD AgentEvent → ACP SessionUpdate translation.
 *
 * What these tests cover:
 *   hpdEventToAcpUpdate translates 6 HPD event types to ACP SessionUpdate payloads
 *   and returns null for all other event types (lifecycle, observability, etc.).
 *   Also covers the partial-JSON guard in TOOL_CALL_ARGS.
 *
 * Test type: unit — pure function, no I/O or network.
 */

import { describe, it, expect } from 'vitest';
import { hpdEventToAcpUpdate } from '../../src/bridge/events.js';

// ---------------------------------------------------------------------------
// Minimal event factories matching HPD wire types
// ---------------------------------------------------------------------------

const textDelta = (text: string) => ({
  type: 'TEXT_DELTA' as const,
  text,
  messageId: 'msg-1',
  version: '1.0',
});

const reasoningDelta = (text: string) => ({
  type: 'REASONING_DELTA' as const,
  text,
  messageId: 'msg-1',
  version: '1.0',
});

const toolCallStart = (callId: string, name: string) => ({
  type: 'TOOL_CALL_START' as const,
  callId,
  name,
  messageId: 'msg-1',
  version: '1.0',
});

const toolCallArgs = (callId: string, argsJson: string) => ({
  type: 'TOOL_CALL_ARGS' as const,
  callId,
  argsJson,
  version: '1.0',
});

const toolCallEnd = (callId: string) => ({
  type: 'TOOL_CALL_END' as const,
  callId,
  version: '1.0',
});

const toolCallResult = (callId: string, result: string | null) => ({
  type: 'TOOL_CALL_RESULT' as const,
  callId,
  result,
  version: '1.0',
});

const unknownEvent = () => ({
  type: 'MESSAGE_TURN_FINISHED' as const,
  messageTurnId: 't1',
  conversationId: 'c1',
  agentName: 'test',
  duration: '0',
  timestamp: '',
  version: '1.0',
});

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('hpdEventToAcpUpdate', () => {
  it('TEXT_DELTA → agent_message_chunk with text content', () => {
    const result = hpdEventToAcpUpdate(textDelta('hello') as any);

    expect(result).toEqual({
      sessionUpdate: 'agent_message_chunk',
      content: { type: 'text', text: 'hello' },
    });
  });

  it('REASONING_DELTA → agent_thought_chunk with text content', () => {
    const result = hpdEventToAcpUpdate(reasoningDelta('thinking...') as any);

    expect(result).toEqual({
      sessionUpdate: 'agent_thought_chunk',
      content: { type: 'text', text: 'thinking...' },
    });
  });

  it('TOOL_CALL_START → tool_call with in_progress status and correct kind', () => {
    const result = hpdEventToAcpUpdate(toolCallStart('c1', 'read_file') as any);

    expect(result).toEqual({
      sessionUpdate: 'tool_call',
      toolCallId: 'c1',
      title: 'read_file',
      kind: 'read',
      status: 'in_progress',
    });
  });

  it('TOOL_CALL_ARGS — valid JSON parsed into rawInput', () => {
    const result = hpdEventToAcpUpdate(toolCallArgs('c1', '{"path":"/foo.ts"}') as any);

    expect(result).toMatchObject({
      sessionUpdate: 'tool_call_update',
      toolCallId: 'c1',
      rawInput: { path: '/foo.ts' },
    });
  });

  it('TOOL_CALL_ARGS — partial/invalid JSON does not throw and omits rawInput', () => {
    const result = hpdEventToAcpUpdate(toolCallArgs('c1', '{"path":') as any);

    expect(result).toMatchObject({
      sessionUpdate: 'tool_call_update',
      toolCallId: 'c1',
    });
    expect((result as any).rawInput).toBeUndefined();
  });

  it('TOOL_CALL_END → tool_call_update with completed status', () => {
    const result = hpdEventToAcpUpdate(toolCallEnd('c1') as any);

    expect(result).toEqual({
      sessionUpdate: 'tool_call_update',
      toolCallId: 'c1',
      status: 'completed',
    });
  });

  it('TOOL_CALL_RESULT → tool_call_update with content text entry', () => {
    const result = hpdEventToAcpUpdate(toolCallResult('c1', 'file contents') as any);

    expect(result).toMatchObject({
      sessionUpdate: 'tool_call_update',
      toolCallId: 'c1',
      content: [{ type: 'content', content: { type: 'text', text: 'file contents' } }],
    });
  });

  it('TOOL_CALL_RESULT — null result produces empty content array', () => {
    const result = hpdEventToAcpUpdate(toolCallResult('c1', null) as any);

    expect((result as any).content).toEqual([]);
  });

  it('TOOL_CALL_RESULT — empty string result produces empty content array', () => {
    const result = hpdEventToAcpUpdate(toolCallResult('c1', '') as any);

    expect((result as any).content).toEqual([]);
  });

  it('MESSAGE_TURN_FINISHED → null (no ACP representation)', () => {
    const result = hpdEventToAcpUpdate(unknownEvent() as any);

    expect(result).toBeNull();
  });

  it('unknown event type → null without throwing', () => {
    const result = hpdEventToAcpUpdate({ type: 'SOME_FUTURE_EVENT' } as any);

    expect(result).toBeNull();
  });
});
