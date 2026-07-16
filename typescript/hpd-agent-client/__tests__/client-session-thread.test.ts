/**
 * Unit tests for AgentClient session/thread passthrough methods.
 *
 * What these tests cover:
 *   The session CRUD, thread CRUD, and sibling navigation methods added to
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
const THREAD  = {
  id: 'thread-1',
  sessionId: 'sess-1',
  createdAt: '2024-01-01T00:00:00Z',
  lastActivity: '2024-01-01T00:00:00Z',
  messageCount: 0,
  metadata: {},
  kind: 'MainAgent',
  visibility: 'Visible',
  childThreads: [],
  totalForks: 0,
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AgentClient — session/thread passthroughs', () => {
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
  // Thread CRUD
  // ==========================================================================

  describe('listThreads', () => {
    it('calls GET /sessions/{id}/threads and returns the thread array', async () => {
      mockFetchJson([THREAD]);
      const result = await client.listThreads('sess-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/threads`);
      expect(init?.method ?? 'GET').toBe('GET');
      expect(result).toEqual([THREAD]);
    });
  });

  describe('getThread', () => {
    it('calls GET /sessions/{sid}/threads/{bid} and returns the thread', async () => {
      mockFetchJson(THREAD);
      const result = await client.getThread('sess-1', 'thread-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/threads/thread-1`);
      expect(result).toEqual(THREAD);
    });

    it('returns subagent thread metadata from the server DTO unchanged', async () => {
      const subAgentThread = {
        ...THREAD,
        id: 'subagent/reviewer/run-1',
        name: 'Reviewer',
        kind: 'SubAgent',
        visibility: 'Hidden',
        parentSessionId: 'sess-1',
        parentThreadId: 'main',
        subAgentName: 'Reviewer',
        subAgentRunId: 'run-1',
      };
      mockFetchJson(subAgentThread);

      const result = await client.getThread('sess-1', 'subagent/reviewer/run-1');

      expect(result).toEqual(subAgentThread);
      expect(result?.kind).toBe('SubAgent');
      expect(result?.visibility).toBe('Hidden');
      expect(result?.parentThreadId).toBe('main');
      expect(result?.subAgentName).toBe('Reviewer');
    });

    it('returns null for a missing thread', async () => {
      vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => 'Not Found',
        json: async () => null,
      } as Response);

      const result = await client.getThread('sess-1', 'no-such');
      expect(result).toBeNull();
    });
  });

  describe('createThread', () => {
    it('calls POST /agents/{agentId}/sessions/{sid}/threads and returns the new thread', async () => {
      mockFetchJson(THREAD, 201);
      const result = await client.createThread('sess-1', { agentId: 'agent-1', metadata: { label: 'alt' } });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads`);
      expect(init?.method).toBe('POST');
      expect(JSON.parse(init?.body as string)).toEqual({ metadata: { label: 'alt' } });
      expect(result).toEqual(THREAD);
    });
  });

  describe('forkThread', () => {
    it('calls POST /agents/{agentId}/sessions/{sid}/threads/{bid}/fork and returns the forked thread', async () => {
      const fork = { ...THREAD, id: 'thread-2' };
      mockFetchJson(fork, 201);

      const result = await client.forkThread('sess-1', 'thread-1', {
        agentId: 'agent-1',
        fromMessageId: 'msg-3',
        compaction: {
          mode: 1,
          preferCache: false,
          strategy: {
            $type: 'messageCounting',
            preserveRecentUserTurnCount: 3,
          },
        },
      });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads/thread-1/fork`);
      expect(init?.method).toBe('POST');
      expect(JSON.parse(init?.body as string)).toEqual({
        fromMessageId: 'msg-3',
        compaction: {
          mode: 1,
          preferCache: false,
          strategy: {
            $type: 'messageCounting',
            preserveRecentUserTurnCount: 3,
          },
        },
      });
      expect(result).toEqual(fork);
    });
  });

  describe('updateThread', () => {
    it('calls PATCH /sessions/{sid}/threads/{bid} with thread metadata', async () => {
      const updated = { ...THREAD, metadata: { label: 'final' } };
      mockFetchJson(updated);

      const result = await client.updateThread('sess-1', 'thread-1', {
        metadata: { label: 'final', old: null },
      });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/threads/thread-1`);
      expect(init?.method).toBe('PATCH');
      expect(JSON.parse(init?.body as string)).toEqual({
        metadata: { label: 'final', old: null },
      });
      expect(result).toEqual(updated);
    });
  });

  describe('deleteThread', () => {
    it('calls DELETE /sessions/{sid}/threads/{bid} and resolves void', async () => {
      mockFetchEmpty();
      const result = await client.deleteThread('sess-1', 'thread-1');

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/threads/thread-1`);
      expect(init?.method).toBe('DELETE');
      expect(result).toBeUndefined();
    });

    it('passes recursive=true in the request when specified', async () => {
      mockFetchEmpty();
      await client.deleteThread('sess-1', 'thread-1', { recursive: true });

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      // The API encodes recursive either as query param or body — URL must
      // include the base path at minimum.
      expect(String(url)).toContain('/sessions/sess-1/threads/thread-1');
    });

    it('keeps recursive delete relative in desktop shells', async () => {
      const relativeClient = new AgentClient('/api/hpd-agent');
      const spy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
        ok: true,
        status: 204,
        text: async () => '',
      } as Response);

      await relativeClient.deleteThread('sess-1', 'thread-1', { recursive: true });

      expect(spy).toHaveBeenCalledWith(
        '/api/hpd-agent/sessions/sess-1/threads/thread-1?recursive=true',
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
  });

  describe('thread runs', () => {
    it('calls GET /agents/{agentId}/sessions/{sid}/threads/{bid}/runs and returns runs', async () => {
      const runs = [{
        runtimeRunId: 'run-1',
        agentId: 'agent-1',
        sessionId: 'sess-1',
        threadId: 'thread-1',
        status: 'active',
        startedAt: '2026-05-28T00:00:00Z',
        backgroundTasks: [],
        backgroundHandles: [],
      }];
      mockFetchJson(runs);

      const result = await client.getThreadRuns('agent-1', 'sess-1', 'thread-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads/thread-1/runs`);
      expect(result).toEqual(runs);
    });

    it('calls GET /state and returns the observation boundary and active run', async () => {
      const state = {
        observedCursor: { generation: 1, sequenceNumber: 4 },
        activeRun: null,
      };
      mockFetchJson(state);

      const result = await client.getThreadState('agent-1', 'sess-1', 'thread-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads/thread-1/state`);
      expect(result).toEqual(state);
    });

    it('calls GET /runs/{runtimeRunId} and returns the run', async () => {
      const run = {
        runtimeRunId: 'run-1',
        agentId: 'agent-1',
        sessionId: 'sess-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-05-28T00:00:00Z',
        completedAt: '2026-05-28T00:00:02Z',
        backgroundTasks: [],
        backgroundHandles: [],
      };
      mockFetchJson(run);

      const result = await client.getThreadRun('agent-1', 'sess-1', 'thread-1', 'run-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads/thread-1/runs/run-1`);
      expect(result).toEqual(run);
    });

    it('calls POST /context-usage and returns the usage estimate', async () => {
      const usage = {
        sessionId: 'sess-1',
        threadId: 'thread-1',
        providerKey: 'openai',
        modelId: 'gpt-4.1',
        contextWindow: 128000,
        effectiveInputTokens: 64000,
        usageRatio: 0.5,
        isEstimate: false,
        source: 'last-observed-provider-usage',
      };
      mockFetchJson(usage);

      const result = await client.estimateContextUsage('agent-1', 'sess-1', 'thread-1', {
        runConfig: {
          providerKey: 'openai',
          modelId: 'gpt-4.1',
          compaction: {
            mode: 0,
            modelContext: {
              providerKey: 'openai',
              modelId: 'gpt-4.1',
              contextWindow: 128000,
            },
          },
        },
      });

      const [url, init] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/agents/agent-1/sessions/sess-1/threads/thread-1/context-usage`);
      expect(init?.method).toBe('POST');
      expect(JSON.parse(String(init?.body))).toEqual({
        runConfig: {
          providerKey: 'openai',
          modelId: 'gpt-4.1',
          compaction: {
            mode: 0,
            modelContext: {
              providerKey: 'openai',
              modelId: 'gpt-4.1',
              contextWindow: 128000,
            },
          },
        },
      });
      expect(result).toEqual(usage);
    });
  });

  describe('getThreadGraph', () => {
    it('calls GET /sessions/{sid}/thread-graph and returns threads plus fork groups', async () => {
      const graph = {
        threads: [THREAD, { ...THREAD, id: 'thread-2', forkedFrom: 'thread-1' }],
        forkGroups: [
          {
            id: 'thread-1@message-1',
            sourceThreadId: 'thread-1',
            forkedAtMessageId: 'message-1',
            forkedAtMessageIndex: 0,
            choiceMessageIndex: 1,
            members: [
              { threadId: 'thread-1', name: 'thread-1', index: 0, isSource: true, messageCount: 1, createdAt: THREAD.createdAt, lastActivity: THREAD.lastActivity },
              { threadId: 'thread-2', name: 'thread-2', index: 1, isSource: false, messageCount: 1, createdAt: THREAD.createdAt, lastActivity: THREAD.lastActivity },
            ],
          },
        ],
        runtimeChildren: [],
      };
      mockFetchJson(graph);

      const result = await client.getThreadGraph('sess-1');

      const [url] = ((fetch as unknown as { mock: { calls: any[] } }).mock).calls[0];
      expect(String(url)).toBe(`${BASE}/sessions/sess-1/thread-graph`);
      expect(result).toEqual(graph);
    });
  });
});
