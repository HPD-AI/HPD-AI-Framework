import type { ClarificationRequestEvent } from '@hpd/hpd-agent-client';
import type { AcpSessionState } from './session.js';
import type { AcpWriter } from '../acp/writer.js';

/**
 * Handles CLARIFICATION_REQUEST events.
 *
 * ACP has no native clarification method, so we use two mechanisms:
 *
 * 1. Emit the question as an agent_message_chunk so every editor renders it.
 * 2. Include a `hpd.dev` _meta extension so editors that understand it can
 *    render a choice UI rather than plain text.
 *
 * When the next session/prompt arrives on this session and a clarification is
 * pending, the bridge routes that prompt as a clarification_response to HPD
 * instead of starting a new stream. See the routing logic in index.ts.
 */
export function handleClarificationRequest(
  event: ClarificationRequestEvent,
  session: AcpSessionState,
  writer: AcpWriter,
): Promise<string> {
  return new Promise((resolve, reject) => {
    // Emit the question as a visible agent message with hpd.dev meta extension
    writer.notifySessionUpdate(session.acpSessionId, {
      sessionUpdate: 'agent_message_chunk',
      content: { type: 'text', text: event.question },
      _meta: {
        'hpd.dev': {
          type: 'clarification_request',
          requestId: event.requestId,
          options: event.options ?? [],
        },
      },
    });

    session.pendingClarification = {
      hpdRequestId: event.requestId,
      resolve,
      reject,
    };
  });
}

/**
 * Called when a new session/prompt arrives on a session that has a pending
 * clarification. Routes the prompt text as the clarification answer.
 *
 * Returns true if the prompt was consumed as a clarification response,
 * false if the session had no pending clarification (normal send).
 */
export function tryResolveClarification(
  session: AcpSessionState,
  promptText: string,
): boolean {
  if (!session.pendingClarification) return false;

  session.pendingClarification.resolve(promptText);
  session.pendingClarification = null;
  return true;
}
