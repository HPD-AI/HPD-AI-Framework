import type { ThreadExecutionDto } from '@hpd-research/hpd-agent-client';
import type { ThreadSnapshot, LoadThreadSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.api.threads.getThread({ sessionId, threadId });
  const statePromise = client.api.agentRuntime.getAgentThreadRuntimeState({ agentId, sessionId, threadId });
  const executionsPromise = loadOptions.includeExecutions
    ? client.api.executions.listThreadExecutions({ agentId, sessionId, threadId })
    : Promise.resolve<ThreadExecutionDto[]>([]);

  const [thread, state, executions] = await Promise.all([
    threadPromise,
    statePromise,
    executionsPromise,
  ]);

  return {
    thread,
    events: [],
    observedCursor: { generation: state.observedCursor.generation ?? 1, sequenceNumber: state.observedCursor.sequenceNumber ?? 0 },
    executions,
    activeExecution: state?.activeExecution ?? null,
    pendingRequests: state.pendingRequests.map(({ request, createdAt }) => ({
      request,
      createdAt,
    })),
  };
}
