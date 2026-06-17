import { describe, expect, it, vi } from 'vitest';
import type { AgentClient } from '@hpd-research/hpd-agent-client';
import { loadThreadSnapshot } from '../src/index.js';

function fakeClient(): AgentClient {
  return {
    getThread: vi.fn(async () => ({ id: 'main', sessionId: 's1' })),
    getThreadMessages: vi.fn(async () => []),
    getThreadEvents: vi.fn(async () => []),
    getThreadRuns: vi.fn(async () => []),
    getActiveThreadRun: vi.fn(async () => null),
  } as unknown as AgentClient;
}

describe('loadThreadSnapshot', () => {
  it('loads durable thread baseline without runs or events by default', async () => {
    const client = fakeClient();

    const snapshot = await loadThreadSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.getThread).toHaveBeenCalledWith('s1', 'main');
    expect(client.getThreadMessages).toHaveBeenCalledWith('s1', 'main');
    expect(client.getThreadEvents).not.toHaveBeenCalled();
    expect(client.getThreadRuns).not.toHaveBeenCalled();
    expect(snapshot.thread).toEqual({ id: 'main', sessionId: 's1' });
    expect(snapshot.events).toEqual([]);
    expect(snapshot.runs).toEqual([]);
    expect(snapshot.activeRun).toBeNull();
  });

  it('can include durable events and thread runs explicitly', async () => {
    const client = fakeClient();

    await loadThreadSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    }, {
      includeEvents: true,
      includeRuns: true,
    });

    expect(client.getThreadEvents).toHaveBeenCalledWith('s1', 'main');
    expect(client.getThreadRuns).toHaveBeenCalledWith('agent', 's1', 'main');
    expect(client.getActiveThreadRun).toHaveBeenCalledWith('agent', 's1', 'main');
  });
});
