import { describe, expect, it, vi } from 'vitest';
import {
  EventTypes,
  type AgentClient,
  type AgentEvent,
  type EventSubscription,
  type Thread,
  type ThreadGraph,
  type ThreadMessage,
} from '@hpd-research/hpd-agent-client';
import { createThreadBranchNavigator, createThreadController } from '../src/index.js';

function subscription(dispose: () => void = () => {}): EventSubscription {
  return { dispose: vi.fn(dispose) };
}

function thread(id: string, overrides: Partial<Thread> = {}): Thread {
  return {
    id,
    sessionId: 's1',
    defaultAgentId: 'agent-1',
    name: id,
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActivity: '2026-01-01T00:00:00.000Z',
    messageCount: 1,
    kind: 'MainAgent',
    visibility: 'Visible',
    childThreads: [],
    totalForks: 0,
    ...overrides,
  };
}

function fakeClient(): AgentClient & { emit(event: AgentEvent): Promise<void> } {
  const handlers: Array<(event: AgentEvent) => void | Promise<void>> = [];
  const threads = new Map<string, Thread>([
    ['main', thread('main', {
      childThreads: ['runtime-child'],
      totalForks: 1,
    })],
    ['alt', thread('alt', {
      forkedFrom: 'main',
      forkedAtMessageId: 'u1',
      forkedAtMessageIndex: 0,
    })],
  ]);
  const threadGraph: ThreadGraph = {
    threads: [...threads.values()],
    forkGroups: [{
      id: 'main@u1',
      sourceThreadId: 'main',
      forkedAtMessageId: 'u1',
      forkedAtMessageIndex: 0,
      choiceMessageIndex: 1,
      members: [
        {
          threadId: 'main',
          name: 'main',
          index: 0,
          isSource: true,
          messageCount: 1,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
        {
          threadId: 'alt',
          name: 'alt',
          index: 1,
          isSource: false,
          messageCount: 1,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
      ],
    }],
    runtimeChildren: [{
      threadId: 'runtime-child',
      sessionId: 's1',
      defaultAgentId: 'reviewer-agent',
      parentSessionId: 's1',
      parentThreadId: 'main',
      name: 'Reviewer',
      kind: 'SubAgent',
      visibility: 'Hidden',
      subAgentName: 'Reviewer',
      messageCount: 1,
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
    }],
  };
  const messages: ThreadMessage[] = [{
    id: 'u1',
    role: 'user',
    timestamp: '2026-01-01T00:00:00.000Z',
    contents: [{ $type: 'text', text: 'hello' }],
  }];
  const events: AgentEvent[] = [
    {
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'u1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      timestamp: '2026-01-01T00:00:00.000Z',
      sessionId: 's1',
      threadId: 'main',
    },
    {
      type: EventTypes.TEXT_DELTA,
      messageId: 'u1',
      text: 'hello',
      timestamp: '2026-01-01T00:00:00.000Z',
      sessionId: 's1',
      threadId: 'main',
    },
    {
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'u1',
      timestamp: '2026-01-01T00:00:00.000Z',
      sessionId: 's1',
      threadId: 'main',
    },
  ];

  const client = {
    connected: false,
    start: vi.fn(async () => {
      client.connected = true;
      for (const event of events) {
        for (const handler of [...handlers]) await handler(event);
      }
    }),
    stop: vi.fn(async () => {
      client.connected = false;
    }),
    run: vi.fn(async () => ({ ok: true })),
    submitInput: vi.fn(async () => {}),
    onAny: vi.fn((handler: (event: AgentEvent) => void | Promise<void>) => {
      handlers.push(handler);
      return subscription(() => {
        const index = handlers.indexOf(handler);
        if (index >= 0) handlers.splice(index, 1);
      });
    }),
    onError: vi.fn(() => subscription()),
    getThread: vi.fn(async (_sessionId: string, threadId: string) =>
      threads.get(threadId) ?? null),
    getThreadMessages: vi.fn(async () => messages),
    getThreadEvents: vi.fn(async () => events),
    getThreadExecutions: vi.fn(async () => []),
    getThreadState: vi.fn(async () => ({
      observedCursor: { generation: 1, sequenceNumber: events.length },
      activeExecution: null,
      pendingRequests: [],
    })),
    getThreadGraph: vi.fn(async () => threadGraph),
    emit: async (event: AgentEvent) => {
      for (const handler of [...handlers]) await handler(event);
    },
  };

  return client as unknown as AgentClient & { emit(event: AgentEvent): Promise<void> };
}

describe('thread lifecycle scenario', () => {
  it('composes controller projection and navigator without a workspace runtime', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });
    const navigator = createThreadBranchNavigator({
      client,
      sessionId: 's1',
      threadId: 'main',
    });

    const observed = vi.fn();
    const unsubscribe = controller.projection.subscribe(observed);

    await controller.start({ includeExecutions: true });
    expect(controller.projection.getSnapshot().transcriptMessages.map((message) => message.id)).toEqual(['u1']);
    expect(client.start).toHaveBeenCalledWith({
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      after: { generation: 1, sequenceNumber: 0 },
      signal: undefined,
    });

    await client.emit({
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:01.000Z',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'hi',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'call1',
      sessionId: 's1',
      threadId: 'main',
    });

    let snapshot = controller.projection.getSnapshot();
    expect(snapshot.threadExecution?.status).toBe('active');
    expect(snapshot.activity.streaming).toBe(true);
    expect(snapshot.transcriptMessages.at(-1)?.content).toBe('hi');
    expect(snapshot.pendingRuntimeRequests.map((request) => request.id)).toEqual(['p1']);
    expect(snapshot.pendingRuntimeRequests[0]?.kind).toBe('permission');
    expect(snapshot.canSend).toBe(false);

    const approval = await controller.approve('p1');
    expect(approval).toEqual({ ok: true });
    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
      choice: 'ask',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await client.emit({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'a1',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.THREAD_EXECUTION_FINISHED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      outcome: 'Succeeded',
      finishedAt: '2026-01-01T00:00:02.000Z',
      sessionId: 's1',
      threadId: 'main',
    });

    snapshot = controller.projection.getSnapshot();
    expect(snapshot.pendingRuntimeRequests).toEqual([]);
    expect(snapshot.threadExecution?.status).toBe('succeeded');
    expect(snapshot.activity.streaming).toBe(false);
    expect(snapshot.canSend).toBe(true);

    const navigation = await navigator.load();
    expect(navigation.current?.id).toBe('main');
    expect(navigation.activePathChoices[0].next?.threadId).toBe('alt');
    expect(navigation.runtimeChildren.map((child) => child.threadId)).toEqual(['runtime-child']);

    const next = await navigator.nextInGroup('main@u1');
    expect(next.current?.id).toBe('alt');
    expect(navigator.threadId).toBe('alt');

    unsubscribe();
    await controller.dispose();
    await client.emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'ignored',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.stop).toHaveBeenCalled();
    expect(controller.projection.getSnapshot().transcriptMessages.map((message) => message.id))
      .not.toContain('ignored');
    expect(observed).toHaveBeenCalled();
  });
});
