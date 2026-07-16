import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentClient } from '../src/client.js';
import { EventTypes } from '../src/types/events.js';

function okStream(...events: object[]): Response {
  return {
    ok: true,
    body: new ReadableStream({
      start(controller) {
        controller.enqueue(new TextEncoder().encode(events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join('')));
        controller.close();
      },
    }),
    text: async () => '',
  } as Response;
}

describe('ChatSession', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('opens an existing session from search metadata', async () => {
    const client = new AgentClient('http://localhost:5135');
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ id: 's1', createdAt: '', lastActivity: '', metadata: {} }],
        text: async () => '',
      } as Response);

    const chat = await client.chat.open({
      agentId: 'a1',
      session: { search: { metadata: { project: 'p1' } } },
    });
    expect(chat.sessionId).toBe('s1');
    chat.dispose();
  });

  it('submits a user message through the client and leaves live output to subscriptions', async () => {
    const client = new AgentClient('http://localhost:5135');
    const events: unknown[] = [];
    client.onAny((event) => {
      events.push(event);
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: null,
      text: async () => JSON.stringify({
        runtimeRunId: 'run-1',
        startedAt: '2026-07-15T00:00:00Z',
      }),
    } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const submission = await chat.submitMessage({ contents: [{ $type: 'text', text: 'hello' }] });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/inputs',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(submission.runtimeRunId).toBe('run-1');
    expect(events).toEqual([]);
    chat.dispose();
  });

  it('reads one authoritative thread state through the scoped chat session', async () => {
    const client = new AgentClient('http://localhost:5135');
    const state = {
      observedHead: 8,
      activeRun: {
        runtimeRunId: 'run-1',
        agentId: 'a1',
        sessionId: 's1',
        threadId: 'main',
        status: 'active',
        startedAt: '2026-05-28T00:00:00Z',
        backgroundTasks: [],
        backgroundHandles: [],
      },
    };
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => state,
      text: async () => '',
    } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const result = await chat.getState();

    expect(result).toEqual(state);
    expect(globalThis.fetch).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/state',
      expect.objectContaining({ method: 'GET' }),
    );
    chat.dispose();
  });

  it('cancels by comparing the authoritative active run ID', async () => {
    const client = new AgentClient('http://localhost:5135');
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          observedHead: 8,
          activeRun: {
            runtimeRunId: 'run-1',
            agentId: 'a1',
            sessionId: 's1',
            threadId: 'main',
            status: 'active',
            startedAt: '2026-05-28T00:00:00Z',
            backgroundTasks: [],
            backgroundHandles: [],
          },
        }),
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        text: async () => JSON.stringify({ status: 'accepted', activeRun: null }),
      } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const result = await chat.cancelActiveTurn({ reason: 'stop' });

    expect(result.status).toBe('accepted');
    expect(fetchSpy).toHaveBeenLastCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/interrupt',
      expect.objectContaining({
        body: expect.stringContaining('"expectedRuntimeRunId":"run-1"'),
      }),
    );
    chat.dispose();
  });

  it('hydrates control state then replays from the applied cursor', async () => {
    const client = new AgentClient('http://localhost:5135');
    const state = { observedHead: 9, activeRun: null };
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, json: async () => state } as Response)
      .mockResolvedValueOnce({
        ok: true,
        body: new ReadableStream({ start(controller) { controller.close(); } }),
        text: async () => '',
      } as Response);
    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });

    const hydrated = await chat.subscribeLive();

    expect(hydrated).toEqual(state);
    expect(fetchSpy.mock.calls.map(([url]) => String(url))).toEqual([
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/state',
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=0',
    ]);
    await chat.disconnectLive();
  });
});
