import type { ThreadExecution } from '@hpd-research/hpd-agent-client';
import type { ThreadSnapshot, LoadThreadSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.getThread(sessionId, threadId);
  const statePromise = client.getThreadState(agentId, sessionId, threadId);
  const executionsPromise = loadOptions.includeExecutions
    ? client.getThreadExecutions(agentId, sessionId, threadId)
    : Promise.resolve<ThreadExecution[]>([]);

  const [thread, state, executions] = await Promise.all([
    threadPromise,
    statePromise,
    executionsPromise,
  ]);

  return {
    thread,
    events: [],
    observedCursor: state?.observedCursor ?? { generation: 1, sequenceNumber: 0 },
    executions,
    activeExecution: state?.activeExecution ?? null,
    pendingRequests: state?.pendingRequests ?? [],
  };
}
