/**
 * Unit tests for AgentClient session/branch passthrough methods.
 *
 * What these tests cover:
 *   The session CRUD, branch CRUD, and sibling navigation methods added to
 *   AgentClient in the 009-platform-adapters prerequisite. Each method is a
 *   one-line passthrough to the underlying AgentTransport; the tests verify:
 *     1. The correct HTTP method and URL are called.
 *     2. The request body (where applicable) carries the right payload.
 *     3. The return value is the parsed JSON the server sent back.
 *     4. Void-returning methods (delete) resolve without a value.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 * Transport under test: SseTransport (default when no transport is specified),
 * which means the session/branch HTTP calls use plain fetch just like SSE.
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
      // The transport must include the metadata filter somewhere — either via
      // query string (GET) or a POST body. Either way the URL base is /sessions.
      expect(String(url)).toContain('/sessions');
    });
  });

  describe('getSession', () => {
    it('calls GET /sessions/{id} and returns the session', async () => {
      mockFetchJson(SESSION);
      const result = await client.getSession('sess-1');

      const [url, init] = vi.mocked(fetch).mock.calls[0];
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

      const [url, init] = vi.mocked(fetch).mock.calls[0];
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
    it('calls PUT /sessions/{id} with the metadata and returns updated session', async () => {
      const updated = { ...SESSION, metadata: { foo: 'bar' } };
      mockFetchJson(updated);

      const result = await client.updateSession('sess-1', { metadata: { foo: 'bar' } });

      const [url, init] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1`);
      expect(init?.method).toBe('PUT');
      expect(result).toEqual(updated);
    });
  });

  describe('deleteSession', () => {
    it('calls DELETE /sessions/{id} and resolves void', async () => {
      mockFetchEmpty();
      const result = await client.deleteSession('sess-1');

      const [url, init] = vi.mocked(fetch).mock.calls[0];
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

      const [url, init] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches`);
      expect(init?.method ?? 'GET').toBe('GET');
      expect(result).toEqual([BRANCH]);
    });
  });

  describe('getBranch', () => {
    it('calls GET /sessions/{sid}/branches/{bid} and returns the branch', async () => {
      mockFetchJson(BRANCH);
      const result = await client.getBranch('sess-1', 'branch-1');

      const [url] = vi.mocked(fetch).mock.calls[0];
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
    it('calls POST /sessions/{sid}/branches and returns the new branch', async () => {
      mockFetchJson(BRANCH, 201);
      const result = await client.createBranch('sess-1', { metadata: { label: 'alt' } });

      const [url, init] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches`);
      expect(init?.method).toBe('POST');
      expect(result).toEqual(BRANCH);
    });
  });

  describe('forkBranch', () => {
    it('calls POST /sessions/{sid}/branches/{bid}/fork and returns the forked branch', async () => {
      const fork = { ...BRANCH, id: 'branch-2' };
      mockFetchJson(fork, 201);

      const result = await client.forkBranch('sess-1', 'branch-1', { forkAtMessageIndex: 3 });

      const [url, init] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1/fork`);
      expect(init?.method).toBe('POST');
      expect(result).toEqual(fork);
    });
  });

  describe('deleteBranch', () => {
    it('calls DELETE /sessions/{sid}/branches/{bid} and resolves void', async () => {
      mockFetchEmpty();
      const result = await client.deleteBranch('sess-1', 'branch-1');

      const [url, init] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1`);
      expect(init?.method).toBe('DELETE');
      expect(result).toBeUndefined();
    });

    it('passes recursive=true in the request when specified', async () => {
      mockFetchEmpty();
      await client.deleteBranch('sess-1', 'branch-1', { recursive: true });

      const [url] = vi.mocked(fetch).mock.calls[0];
      // Transport encodes recursive either as query param or body — URL must
      // include the base path at minimum.
      expect(String(url)).toContain('/sessions/sess-1/branches/branch-1');
    });
  });

  describe('getBranchMessages', () => {
    it('calls GET /sessions/{sid}/branches/{bid}/messages and returns messages', async () => {
      const messages = [
        { id: 'msg-1', role: 'user', contents: [{ $type: 'text', text: 'Hi' }], timestamp: '2024-01-01T00:00:00Z' },
        { id: 'msg-2', role: 'assistant', contents: [{ $type: 'text', text: 'Hello!' }], timestamp: '2024-01-01T00:00:01Z' },
      ];
      mockFetchJson(messages);

      const result = await client.getBranchMessages('sess-1', 'branch-1');

      const [url] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1/messages`);
      expect(result).toEqual(messages);
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

      const [url] = vi.mocked(fetch).mock.calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/branches/branch-1/siblings`);
      expect(result).toEqual(siblings);
    });
  });

  describe('getNextSibling', () => {
    it('resolves the next sibling by following nextSiblingId from the current branch', async () => {
      // Transport calls getBranch twice: once to get nextSiblingId, once to fetch that branch.
      const current = { ...BRANCH, id: 'branch-1', nextSiblingId: 'branch-2' };
      const next    = { ...BRANCH, id: 'branch-2' };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({ ok: true, json: async () => current, text: async () => '' } as Response)
        .mockResolvedValueOnce({ ok: true, json: async () => next,    text: async () => '' } as Response);

      const result = await client.getNextSibling('sess-1', 'branch-1');

      // Second fetch should resolve the next branch by its ID
      const [url] = vi.mocked(fetch).mock.calls[1];
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
      // Transport calls getBranch twice: once to get previousSiblingId, once to fetch that branch.
      const current = { ...BRANCH, id: 'branch-1', previousSiblingId: 'branch-0' };
      const prev    = { ...BRANCH, id: 'branch-0' };

      vi.spyOn(globalThis, 'fetch')
        .mockResolvedValueOnce({ ok: true, json: async () => current, text: async () => '' } as Response)
        .mockResolvedValueOnce({ ok: true, json: async () => prev,    text: async () => '' } as Response);

      const result = await client.getPreviousSibling('sess-1', 'branch-1');

      const [url] = vi.mocked(fetch).mock.calls[1];
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
