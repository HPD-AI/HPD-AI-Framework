import type { ThreadConversationElementProps } from './types.js';
import type { ThreadStateSnapshot } from '../thread-state.js';

export function createThreadConversationElementProps(
  snapshot: ThreadStateSnapshot,
  restProps: Record<string, unknown> = {},
): ThreadConversationElementProps {
  return {
    ...restProps,
    'data-hpd-thread-conversation': '',
    'data-busy': snapshot.activity.status !== 'idle' ? '' : undefined,
    'data-empty': snapshot.timeline.length === 0 ? '' : undefined,
    'data-requesting': snapshot.pendingRuntimeRequests.length > 0 ? '' : undefined,
  } as ThreadConversationElementProps;
}
