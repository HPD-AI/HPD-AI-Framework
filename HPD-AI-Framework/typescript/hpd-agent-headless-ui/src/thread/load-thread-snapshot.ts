import type { ThreadRun } from '@hpd-research/hpd-agent-client';
import type { ThreadSnapshot, LoadThreadSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.getThread(sessionId, threadId);
  const eventsPromise = client.getThreadEvents(sessionId, threadId);
  const runsPromise = loadOptions.includeRuns
    ? client.getThreadRuns(agentId, sessionId, threadId)
    : Promise.resolve<ThreadRun[]>([]);
  const activeRunPromise = loadOptions.includeRuns
    ? client.getActiveThreadRun(agentId, sessionId, threadId)
    : Promise.resolve<ThreadRun | null>(null);

  const [thread, events, runs, activeRun] = await Promise.all([
    threadPromise,
    eventsPromise,
    runsPromise,
    activeRunPromise,
  ]);

  return {
    thread,
    events,
    runs,
    activeRun,
  };
}
