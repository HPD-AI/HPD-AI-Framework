import { installResources } from './sdk-client-fixture.js';
import { describe, expect, it, vi } from 'vitest';
import { EventTypes, type AgentClient } from '@hpd-research/hpd-agent-client';
import { loadThreadSnapshot } from '../src/index.js';

function fakeClient(): AgentClient {
  return installResources({
    getThread: vi.fn(async () => ({ id: 'main', sessionId: 's1' })),
    getThreadState: vi.fn(async () => ({
      observedCursor: { generation: 1, sequenceNumber: 0 },
      activeExecution: null,
      pendingRequests: [{
        request: {
          type: 'PERMISSION_REQUEST',
          permissionId: 'p1',
          sourceName: 'permission',
          functionName: 'Bash',
          callId: 'c1',
        },
        createdAt: '2026-01-01T00:00:00.000Z',
      }],
    })),
    getThreadExecutions: vi.fn(async () => []),
  }) as unknown as AgentClient;
}

describe('loadThreadSnapshot', () => {
  it('loads durable thread baseline from events by default', async () => {
    const client = fakeClient();

    const snapshot = await loadThreadSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.getThread).toHaveBeenCalledWith('s1', 'main');
    expect(client.getThreadState).toHaveBeenCalledWith('agent', 's1', 'main');
    expect(client.getThreadExecutions).not.toHaveBeenCalled();
    expect(snapshot.thread).toEqual({ id: 'main', sessionId: 's1' });
    expect(snapshot.events).toEqual([]);
    expect(snapshot.observedCursor).toEqual({ generation: 1, sequenceNumber: 0 });
    expect(snapshot.executions).toEqual([]);
    expect(snapshot.activeExecution).toBeNull();
    expect(snapshot.pendingRequests).toHaveLength(1);
    expect(snapshot.pendingRequests?.[0].request).toMatchObject({ permissionId: 'p1' });
  });

  it('can include thread executions explicitly', async () => {
    const client = fakeClient();

    await loadThreadSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    }, {
      includeExecutions: true,
    });

    expect(client.getThreadState).toHaveBeenCalledWith('agent', 's1', 'main');
    expect(client.getThreadExecutions).toHaveBeenCalledWith('agent', 's1', 'main');
  });

  it('replays committed events through the journal head before connecting live', async () => {
    const event = {
      type: EventTypes.USER_MESSAGE,
      messageId: 'user-1',
      text: 'restore this message',
      threadSequenceNumber: 2,
      sessionId: 's1',
      threadId: 'main',
      timestamp: '2026-01-01T00:00:00.000Z',
    };
    const client = installResources({
      getThread: vi.fn(async () => ({ id: 'main', sessionId: 's1' })),
      getThreadState: vi.fn(async () => ({
        observedCursor: { generation: 3, sequenceNumber: 2 },
        activeExecution: null,
        pendingRequests: [],
      })),
      getThreadExecutions: vi.fn(async () => []),
      start: vi.fn(async () => {}),
      stop: vi.fn(async () => {}),
      onAny: vi.fn((handler: (event: typeof event) => void) => {
        handler(event);
        return { dispose: vi.fn() };
      }),
    }) as unknown as AgentClient;

    const snapshot = await loadThreadSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.start).toHaveBeenCalledWith({
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      after: { generation: 3, sequenceNumber: 0 },
      signal: undefined,
    });
    expect(client.stop).toHaveBeenCalled();
    expect(snapshot.events).toEqual([event]);
    expect(snapshot.observedCursor).toEqual({ generation: 3, sequenceNumber: 2 });
  });
});
