import type { AgentEvent } from './events.js';

export interface ThreadKey {
  sessionId: string;
  threadId: string;
}

export type AgentEventHierarchy =
  | 'exactThread'
  | 'directChildren'
  | 'threadAndDirectChildren'
  | 'descendants'
  | 'threadAndDescendants';

export interface AgentEventRoute {
  origin: ThreadKey;
  path: ThreadKey[];
  threadExecutionId?: string | null;
}

export interface AgentEventDelivery<TEvent extends AgentEvent = AgentEvent> {
  event: TEvent;
  route: AgentEventRoute;
}
