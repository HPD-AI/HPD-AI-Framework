import { describe, expect, it, vi } from 'vitest';
import type { AgentClient, Thread, ThreadGraph } from '@hpd-research/hpd-agent-client';
import { createThreadBranchNavigator } from '../src/index.js';

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
      thread('main', { childThreads: ['runtime-child'], totalForks: 1 }),
      thread('alt', { forkedFrom: 'main', forkedAtMessageId: 'm1', forkedAtMessageIndex: 0 }),
      thread('child', {
        forkedFrom: 'alt',
        forkedAtMessageId: 'm2',
        forkedAtMessageIndex: 1,
        ancestors: { '0': 'main', '1': 'alt' },
      }),
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
          { threadId: 'alt', name: 'alt', index: 1, isSource: false, messageCount: 1, createdAt: stamp, lastActivity: stamp },
        ],
      },
      {
        id: 'alt@m2',
        sourceThreadId: 'alt',
        forkedAtMessageId: 'm2',
        forkedAtMessageIndex: 1,
        choiceMessageIndex: 2,
        members: [
          { threadId: 'alt', name: 'alt', index: 0, isSource: true, messageCount: 1, createdAt: stamp, lastActivity: stamp },
          { threadId: 'child', name: 'child', index: 1, isSource: false, messageCount: 1, createdAt: stamp, lastActivity: stamp },
        ],
      },
    ],
    runtimeChildren: [
      {
        threadId: 'runtime-child',
        parentSessionId: 's1',
        parentThreadId: 'main',
        name: 'Reviewer',
        kind: 'SubAgent',
        visibility: 'Hidden',
        subAgentName: 'Reviewer',
        messageCount: 1,
        createdAt: stamp,
        lastActivity: stamp,
      },
    ],
  };
}

function fakeClient(threadGraph = graph()): AgentClient {
  return {
    getThreadGraph: vi.fn(async () => threadGraph),
  } as unknown as AgentClient;
}

