import type { AgentEvent } from '@hpd-research/hpd-agent-client';
import type { BranchScope } from './types.js';

export interface EventScopeOptions {
  allowScopeLess?: boolean;
}

export function eventBelongsToScope(
  event: AgentEvent,
  scope: BranchScope,
  options: EventScopeOptions = {},
): boolean {
  const eventAgentId = 'agentId' in event ? event.agentId : undefined;
  const eventSessionId = 'sessionId' in event ? event.sessionId : undefined;
  const eventBranchId = 'branchId' in event ? event.branchId : undefined;

  if (!eventAgentId && !eventSessionId && !eventBranchId) {
    return options.allowScopeLess === true;
  }

  if (eventAgentId && eventAgentId !== scope.agentId) {
    return false;
  }

  if (eventSessionId && eventSessionId !== scope.sessionId) {
    return false;
  }

  if (eventBranchId && eventBranchId !== scope.branchId) {
    return false;
  }

  return true;
}

export function withBranchScope<T extends AgentEvent>(
  event: T,
  scope: BranchScope,
): T {
  return {
    ...event,
    agentId: 'agentId' in event ? event.agentId ?? scope.agentId : scope.agentId,
    sessionId: event.sessionId ?? scope.sessionId,
    branchId: event.branchId ?? scope.branchId,
  } as T;
}
