import type { BranchEvent, BranchRun } from '@hpd-research/hpd-agent-client';
import type { BranchSnapshot, LoadBranchSnapshotOptions, RehydrateOptions } from './types.js';

export async function loadBranchSnapshot(
  options: LoadBranchSnapshotOptions,
  loadOptions: RehydrateOptions = {},
): Promise<BranchSnapshot> {
  const { client, agentId, sessionId, branchId } = options;

  const branchPromise = client.getBranch(sessionId, branchId);
  const messagesPromise = client.getBranchMessages(sessionId, branchId);
  const eventsPromise = loadOptions.includeEvents
    ? client.getBranchEvents(sessionId, branchId)
    : Promise.resolve<BranchEvent[]>([]);
  const runsPromise = loadOptions.includeRuns
    ? client.getBranchRuns(agentId, sessionId, branchId)
    : Promise.resolve<BranchRun[]>([]);
  const activeRunPromise = loadOptions.includeRuns
    ? client.getActiveBranchRun(agentId, sessionId, branchId)
    : Promise.resolve<BranchRun | null>(null);

  const [branch, messages, events, runs, activeRun] = await Promise.all([
    branchPromise,
    messagesPromise,
    eventsPromise,
    runsPromise,
    activeRunPromise,
  ]);

  return {
    branch,
    messages,
    events,
    runs,
    activeRun,
  };
}
