/**
 * Unit tests for AgentClient agent definition CRUD methods.
 *
 * What these tests cover:
 *   listAgents, getAgent, createAgent, updateAgent, deleteAgent — one-line
 *   passthroughs to the underlying AgentTransport. The tests verify:
 *     1. The correct HTTP method and URL are called.
 *     2. The request body (where applicable) carries the right payload.
 *     3. The return value is the parsed JSON the server sent back.
 *     4. getAgent returns null on 404.
 *     5. Void-returning deleteAgent resolves without a value.
 *
 * Test type: unit — all network I/O is replaced by vi.spyOn(globalThis, 'fetch').
 * Transport under test: SseTransport (default).
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { AgentClient } from '../src/client.js';
import type { AgentSummaryDto, StoredAgentDto, CreateAgentRequest } from '../src/types/agent.js';

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

function mockFetchEmpty(status = 204) {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: true,
    status,
    json: async () => undefined,
    text: async () => '',
  } as Response);
}

function mockFetch404() {
  vi.spyOn(globalThis, 'fetch').mockResolvedValue({
    ok: false,
    status: 404,
    json: async () => null,
    text: async () => '',
  } as Response);
}

const BASE = 'http://localhost:5135';

// Minimal fixtures
const SUMMARY: AgentSummaryDto = {
  id: 'agent-1',
  name: 'Test Agent',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

const STORED: StoredAgentDto = {
  id: 'agent-1',
  name: 'Test Agent',
  config: { name: 'Test Agent', maxAgenticIterations: 10 },
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AgentClient — agent definition CRUD', () => {
  let client: AgentClient;

  beforeEach(() => {
    vi.resetAllMocks();
    client = new AgentClient(BASE);
  });

  // ── listAgents ────────────────────────────────────────────────────────────

  it('listAgents: GET /agents, returns AgentSummaryDto[]', async () => {
    mockFetchJson([SUMMARY]);

    const result = await client.listAgents();

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/agents`);
    expect(init.method).toBe('GET');
    expect(result).toEqual([SUMMARY]);
  });

  it('listAgents: returns empty array when no agents exist', async () => {
    mockFetchJson([]);
    const result = await client.listAgents();
    expect(result).toEqual([]);
  });

  // ── getAgent ──────────────────────────────────────────────────────────────

  it('getAgent: GET /agents/{id}, returns StoredAgentDto', async () => {
    mockFetchJson(STORED);

    const result = await client.getAgent('agent-1');

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/agents/agent-1`);
    expect(init.method).toBe('GET');
    expect(result).toEqual(STORED);
  });

  it('getAgent: returns null on 404', async () => {
    mockFetch404();
    const result = await client.getAgent('nonexistent');
    expect(result).toBeNull();
  });

  // ── createAgent ───────────────────────────────────────────────────────────

  it('createAgent: POST /agents with body, returns StoredAgentDto', async () => {
    mockFetchJson(STORED, 201);

    const request: CreateAgentRequest = {
      name: 'Test Agent',
      config: { name: 'Test Agent', maxAgenticIterations: 10 },
    };
    const result = await client.createAgent(request);

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/agents`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual(request);
    expect(result).toEqual(STORED);
  });

  it('createAgent: includes metadata when provided', async () => {
    mockFetchJson(STORED, 201);

    const request: CreateAgentRequest = {
      name: 'Test Agent',
      config: { name: 'Test Agent' },
      metadata: { tenantId: 'acme' },
    };
    await client.createAgent(request);

    const [, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    const body = JSON.parse(init.body as string);
    expect(body.metadata).toEqual({ tenantId: 'acme' });
  });

  // ── updateAgent ───────────────────────────────────────────────────────────

  it('updateAgent: PUT /agents/{id} with body, returns updated StoredAgentDto', async () => {
    const updated = { ...STORED, config: { name: 'Updated', maxAgenticIterations: 99 } };
    mockFetchJson(updated);

    const result = await client.updateAgent('agent-1', { config: { name: 'Updated', maxAgenticIterations: 99 } });

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/agents/agent-1`);
    expect(init.method).toBe('PUT');
    expect(JSON.parse(init.body as string)).toEqual({ config: { name: 'Updated', maxAgenticIterations: 99 } });
    expect(result).toEqual(updated);
  });

  it('updateAgent: throws on 404', async () => {
    mockFetch404();
    await expect(client.updateAgent('missing', { config: {} })).rejects.toThrow('Agent not found: missing');
  });

  // ── deleteAgent ───────────────────────────────────────────────────────────

  it('deleteAgent: DELETE /agents/{id}, resolves void', async () => {
    mockFetchEmpty(204);

    await expect(client.deleteAgent('agent-1')).resolves.toBeUndefined();

    const [url, init] = vi.mocked(fetch).mock.calls[0] as [string, RequestInit];
    expect(url).toBe(`${BASE}/agents/agent-1`);
    expect(init.method).toBe('DELETE');
  });

  it('deleteAgent: throws on 404', async () => {
    mockFetch404();
    await expect(client.deleteAgent('missing')).rejects.toThrow('Agent not found: missing');
  });
});
