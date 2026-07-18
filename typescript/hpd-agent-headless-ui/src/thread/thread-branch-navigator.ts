import type { Thread, ThreadForkGroup, ThreadGraph, ThreadRuntimeChild } from '@hpd-research/hpd-agent-client';
import type {
  ActivePathChoice,
  ThreadBranchNavigationSnapshot,
  ThreadBranchNavigator,
  ThreadBranchNavigatorOptions,
} from './types.js';

export function createThreadBranchNavigator(options: ThreadBranchNavigatorOptions): ThreadBranchNavigator {
  return new ThreadBranchNavigatorImpl(options);
}

class ThreadBranchNavigatorImpl implements ThreadBranchNavigator {
  readonly sessionId;
  private readonly client;
  private currentThreadId;
  private snapshot: ThreadBranchNavigationSnapshot;

  constructor(options: ThreadBranchNavigatorOptions) {
    this.client = options.client;
    this.sessionId = options.sessionId;
    this.currentThreadId = options.threadId;
    this.snapshot = createEmptySnapshot(options.sessionId, options.threadId);
  }

  get threadId(): string {
    return this.currentThreadId;
  }

  getSnapshot(): ThreadBranchNavigationSnapshot {
    return cloneSnapshot(this.snapshot);
  }

  async load(threadId = this.currentThreadId): Promise<ThreadBranchNavigationSnapshot> {
    const graph = await this.client.getThreadGraph(this.sessionId);
    const current = graph.threads.find((thread) => thread.id === threadId) ?? null;
    const runtimeChildren = current ? loadRuntimeChildrenFromGraph(graph, current.id) : [];

    this.currentThreadId = threadId;
    this.snapshot = {
      sessionId: this.sessionId,
      threadId,
      graph,
      current,
      threads: graph.threads,
      forkGroups: graph.forkGroups,
      activePathChoices: getActivePathChoices(graph, threadId),
      runtimeChildren,
      hasRuntimeChildren: runtimeChildren.length > 0,
    };

    return this.getSnapshot();
  }

  selectThread(threadId: string): Promise<ThreadBranchNavigationSnapshot> {
    return this.load(threadId);
  }

  getRuntimeChildScope(threadId: string) {
    const child = this.snapshot.graph.runtimeChildren.find((candidate) => candidate.threadId === threadId);
    return child ? {
      agentId: child.defaultAgentId,
      sessionId: child.sessionId,
      threadId: child.threadId,
    } : null;
  }

  selectForkGroupMember(groupId: string, threadId: string): Promise<ThreadBranchNavigationSnapshot> {
    const group = this.snapshot.forkGroups.find((candidate) => candidate.id === groupId);
    const isMember = group?.members.some((member) => member.threadId === threadId) ?? false;
    return isMember ? this.load(threadId) : this.getOrLoad();
  }

  async previousInGroup(groupId: string): Promise<ThreadBranchNavigationSnapshot> {
    const snapshot = await this.getOrLoad();
    const activeChoice = snapshot.activePathChoices.find((candidate) => candidate.group.id === groupId);
    return activeChoice?.previous ? this.load(activeChoice.previous.threadId) : snapshot;
  }

  async nextInGroup(groupId: string): Promise<ThreadBranchNavigationSnapshot> {
    const snapshot = await this.getOrLoad();
    const activeChoice = snapshot.activePathChoices.find((candidate) => candidate.group.id === groupId);
    return activeChoice?.next ? this.load(activeChoice.next.threadId) : snapshot;
  }

  private getOrLoad(): Promise<ThreadBranchNavigationSnapshot> {
    return this.snapshot.current
      ? Promise.resolve(this.getSnapshot())
      : this.load();
  }
}

function getActivePathChoices(graph: ThreadGraph, threadId: string): ActivePathChoice[] {
  const current = graph.threads.find((thread) => thread.id === threadId);
  const threadsById = new Map(graph.threads.map((thread) => [thread.id, thread]));
  const activePath = getActiveThreadPath(graph, threadId);
  const activeThreadScores = new Map<string, number>();
  for (const [depth, ancestorThreadId] of Object.entries(current?.ancestors ?? {})) {
    activeThreadScores.set(ancestorThreadId, Number(depth));
  }
  activeThreadScores.set(threadId, Number.MAX_SAFE_INTEGER);

  return graph.forkGroups
    .map((group): ActivePathChoice | null => {
      const selectedMember = group.members
        .filter((member) => activeThreadScores.has(member.threadId))
        .sort((left, right) =>
          (activeThreadScores.get(right.threadId) ?? -1) -
          (activeThreadScores.get(left.threadId) ?? -1),
        )[0];
      if (!selectedMember) return null;
      if (!isForkGroupOnActivePath(group, selectedMember.threadId, activePath, threadsById)) return null;

      const previous = group.members[selectedMember.index - 1] ?? null;
      const next = group.members[selectedMember.index + 1] ?? null;
      return {
        group,
        selectedMember,
        selectedThreadId: threadId,
        relationship: selectedMember.threadId === threadId ? 'exact-member' : 'descendant-of-member',
        previous,
        next,
        position: {
          current: selectedMember.index + 1,
          total: group.members.length,
        },
      };
    })
    .filter((group): group is ActivePathChoice => group !== null)
    .sort((left, right) =>
      left.group.choiceMessageIndex - right.group.choiceMessageIndex ||
      left.group.id.localeCompare(right.group.id),
    );
}

