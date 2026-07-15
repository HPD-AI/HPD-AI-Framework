import type { ThreadRun } from '@hpd-research/hpd-agent-client';
import type { ThreadSnapshot, LoadThreadSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.getThread(sessionId, threadId);
  const statePromise = client.getThreadState(agentId, sessionId, threadId);
  const runsPromise = loadOptions.includeRuns
    ? client.getThreadRuns(agentId, sessionId, threadId)
    : Promise.resolve<ThreadRun[]>([]);

  const [thread, state, runs] = await Promise.all([
    threadPromise,
    statePromise,
    runsPromise,
  ]);

  return {
    thread,
    events: state?.events ?? [],
    latestSequenceNumber: state?.latestSequenceNumber ?? 0,
    runs,
    activeRun: state?.activeRun ?? null,
  };
}
