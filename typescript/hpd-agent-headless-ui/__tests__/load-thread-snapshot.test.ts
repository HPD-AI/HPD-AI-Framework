import { describe, expect, it, vi } from 'vitest';
import type { AgentClient } from '@hpd-research/hpd-agent-client';
import { loadThreadSnapshot } from '../src/index.js';

function fakeClient(): AgentClient {
  return {
    getThread: vi.fn(async () => ({ id: 'main', sessionId: 's1' })),
    getThreadState: vi.fn(async () => ({
      observedCursor: { generation: 1, sequenceNumber: 0 },
      activeExecution: null,
      pendingRequests: [],
    })),
    getThreadExecutions: vi.fn(async () => []),
  } as unknown as AgentClient;
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
});
