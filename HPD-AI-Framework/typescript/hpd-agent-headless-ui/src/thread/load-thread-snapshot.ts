import type { ThreadEvent, ThreadRun } from '@hpd-research/hpd-agent-client';
import type { ThreadSnapshot, LoadThreadSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadThreadSnapshot(
  options: LoadThreadSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<ThreadSnapshot> {
  const { client, agentId, sessionId, threadId } = options;

  const threadPromise = client.getThread(sessionId, threadId);
  const messagesPromise = client.getThreadMessages(sessionId, threadId);
  const eventsPromise = loadOptions.includeEvents
    ? client.getThreadEvents(sessionId, threadId)
    : Promise.resolve<ThreadEvent[]>([]);
  const runsPromise = loadOptions.includeRuns
    ? client.getThreadRuns(agentId, sessionId, threadId)
    : Promise.resolve<ThreadRun[]>([]);
  const activeRunPromise = loadOptions.includeRuns
    ? client.getActiveThreadRun(agentId, sessionId, threadId)
    : Promise.resolve<ThreadRun | null>(null);

  const [thread, messages, events, runs, activeRun] = await Promise.all([
    threadPromise,
    messagesPromise,
    eventsPromise,
    runsPromise,
    activeRunPromise,
  ]);

  return {
    thread,
    messages,
    events,
    runs,
    activeRun,
  };
}
