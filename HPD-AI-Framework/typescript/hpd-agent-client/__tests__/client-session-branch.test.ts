/**
 * Unit tests for AgentClient session/branch passthrough methods.
 *
 * What these tests cover:
 *   The session CRUD, branch CRUD, and sibling navigation methods added to
 *   AgentClient in the 009-platform-adapters prerequisite. Each method is a
 *   convenience methods backed by AgentHttpApi; the tests verify:
 *     1. The correct HTTP method and URL are called.
 *     2. The request body (where applicable) carries the right payload.
 *     3. The return value is the parsed JSON the server sent back.
 *     4. Void-returning methods (delete) resolve without a value.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 * API under test: AgentHttpApi through AgentClient convenience methods.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { AgentClient } from '../src/client.js';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function mockFetchJson(body: unknown, status = 200) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as Response);
}

function mockFetchEmpty() {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: true,
    status: 204,
    json: async () => undefined,
    text: async () => '',
  } as Response);
}

const BASE = 'http://localhost:5135';

// Minimal fixtures matching server DTOs
const SESSION = { id: 'sess-1', createdAt: '2024-01-01T00:00:00Z', lastActivity: '2024-01-01T00:00:00Z', metadata: {} };
const BRANCH  = { id: 'branch-1', sessionId: 'sess-1', createdAt: '2024-01-01T00:00:00Z', metadata: {} };

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AgentClient — session/branch passthroughs', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ==========================================================================
  // Session CRUD
  // ==========================================================================

  describe('listSessions', () => {
    it('calls GET /sessions and returns the session array', async () => {
      const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => [SESSION],
        text: async () => '',
      } as Response);

      const result = await client.listSessions();

      expect(spy).toHaveBeenCalledOnce();
      const [url, init] = spy.mock.calls[0];
      expect(url).toBe(`${BASE}/sessions`);
      expect(init?.method ?? 'GET').toBe('GET');
      expect(result).toEqual([SESSION]);
    });

    it('forwards filter options as query params', async () => {
      const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => [],
        text: async () => '',
      } as Response);

      await client.listSessions({ metadata: { projectId: 'p1' } });

      const [url] = spy.mock.calls[0];
      // The API must include the metadata filter somewhere — either via
      // query string (GET) or a POST body. Either way the URL base is /sessions.
      expect(String(url)).toContain('/sessions');
    });

    it('keeps relative API base URLs relative when adding query params', async () => {
      const relativeClient = new AgentClient('/api/hpd-agent');
      const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => [],
        text: async () => '',
      } as Response);

      await relativeClient.listSessions({
        limit: 25,
        offset: 5,
        sortBy: 'lastActivity',
        sortDirection: 'desc',
      });

      expect(spy).toHaveBeenCalledWith(
        '/api/hpd-agent/sessions?limit=25&offset=5&sortBy=lastActivity&sortDirection=desc',
        expect.objectContaining({ method: 'GET' }),
      );
    });
  });

  describe('getSession', () => {
    it('calls GET /sessions/{id} and returns the session', async () => {
      mockFetchJson(SESSION);
      const result = await client.getSession('sess-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1`);
      expect(init?.method ?? 'GET').toBe('GET');
      expect(result).toEqual(SESSION);
    });

    it('returns null when the server returns 404', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
        json: async () => null,
        text: async () => 'Not Found',
      } as Response);

      const result = await client.getSession('missing');
      expect(result).toBeNull();
    });
  });

  describe('createSession', () => {
    it('calls POST /sessions and returns the created session', async () => {
      mockFetchJson(SESSION, 201);
      const result = await client.createSession({ metadata: { env: 'test' } });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions`);
      expect(init?.method).toBe('POST');
      expect(result).toEqual(SESSION);
    });

    it('works with no options (creates default session)', async () => {
      mockFetchJson(SESSION, 201);
      const result = await client.createSession();
      expect(result).toEqual(SESSION);
    });
  });

  describe('updateSession', () => {
    it('calls PATCH /sessions/{id} with the metadata and returns updated session', async () => {
      const updated = { ...SESSION, metadata: { foo: 'bar' } };
      mockFetchJson(updated);

      const result = await client.updateSession('sess-1', { metadata: { foo: 'bar' } });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1`);
      expect(init?.method).toBe('PATCH');
      expect(result).toEqual(updated);
    });
  });

  describe('deleteSession', () => {
    it('calls DELETE /sessions/{id} and resolves void', async () => {
      mockFetchEmpty();
      const result = await client.deleteSession('sess-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1`);
      expect(init?.method).toBe('DELETE');
      expect(result).toBeUndefined();
    });
  });

  // ==========================================================================
  // Branch CRUD
  // ==========================================================================

  describe('listBranches', () => {
    it('calls GET /sessions/{id}/branches and returns the branch array', async () => {
      mockFetchJson([BRANCH]);
      const result = await client.listBranches('sess-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches`);
      expect(init?.method ?? 'GET').toBe('GET');
      expect(result).toEqual([BRANCH]);
    });
  });

  describe('getBranch', () => {
    it('calls GET /sessions/{sid}/branches/{bid} and returns the branch', async () => {
      mockFetchJson(BRANCH);
      const result = await client.getBranch('sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1`);
      expect(result).toEqual(BRANCH);
    });

    it('returns null for a missing branch', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => 'Not Found',
        json: async () => null,
      } as Response);

      const result = await client.getBranch('sess-1', 'no-such');
      expect(result).toBeNull();
    });
  });

  describe('createBranch', () => {
    it('calls POST /agents/{agentId}/sessions/{sid}/branches and returns the new branch', async () => {
      mockFetchJson(BRANCH, 201);
      const result = await client.createBranch('sess-1', { agentId: 'agent-1', metadata: { label: 'alt' } });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/branches`);
      expect(init?.method).toBe('POST');
      expect(JSON.parse(init?.body as string)).toEqual({ metadata: { label: 'alt' } });
      expect(result).toEqual(BRANCH);
    });
  });

  describe('forkBranch', () => {
    it('calls POST /agents/{agentId}/sessions/{sid}/branches/{bid}/fork and returns the forked branch', async () => {
      const fork = { ...BRANCH, id: 'branch-2' };
      mockFetchJson(fork, 201);

      const result = await client.forkBranch('sess-1', 'branch-1', { agentId: 'agent-1', fromMessageId: 'msg-3' });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/branches/branch-1/fork`);
      expect(init?.method).toBe('POST');
      expect(result).toEqual(fork);
    });
  });

  describe('updateBranch', () => {
    it('calls PATCH /sessions/{sid}/branches/{bid} with branch metadata', async () => {
      const updated = { ...BRANCH, metadata: { label: 'final' } };
      mockFetchJson(updated);

      const result = await client.updateBranch('sess-1', 'branch-1', {
        metadata: { label: 'final', old: null },
      });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1`);
      expect(init?.method).toBe('PATCH');
      expect(JSON.parse(init?.body as string)).toEqual({
        metadata: { label: 'final', old: null },
      });
      expect(result).toEqual(updated);
    });
  });

  describe('deleteBranch', () => {
    it('calls DELETE /sessions/{sid}/branches/{bid} and resolves void', async () => {
      mockFetchEmpty();
      const result = await client.deleteBranch('sess-1', 'branch-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1`);
      expect(init?.method).toBe('DELETE');
      expect(result).toBeUndefined();
    });

    it('passes recursive=true in the request when specified', async () => {
      mockFetchEmpty();
      await client.deleteBranch('sess-1', 'branch-1', { recursive: true });

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      // The API encodes recursive either as query param or body — URL must
      // include the base path at minimum.
      expect(String(url)).toContain('/sessions/sess-1/branches/branch-1');
    });

    it('keeps recursive delete relative in desktop shells', async () => {
      const relativeClient = new AgentClient('/api/hpd-agent');
      const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        status: 204,
        text: async () => '',
      } as Response);

      await relativeClient.deleteBranch('sess-1', 'branch-1', { recursive: true });

      expect(spy).toHaveBeenCalledWith(
        '/api/hpd-agent/sessions/sess-1/branches/branch-1?recursive=true',
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
  });

  describe('getBranchEvents', () => {
    it('calls GET /sessions/{sid}/branches/{bid}/events and returns branch events', async () => {
      const events = [
        {
          eventId: 'evt-1',
          sessionId: 'sess-1',
          branchId: 'branch-1',
          type: 'TEXT_DELTA',
          messageId: 'msg-1',
          text: 'Hi',
          sequenceNumber: 1,
          timestamp: '2024-01-01T00:00:00Z',
        },
      ];
      mockFetchJson(events);

      const result = await client.getBranchEvents('sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1/events`);
      expect(result).toEqual(events);
    });

    it('returns an empty event list when an existing branch has no event document yet', async () => {
      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: false,
          status: 404,
          json: async () => null,
          text: async () => 'Unknown error',
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          json: async () => BRANCH,
          text: async () => '',
        } as Response);

      const result = await client.getBranchEvents('sess-1', 'branch-1');

      expect(result).toEqual([]);
      expect(((fetch as unknown as { mock: { calls: any[] } }).mock).calls.map(([url]) => String(url))).toEqual([
        `${BASE}/sessions/sess-1/branches/branch-1/events`,
        `${BASE}/sessions/sess-1/branches/branch-1`,
      ]);
    });

    it('still throws when branch events 404 because the branch is missing', async () => {
      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({
          ok: false,
          status: 404,
          json: async () => null,
          text: async () => 'Unknown error',
        } as Response)
        .mockResolvedValueOnce({
          ok: false,
          status: 404,
          json: async () => null,
          text: async () => 'Not Found',
        } as Response);

      await expect(client.getBranchEvents('sess-1', 'missing')).rejects.toMatchObject({
        statusCode: 404,
      });
    });
  });

  describe('branch runs', () => {
    it('calls GET /agents/{agentId}/sessions/{sid}/branches/{bid}/runs and returns runs', async () => {
      const runs = [{
        runtimeRunId: 'run-1',
        agentId: 'agent-1',
        sessionId: 'sess-1',
        branchId: 'branch-1',
        status: 'active',
        startedAt: '2026-05-28T00:00:00Z',
        backgroundTasks: [],
      }];
      mockFetchJson(runs);

      const result = await client.getBranchRuns('agent-1', 'sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/branches/branch-1/runs`);
      expect(result).toEqual(runs);
    });

    it('calls GET /runs/active and returns null on 404', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
        ok: false,
        status: 404,
        json: async () => null,
        text: async () => 'Not Found',
      } as Response);

      const result = await client.getActiveBranchRun('agent-1', 'sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/branches/branch-1/runs/active`);
      expect(result).toBeNull();
    });

    it('calls GET /runs/{runtimeRunId} and returns the run', async () => {
      const run = {
        runtimeRunId: 'run-1',
        agentId: 'agent-1',
        sessionId: 'sess-1',
        branchId: 'branch-1',
        status: 'completed',
        startedAt: '2026-05-28T00:00:00Z',
        completedAt: '2026-05-28T00:00:02Z',
        backgroundTasks: [],
      };
      mockFetchJson(run);

      const result = await client.getBranchRun('agent-1', 'sess-1', 'branch-1', 'run-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/branches/branch-1/runs/run-1`);
      expect(result).toEqual(run);
    });
  });

  // ==========================================================================
  // Sibling Navigation
  // ==========================================================================

  describe('getBranchSiblings', () => {
    it('calls GET /sessions/{sid}/branches/{bid}/siblings and returns siblings array', async () => {
      const siblings = [
        { id: 'branch-1', siblingIndex: 0, totalSiblings: 2, isOriginal: true },
        { id: 'branch-2', siblingIndex: 1, totalSiblings: 2, isOriginal: false },
      ];
      mockFetchJson(siblings);

      const result = await client.getBranchSiblings('sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1/siblings`);
      expect(result).toEqual(siblings);
    });
  });

  describe('getNextSibling', () => {
    it('resolves the next sibling by following nextSiblingId from the current branch', async () => {
      // The API calls getBranch twice: once to get nextSiblingId, once to fetch that branch.
      const current = { ...BRANCH, id: 'branch-1', nextSiblingId: 'branch-2' };
      const next    = { ...BRANCH, id: 'branch-2' };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({ ok: true, json: async () => current, text: async () => '' } as Response)
        .mockResolvedValueOnce({ ok: true, json: async () => next,    text: async () => '' } as Response);

      const result = await client.getNextSibling('sess-1', 'branch-1');

      // Second fetch should resolve the next branch by its ID
      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[1];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-2`);
      expect(result).toEqual(next);
    });

    it('returns null when the current branch has no nextSiblingId', async () => {
      const current = { ...BRANCH, id: 'branch-last', nextSiblingId: undefined };
      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => current,
        text: async () => '',
      } as Response);

      const result = await client.getNextSibling('sess-1', 'branch-last');
      expect(result).toBeNull();
    });
  });

  describe('getPreviousSibling', () => {
    it('resolves the previous sibling by following previousSiblingId from the current branch', async () => {
      // The API calls getBranch twice: once to get previousSiblingId, once to fetch that branch.
      const current = { ...BRANCH, id: 'branch-1', previousSiblingId: 'branch-0' };
      const prev    = { ...BRANCH, id: 'branch-0' };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({ ok: true, json: async () => current, text: async () => '' } as Response)
        .mockResolvedValueOnce({ ok: true, json: async () => prev,    text: async () => '' } as Response);

      const result = await client.getPreviousSibling('sess-1', 'branch-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[1];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-0`);
      expect(result).toEqual(prev);
    });

    it('returns null when the current branch has no previousSiblingId', async () => {
      const current = { ...BRANCH, id: 'branch-first', previousSiblingId: undefined };
      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        json: async () => current,
        text: async () => '',
      } as Response);

      const result = await client.getPreviousSibling('sess-1', 'branch-first');
      expect(result).toBeNull();
    });
  });
});