function getActiveThreadPath(graph: ThreadGraph, threadId: string): Thread[] {
  const threadsById = new Map(graph.threads.map((thread) => [thread.id, thread]));
  const current = threadsById.get(threadId);
  if (!current) return [];

  const ancestors = Object.entries(current.ancestors ?? {})
    .sort((left, right) =>
      Number(left[0]) - Number(right[0]) ||
      left[0].localeCompare(right[0]),
    )
    .map(([, ancestorThreadId]) => threadsById.get(ancestorThreadId))
    .filter((thread): thread is Thread => Boolean(thread));

  return [...ancestors, current];
}

function isForkGroupOnActivePath(
  group: ThreadForkGroup,
  selectedMemberThreadId: string,
  activePath: readonly Thread[],
  threadsById: ReadonlyMap<string, Thread>,
): boolean {
  const activePathIndex = activePath.findIndex((thread) => thread.id === selectedMemberThreadId);
  if (activePathIndex < 0) return false;

  const selectedMember = group.members.find((member) => member.threadId === selectedMemberThreadId);
  if (!selectedMember) return false;

  if (activePathIndex === activePath.length - 1) return true;

  const nextPathThread = activePath[activePathIndex + 1];
  if (!nextPathThread || nextPathThread.forkedFrom !== selectedMemberThreadId) return true;

  return pathSegmentIncludesForkGroupBoundary(group, nextPathThread, threadsById);
}

function pathSegmentIncludesForkGroupBoundary(
  group: ThreadForkGroup,
  nextPathThread: Thread,
  threadsById: ReadonlyMap<string, Thread>,
): boolean {
  const nextChoiceIndex = resolveThreadChoiceMessageIndex(nextPathThread);

  if (nextChoiceIndex !== null) return nextChoiceIndex > group.choiceMessageIndex;

  if (!group.forkedAtMessageId) return false;
  const nextThread = threadsById.get(nextPathThread.id);
  return nextThread?.forkedAtMessageId === group.forkedAtMessageId;
}

function resolveThreadChoiceMessageIndex(thread: Thread): number | null {
  if (thread.forkedAtMessageIndex == null) {
    return thread.forkedFrom ? 0 : null;
  }
  return thread.forkedAtMessageIndex + 1;
}

function loadRuntimeChildrenFromGraph(graph: ThreadGraph, parentThreadId: string): ThreadRuntimeChild[] {
  return graph.runtimeChildren.filter((child) => child.parentThreadId === parentThreadId);
}

function createEmptySnapshot(sessionId: string, threadId: string): ThreadBranchNavigationSnapshot {
  const graph = { threads: [], forkGroups: [], runtimeChildren: [] };
  return {
    sessionId,
    threadId,
    graph,
    current: null,
    threads: [],
    forkGroups: [],
    activePathChoices: [],
    runtimeChildren: [],
    hasRuntimeChildren: false,
  };
}

function cloneSnapshot(snapshot: ThreadBranchNavigationSnapshot): ThreadBranchNavigationSnapshot {
  const graph = cloneGraph(snapshot.graph);
  return {
    ...snapshot,
    graph,
    current: snapshot.current ? cloneThread(snapshot.current) : null,
    threads: graph.threads,
    forkGroups: graph.forkGroups,
    activePathChoices: snapshot.activePathChoices.map(cloneActivePathChoice),
    runtimeChildren: snapshot.runtimeChildren.map(cloneRuntimeChild),
  };
}

function cloneGraph(graph: ThreadGraph): ThreadGraph {
  return {
    threads: graph.threads.map(cloneThread),
    forkGroups: graph.forkGroups.map(cloneForkGroup),
    runtimeChildren: graph.runtimeChildren.map(cloneRuntimeChild),
  };
}

function cloneForkGroup(group: ThreadForkGroup): ThreadForkGroup {
  return {
    ...group,
    members: group.members.map((member) => ({ ...member })),
  };
}

function cloneActivePathChoice(choice: ActivePathChoice): ActivePathChoice {
  return {
    group: cloneForkGroup(choice.group),
    selectedMember: { ...choice.selectedMember },
    selectedThreadId: choice.selectedThreadId,
    relationship: choice.relationship,
    previous: choice.previous ? { ...choice.previous } : null,
    next: choice.next ? { ...choice.next } : null,
    position: { ...choice.position },
  };
}

function cloneThread(thread: Thread): Thread {
  return {
    ...thread,
    tags: thread.tags ? [...thread.tags] : undefined,
    metadata: thread.metadata ? { ...thread.metadata } : undefined,
    ancestors: thread.ancestors ? { ...thread.ancestors } : undefined,
    childThreads: Array.isArray(thread.childThreads) ? [...thread.childThreads] : [],
  };
}

function cloneRuntimeChild(child: ThreadRuntimeChild): ThreadRuntimeChild {
  return { ...child };
}
