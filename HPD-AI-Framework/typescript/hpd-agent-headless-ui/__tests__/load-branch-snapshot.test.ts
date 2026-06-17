import { describe, expect, it, vi } from 'vitest';
import type { AgentClient } from '@hpd-research/hpd-agent-client';
import { loadBranchSnapshot } from '../src/index.js';

function fakeClient(): AgentClient {
  return {
    getBranch: vi.fn(async () => ({ id: 'main', sessionId: 's1' })),
    getBranchMessages: vi.fn(async () => []),
    getBranchEvents: vi.fn(async () => []),
    getBranchRuns: vi.fn(async () => []),
    getActiveBranchRun: vi.fn(async () => null),
  } as unknown as AgentClient;
}

describe('loadBranchSnapshot', () => {
  it('loads durable branch baseline without runs or events by default', async () => {
    const client = fakeClient();

    const snapshot = await loadBranchSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      branchId: 'main',
    });

    expect(client.getBranch).toHaveBeenCalledWith('s1', 'main');
    expect(client.getBranchMessages).toHaveBeenCalledWith('s1', 'main');
    expect(client.getBranchEvents).not.toHaveBeenCalled();
    expect(client.getBranchRuns).not.toHaveBeenCalled();
    expect(snapshot.branch).toEqual({ id: 'main', sessionId: 's1' });
    expect(snapshot.events).toEqual([]);
    expect(snapshot.runs).toEqual([]);
    expect(snapshot.activeRun).toBeNull();
  });

  it('can include durable events and branch runs explicitly', async () => {
    const client = fakeClient();

    await loadBranchSnapshot({
      client,
      agentId: 'agent',
      sessionId: 's1',
      branchId: 'main',
    }, {
      includeEvents: true,
      includeRuns: true,
    });

    expect(client.getBranchEvents).toHaveBeenCalledWith('s1', 'main');
    expect(client.getBranchRuns).toHaveBeenCalledWith('agent', 's1', 'main');
    expect(client.getActiveBranchRun).toHaveBeenCalledWith('agent', 's1', 'main');
  });
});
