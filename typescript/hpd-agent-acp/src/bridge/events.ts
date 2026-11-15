import type { HpdAgentEvent, HPDAgentToolCallResultEvent } from '@hpd-research/hpd-agent-client';
import { EventTypes } from '@hpd-research/hpd-agent-client';
import type { SessionUpdate, AcpToolCallContentEntry } from '../types/acp.js';
import { toolNameToKind } from './tools.js';

function formatToolResultPayload(result: HPDAgentToolCallResultEvent['result'] | string | null | undefined): string {
  if (result == null) return '';
  if (typeof result === 'string') return result;
  if (result.text) return result.text;
  if (result.json !== undefined) return JSON.stringify(result.json);
  if (result.content && result.content.length > 0) return JSON.stringify(result.content);
  return '';
}

/**
 * Translates a subset of HPD AgentEvents to ACP SessionUpdate payloads.
 * Returns null for events with no ACP representation (lifecycle markers,
 * audio events, observability events, etc.).
 */
export function hpdEventToAcpUpdate(event: HpdAgentEvent): SessionUpdate | null {
  switch (event.type) {

    case EventTypes.TEXT_DELTA: {
      return {
        sessionUpdate: 'agent_message_chunk',
        content: { type: 'text', text: event.text },
      };
    }

    case EventTypes.REASONING_DELTA: {
      return {
        sessionUpdate: 'agent_thought_chunk',
        content: { type: 'text', text: event.text },
      };
    }

    case EventTypes.TOOL_CALL_START: {
      return {
        sessionUpdate: 'tool_call',
        toolCallId: event.callId,
        title: event.name,
        kind: toolNameToKind(event.name),
        status: 'in_progress',
      };
    }

    case EventTypes.TOOL_CALL_ARGS: {
      let parsed: Record<string, unknown> | undefined;
      try { parsed = JSON.parse(event.argsJson) as Record<string, unknown>; } catch { /* partial */ }
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: event.callId,
        ...(parsed ? { rawInput: parsed } : {}),
      };
    }

    case EventTypes.TOOL_CALL_END: {
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: event.callId,
        status: 'completed',
      };
    }

    case EventTypes.TOOL_CALL_RESULT: {
      const text = formatToolResultPayload(event.result);
      const contentEntries: AcpToolCallContentEntry[] = text
        ? [{ type: 'content', content: { type: 'text', text } }]
        : [];
      return {
        sessionUpdate: 'tool_call_update',
        toolCallId: event.callId,
        content: contentEntries,
      };
    }

    default:
      return null;
  }
}