describe('createThreadBranchNavigator', () => {
  it('loads a graph snapshot with current thread, runtime children, and active fork groups', async () => {
    const client = fakeClient();
    const navigator = createThreadBranchNavigator({
      client,
      sessionId: 's1',
      threadId: 'main',
    });

    const snapshot = await navigator.load();

    expect(snapshot.current?.id).toBe('main');
    expect(snapshot.forkGroups.map((group) => group.id)).toEqual(['main@m1', 'alt@m2']);
    expect(snapshot.runtimeChildren.map((item) => item.threadId)).toEqual(['runtime-child']);
    expect(snapshot.activePathChoices.map((item) => item.group.id)).toEqual(['main@m1']);
    expect(snapshot.activePathChoices[0].next?.threadId).toBe('alt');
    expect(snapshot.hasRuntimeChildren).toBe(true);
  });

  it('moves within a specific fork group', async () => {
    const client = fakeClient();
    const navigator = createThreadBranchNavigator({
      client,
      sessionId: 's1',
      threadId: 'main',
    });

    const next = await navigator.nextInGroup('main@m1');
    expect(next.current?.id).toBe('alt');
    expect(next.activePathChoices.find((group) => group.group.id === 'main@m1')?.previous?.threadId).toBe('main');

    const previous = await navigator.previousInGroup('main@m1');
    expect(previous.current?.id).toBe('main');
    expect(navigator.threadId).toBe('main');
  });

  it('keeps ancestor fork groups active when the selected thread is a descendant', async () => {
    const client = fakeClient();
    const navigator = createThreadBranchNavigator({
      client,
      sessionId: 's1',
      threadId: 'child',
    });

    const snapshot = await navigator.load();

    expect(snapshot.activePathChoices.map((item) => item.group.id)).toEqual(['main@m1', 'alt@m2']);
    expect(snapshot.activePathChoices[0].selectedMember.threadId).toBe('alt');
    expect(snapshot.activePathChoices[0].selectedThreadId).toBe('child');
    expect(snapshot.activePathChoices[0].relationship).toBe('descendant-of-member');
    expect(snapshot.activePathChoices[1].selectedMember.threadId).toBe('child');
    expect(snapshot.activePathChoices[1].relationship).toBe('exact-member');
  });

  it('does not activate later source-path fork groups after the selected path forked earlier', async () => {
    const threadGraph: ThreadGraph = {
      threads: [
        thread('main', { messageCount: 20 }),
        thread('early-fork', {
          forkedFrom: 'main',
          forkedAtMessageId: 'm2',
          forkedAtMessageIndex: 1,
          ancestors: { '0': 'main' },
          messageCount: 20,
        }),
        thread('late-main-fork', {
          forkedFrom: 'main',
          forkedAtMessageId: 'm10',
          forkedAtMessageIndex: 9,
          ancestors: { '0': 'main' },
          messageCount: 12,
        }),
      ],
      forkGroups: [
        {
          id: 'main@m2',
          sourceThreadId: 'main',
          forkedAtMessageId: 'm2',
          forkedAtMessageIndex: 1,
          choiceMessageIndex: 2,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 20, createdAt: stamp, lastActivity: stamp },
            { threadId: 'early-fork', name: 'early-fork', index: 1, isSource: false, messageCount: 20, createdAt: stamp, lastActivity: stamp },
          ],
        },
        {
          id: 'main@m10',
          sourceThreadId: 'main',
          forkedAtMessageId: 'm10',
          forkedAtMessageIndex: 9,
          choiceMessageIndex: 10,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 20, createdAt: stamp, lastActivity: stamp },
            { threadId: 'late-main-fork', name: 'late-main-fork', index: 1, isSource: false, messageCount: 12, createdAt: stamp, lastActivity: stamp },
          ],
        },
      ],
      runtimeChildren: [],
    };
    const navigator = createThreadBranchNavigator({
      client: fakeClient(threadGraph),
      sessionId: 's1',
      threadId: 'early-fork',
    });

    const snapshot = await navigator.load();

    expect(snapshot.activePathChoices.map((item) => item.group.id)).toEqual(['main@m2']);
  });

  it('keeps earlier choices active when selecting a later descendant of their selected member', async () => {
    const threadGraph: ThreadGraph = {
      threads: [
        thread('main', { messageCount: 6 }),
        thread('edit-root', {
          forkedFrom: 'main',
          forkedAtMessageId: null,
          forkedAtMessageIndex: null,
          ancestors: { '0': 'main' },
          messageCount: 2,
        }),
        thread('edit-second-prompt', {
          forkedFrom: 'main',
          forkedAtMessageId: 'assistant-1',
          forkedAtMessageIndex: 1,
          ancestors: { '0': 'main' },
          messageCount: 4,
        }),
        thread('edit-third-prompt', {
          forkedFrom: 'edit-second-prompt',
          forkedAtMessageId: 'assistant-2',
          forkedAtMessageIndex: 3,
          ancestors: { '0': 'main', '1': 'edit-second-prompt' },
          messageCount: 6,
        }),
      ],
      forkGroups: [
        {
          id: 'main@root',
          sourceThreadId: 'main',
          forkedAtMessageId: undefined,
          forkedAtMessageIndex: undefined,
          choiceMessageIndex: 0,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 6, createdAt: stamp, lastActivity: stamp },
            { threadId: 'edit-root', name: 'edit-root', index: 1, isSource: false, messageCount: 2, createdAt: stamp, lastActivity: stamp },
          ],
        },
        {
          id: 'main@assistant-1',
          sourceThreadId: 'main',
          forkedAtMessageId: 'assistant-1',
          forkedAtMessageIndex: 1,
          choiceMessageIndex: 2,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 6, createdAt: stamp, lastActivity: stamp },
            { threadId: 'edit-second-prompt', name: 'edit-second-prompt', index: 1, isSource: false, messageCount: 4, createdAt: stamp, lastActivity: stamp },
          ],
        },
        {
          id: 'edit-second-prompt@assistant-2',
          sourceThreadId: 'edit-second-prompt',
          forkedAtMessageId: 'assistant-2',
          forkedAtMessageIndex: 3,
          choiceMessageIndex: 4,
          members: [
            { threadId: 'edit-second-prompt', name: 'edit-second-prompt', index: 0, isSource: true, messageCount: 4, createdAt: stamp, lastActivity: stamp },
            { threadId: 'edit-third-prompt', name: 'edit-third-prompt', index: 1, isSource: false, messageCount: 6, createdAt: stamp, lastActivity: stamp },
          ],
        },
      ],
      runtimeChildren: [],
    };
    const navigator = createThreadBranchNavigator({
      client: fakeClient(threadGraph),
      sessionId: 's1',
      threadId: 'edit-third-prompt',
    });

    const snapshot = await navigator.load();

    expect(snapshot.activePathChoices.map((item) => item.group.id)).toEqual([
      'main@root',
      'main@assistant-1',
      'edit-second-prompt@assistant-2',
    ]);
    expect(snapshot.activePathChoices[0].selectedMember.threadId).toBe('main');
    expect(snapshot.activePathChoices[0].relationship).toBe('descendant-of-member');
    expect(snapshot.activePathChoices[1].selectedMember.threadId).toBe('edit-second-prompt');
    expect(snapshot.activePathChoices[1].relationship).toBe('descendant-of-member');
  });

  it('does not activate an ancestor fork member group after the selected path forked away earlier', async () => {
    const threadGraph: ThreadGraph = {
      threads: [
        thread('main', { messageCount: 20 }),
        thread('fork-at-a', {
          forkedFrom: 'main',
          forkedAtMessageId: 'a',
          forkedAtMessageIndex: 9,
          ancestors: { '0': 'main' },
          messageCount: 20,
        }),
        thread('fork-at-a-minus-one', {
          forkedFrom: 'fork-at-a',
          forkedAtMessageId: 'a-minus-one',
          forkedAtMessageIndex: 8,
          ancestors: { '0': 'main', '1': 'fork-at-a' },
          messageCount: 12,
        }),
      ],
      forkGroups: [
        {
          id: 'main@a-minus-one',
          sourceThreadId: 'main',
          forkedAtMessageId: 'a-minus-one',
          forkedAtMessageIndex: 8,
          choiceMessageIndex: 9,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 20, createdAt: stamp, lastActivity: stamp },
            { threadId: 'fork-at-a-minus-one', name: 'fork-at-a-minus-one', index: 1, isSource: false, messageCount: 12, createdAt: stamp, lastActivity: stamp },
          ],
        },
        {
          id: 'main@a',
          sourceThreadId: 'main',
          forkedAtMessageId: 'a',
          forkedAtMessageIndex: 9,
          choiceMessageIndex: 10,
          members: [
            { threadId: 'main', name: 'main', index: 0, isSource: true, messageCount: 20, createdAt: stamp, lastActivity: stamp },
            { threadId: 'fork-at-a', name: 'fork-at-a', index: 1, isSource: false, messageCount: 20, createdAt: stamp, lastActivity: stamp },
          ],
        },
      ],
      runtimeChildren: [],
    };
    const navigator = createThreadBranchNavigator({
      client: fakeClient(threadGraph),
      sessionId: 's1',
      threadId: 'fork-at-a-minus-one',
    });

    const snapshot = await navigator.load();

    expect(snapshot.activePathChoices.map((item) => item.group.id)).toEqual(['main@a-minus-one']);
  });
});
