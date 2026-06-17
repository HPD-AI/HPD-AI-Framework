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

  it('opens an existing session from search metadata and reads thread events', async () => {
    const client = new AgentClient('http://localhost:5135');
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{ id: 's1', createdAt: '', lastActivity: '', metadata: {} }],
        text: async () => '',
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{
          eventId: 'evt-1',
          sessionId: 's1',
          threadId: 'main',
          type: EventTypes.TEXT_DELTA,
          messageId: 'm1',
          text: 'history',
          sequenceNumber: 1,
          timestamp: '2026-01-01T00:00:00Z',
        }],
        text: async () => '',
      } as Response);

    const chat = await client.chat.open({
      agentId: 'a1',
      session: { search: { metadata: { project: 'p1' } } },
    });
    const events = await chat.getThreadEvents();

    expect(chat.sessionId).toBe('s1');
    expect(events).toEqual([
      expect.objectContaining({ eventId: 'evt-1', type: EventTypes.TEXT_DELTA }),
    ]);
    chat.dispose();
  });

  it('reads thread events through the scoped chat session', async () => {
    const client = new AgentClient('http://localhost:5135');
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        json: async () => [{
          eventId: 'evt-1',
          sessionId: 's1',
          threadId: 'main',
          type: EventTypes.TEXT_DELTA,
          messageId: 'm1',
          text: 'history',
          sequenceNumber: 1,
          timestamp: '2026-01-01T00:00:00Z',
        }],
        text: async () => '',
      } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const events = await chat.getThreadEvents();

    expect(events).toEqual([
      expect.objectContaining({ eventId: 'evt-1', type: EventTypes.TEXT_DELTA }),
    ]);
    chat.dispose();
  });

  it('submits text through the client and leaves live output to subscriptions', async () => {
    const client = new AgentClient('http://localhost:5135');
    const events: unknown[] = [];
    client.onAny((event) => {
      events.push(event);
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: null,
      text: async () => '',
    } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    await chat.submitText('hello');

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/inputs',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(events).toEqual([]);
    chat.dispose();
  });

  it('reads the active thread run through the scoped chat session', async () => {
    const client = new AgentClient('http://localhost:5135');
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      json: async () => ({
        runtimeRunId: 'run-1',
        agentId: 'a1',
        sessionId: 's1',
        threadId: 'main',
        status: 'active',
        startedAt: '2026-05-28T00:00:00Z',
        backgroundTasks: [],
      }),
      text: async () => '',
    } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const activeRun = await chat.getActiveRun();

    expect(activeRun?.runtimeRunId).toBe('run-1');
    expect(globalThis.fetch).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/runs/active',
      expect.objectContaining({ method: 'GET' }),
    );
    chat.dispose();
  });

  it('treats an empty active thread run response as no active run', async () => {
    const client = new AgentClient('/api/hpd-agent');
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      text: async () => 'null',
    } as Response);

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', threadId: 'main' });
    const activeRun = await chat.getActiveRun();

    expect(activeRun).toBeNull();
    expect(globalThis.fetch).toHaveBeenCalledWith(
      '/api/hpd-agent/agents/a1/sessions/s1/threads/main/runs/active',
      expect.objectContaining({ method: 'GET' }),
    );
    chat.dispose();
  });
});
