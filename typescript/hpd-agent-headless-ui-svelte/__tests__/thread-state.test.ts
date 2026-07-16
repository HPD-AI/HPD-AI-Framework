import { describe, expect, it, vi } from 'vitest';
import {
  EventTypes,
  type AgentClient,
  type AgentEvent,
  type EventSubscription,
  type Thread,
} from '@hpd-research/hpd-agent-client';
import { createThreadState, type ThreadStateSnapshot } from '../src/index.js';

function subscription(dispose: () => void = () => {}): EventSubscription {
  return { dispose: vi.fn(dispose) };
}

function thread(id: string): Thread {
  return {
    id,
    sessionId: 's1',
    name: id,
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActivity: '2026-01-01T00:00:00.000Z',
    messageCount: 1,
    kind: 'MainAgent',
    visibility: 'Visible',
    childThreads: [],
    totalForks: 0,
  };
}

function fakeClient(): AgentClient & { emit(event: AgentEvent): Promise<void> } {
  const handlers: Array<(event: AgentEvent) => void | Promise<void>> = [];
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
    getThread: vi.fn(async () => thread('main')),
    getThreadEvents: vi.fn(async () => events),
    getThreadRuns: vi.fn(async () => []),
    getThreadState: vi.fn(async () => ({
      observedCursor: { generation: 1, sequenceNumber: events.length },
      activeRun: null,
      pendingRequests: [],
    })),
    emit: async (event: AgentEvent) => {
      for (const handler of [...handlers]) await handler(event);
    },
  };

  return client as unknown as AgentClient & { emit(event: AgentEvent): Promise<void> };
}

describe('createThreadState', () => {
  it('exposes projection updates through a Svelte-compatible readable store', async () => {
    const client = fakeClient();
    const state = createThreadState({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });
    const observed: ThreadStateSnapshot[] = [];
    const unsubscribe = state.subscribe((snapshot) => observed.push(snapshot));

    await state.start({ includeRuns: true });
    expect(state.getSnapshot().transcriptMessages.map((message) => message.id)).toEqual(['u1']);
    expect(state.getSnapshot().connected).toBe(true);

    await client.emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'hi',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });
    await client.emit({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'call1',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    const snapshot = state.getSnapshot();
    expect(snapshot.transcriptMessages.at(-1)?.content).toBe('hi');
    expect(snapshot.pendingRuntimeRequests).toHaveLength(1);
    expect(snapshot.pendingRuntimeRequests[0]?.kind).toBe('permission');
    expect(snapshot.textSubmissionState).toEqual({ canSubmit: false, reason: 'runtime-request' });
    expect(observed.length).toBeGreaterThan(1);

    unsubscribe();
    await state.dispose();
  });

  it('delegates controller actions without owning workspace state', async () => {
    const client = fakeClient();
    const state = createThreadState({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await state.connect();
    await state.sendMessage({ contents: [{ $type: 'text', text: 'hello' }] });

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hello' }],
      }],
      runConfig: undefined,
      clientInputId: expect.any(String),
    });

    await state.dispose();
    expect(client.stop).toHaveBeenCalled();
  });

  it('applies timeline selector options to derived state', async () => {
    const client = fakeClient();
    const state = createThreadState({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      timelineOptions: {
        runtimeRequests: 'exclude',
      },
    });

    await state.start();
    await client.emit({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'call1',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    const snapshot = state.getSnapshot();
    expect(snapshot.projection.timeline.some((item) => item.type === 'runtime-request')).toBe(true);
    expect(snapshot.timeline.some((item) => item.type === 'runtime-request')).toBe(false);
    expect(snapshot.pendingRuntimeRequests).toHaveLength(1);

    await state.dispose();
  });
});
