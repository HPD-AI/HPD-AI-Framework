import type { AgentEvent } from '@hpd-research/hpd-agent-client';
import type { ThreadScope } from './types.js';

export interface EventScopeOptions {
  allowScopeLess?: boolean;
}

export function eventBelongsToScope(
  event: AgentEvent,
  scope: ThreadScope,
  options: EventScopeOptions = {},
): boolean {
  const eventAgentId = 'agentId' in event ? event.agentId : undefined;
  const eventSessionId = 'sessionId' in event ? event.sessionId : undefined;
  const eventThreadId = 'threadId' in event ? event.threadId : undefined;

  if (!eventAgentId && !eventSessionId && !eventThreadId) {
    return options.allowScopeLess === true;
  }

  if (eventAgentId && eventAgentId !== scope.agentId) {
    return false;
  }

  if (eventSessionId && eventSessionId !== scope.sessionId) {
    return false;
  }

  if (eventThreadId && eventThreadId !== scope.threadId) {
    return false;
  }

  return true;
}

export function withThreadScope<T extends AgentEvent>(
  event: T,
  scope: ThreadScope,
): T {
  return {
    ...event,
    agentId: 'agentId' in event ? event.agentId ?? scope.agentId : scope.agentId,
    sessionId: event.sessionId ?? scope.sessionId,
    threadId: event.threadId ?? scope.threadId,
  } as T;
}
