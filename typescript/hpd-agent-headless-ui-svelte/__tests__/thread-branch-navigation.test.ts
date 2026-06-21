import { describe, expect, it, vi } from 'vitest';
import type { AgentClient, Thread, ThreadGraph } from '@hpd-research/hpd-agent-client';
import {
  createThreadBranchNavigationState,
  type ThreadBranchNavigationStateSnapshot,
} from '../src/index.js';

const stamp = '2026-01-01T00:00:00.000Z';

function thread(id: string, overrides: Partial<Thread> = {}): Thread {
  return {
    id,
    sessionId: 's1',
    name: id,
    createdAt: stamp,
    lastActivity: stamp,
    messageCount: 1,
    kind: 'MainAgent',
    visibility: 'Visible',
    childThreads: [],
    totalForks: 0,
    ...overrides,
  };
}

function graph(): ThreadGraph {
  return {
    threads: [
      thread('main', { childThreads: ['subagent-1'], totalForks: 1 }),
      thread('edit-1', { forkedFrom: 'main', forkedAtMessageId: 'm1', forkedAtMessageIndex: 0 }),
      thread('retry-1', { forkedFrom: 'main', forkedAtMessageId: 'm1', forkedAtMessageIndex: 0 }),
      thread('subagent-1', { kind: 'SubAgent', visibility: 'Hidden', parentThreadId: 'main' }),
    ],
    forkGroups: [
      {
        id: 'main@m1',
        sourceThreadId: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
        choiceMessageIndex: 1,
        members: [
          { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 1, createdAt: stamp, lastActivity: stamp },
          { threadId: 'edit-1', name: 'edit-1', index: 1, isSource: false, messageCount: 1, createdAt: stamp, lastActivity: stamp },
          { threadId: 'retry-1', name: 'retry-1', index: 2, isSource: false, messageCount: 1, createdAt: stamp, lastActivity: stamp },
        ],
      },
    ],
    runtimeChildren: [{
      threadId: 'subagent-1',
      parentSessionId: 's1',
      parentThreadId: 'main',
      name: 'Reviewer',
      kind: 'SubAgent',
      visibility: 'Hidden',
      subAgentName: 'Reviewer',
      messageCount: 1,
      createdAt: stamp,
      lastActivity: stamp,
    }],
  };
}

function fakeClient(threadGraph = graph()): AgentClient {
  return {
    getThreadGraph: vi.fn(async () => threadGraph),
  } as unknown as AgentClient;
}

describe('createThreadBranchNavigationState', () => {
  it('wraps graph navigation metadata in a Svelte-readable store', async () => {
    const navigation = createThreadBranchNavigationState({
      client: fakeClient(),
      sessionId: 's1',
      threadId: 'main',
    });
    const observed: ThreadBranchNavigationStateSnapshot[] = [];
    const unsubscribe = navigation.subscribe((snapshot) => observed.push(snapshot));

    const snapshot = await navigation.load();

    expect(snapshot.current?.id).toBe('main');
    expect(snapshot.forkGroups.map((group) => group.id)).toEqual(['main@m1']);
    expect(snapshot.runtimeChildren.map((item) => item.threadId)).toEqual(['subagent-1']);
    expect(snapshot.activePathChoices[0].position).toEqual({ current: 1, total: 3 });
    expect(snapshot.activeLabels).toEqual(['Source (1 / 3)']);
    expect(snapshot.hasForkGroups).toBe(true);
    expect(snapshot.hasActivePathChoices).toBe(true);
    expect(snapshot.hasRuntimeChildren).toBe(true);
    expect(observed.some((item) => item.loading)).toBe(true);

    unsubscribe();
  });

  it('moves within fork groups and lets the app choose how to switch UI state', async () => {
    const onSelected = vi.fn();
    const navigation = createThreadBranchNavigationState({
      client: fakeClient(),
      sessionId: 's1',
      threadId: 'main',
      onSelected,
    });

    const next = await navigation.nextInGroup('main@m1');
    expect(next.current?.id).toBe('edit-1');
    expect(next.activeLabels).toEqual(['Fork 2 / 3']);
    expect(onSelected).toHaveBeenCalledWith(expect.objectContaining({
      trigger: 'next-in-group',
      previousThreadId: 'main',
      threadId: 'edit-1',
      groupId: 'main@m1',
    }));

    const selected = await navigation.selectForkGroupMember('main@m1', 'retry-1');
    expect(selected.current?.id).toBe('retry-1');
    expect(onSelected).toHaveBeenLastCalledWith(expect.objectContaining({
      trigger: 'select-fork-group-member',
      previousThreadId: 'edit-1',
      threadId: 'retry-1',
      groupId: 'main@m1',
    }));
  });

  it('does not emit selection for no-op group movement', async () => {
    const onSelected = vi.fn();
    const navigation = createThreadBranchNavigationState({
      client: fakeClient(),
      sessionId: 's1',
      threadId: 'main',
      onSelected,
    });

    const snapshot = await navigation.previousInGroup('main@m1');

    expect(snapshot.current?.id).toBe('main');
    expect(onSelected).not.toHaveBeenCalled();
  });

  it('records load failures without hiding the thrown error', async () => {
    const client = fakeClient();
    vi.mocked(client.getThreadGraph).mockRejectedValueOnce(new Error('navigation failed'));
    const navigation = createThreadBranchNavigationState({
      client,
      sessionId: 's1',
      threadId: 'main',
    });

    await expect(navigation.load()).rejects.toThrow('navigation failed');
    expect(navigation.getSnapshot().loading).toBe(false);
    expect(navigation.getSnapshot().error?.message).toBe('navigation failed');
  });
});
