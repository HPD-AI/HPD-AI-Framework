import type {
  AgentEvent,
  ToolCallStartEvent,
  ToolCallArgsEvent,
  ToolCallEndEvent,
  ToolCallResultEvent,
  TextDeltaEvent,
  ReasoningDeltaEvent,
} from '@hpd/hpd-agent-client';
import { EventTypes } from '@hpd/hpd-agent-client';
import type { SessionUpdate, AcpToolCallContentEntry } from '../types/acp.js';
import { toolNameToKind } from './tools.js';

/**
 * Translates a subset of HPD AgentEvents to ACP SessionUpdate payloads.
 * Returns null for events with no ACP representation (lifecycle markers,
 * audio events, observability events, etc.).
 */
export function hpdEventToAcpUpdate(event: AgentEvent): SessionUpdate | null {
  switch (event.type) {

    case EventTypes.TEXT_DELTA: {
      const e = event as TextDeltaEvent;
      return {
        sessionUpdate: 'agent_message_chunk',
        content: { type: 'text', text: e.text },
      };
    }

    case EventTypes.REASONING_DELTA: {
      const e = event as ReasoningDeltaEvent;
      return {
        sessionUpdate: 'agent_thought_chunk',
        content: { type: 'text', text: e.text },
      };
    }

    case EventTypes.TOOL_CALL_START: {
      const e = event as ToolCallStartEvent;
      return {
        sessionUpdate: 'tool_call',
        toolCallId: e.callId,
        title: e.name,
        kind: toolNameToKind(e.name),
        status: 'in_progress',
      };
    }

    case EventTypes.TOOL_CALL_ARGS: {
      const e = event as ToolCallArgsEvent;
      let parsed: Record<string, unknown> | undefined;
      try { parsed = JSON.parse(e.argsJson) as Record<string, unknown>; } catch { /* partial */ }
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: e.callId,
        ...(parsed ? { rawInput: parsed } : {}),
      };
    }

    case EventTypes.TOOL_CALL_END: {
      const e = event as ToolCallEndEvent;
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: e.callId,
        status: 'completed',
      };
    }

    case EventTypes.TOOL_CALL_RESULT: {
      const e = event as ToolCallResultEvent;
      const contentEntries: AcpToolCallContentEntry[] = e.result
        ? [{ type: 'content', content: { type: 'text', text: e.result } }]
        : [];
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: e.callId,
        content: contentEntries,
      };
    }

    default:
      return null;
  }
}
