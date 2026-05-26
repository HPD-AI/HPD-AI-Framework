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

  it('opens an existing session from search metadata and reads branch messages', async () => {
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
          id: 'm1',
          role: 'assistant',
          timestamp: '2026-01-01T00:00:00Z',
          contents: [{ $type: 'text', text: 'history' }],
        }],
        text: async () => '',
      } as Response);

    const chat = await client.chat.open({
      agentId: 'a1',
      session: { search: { metadata: { project: 'p1' } } },
    });
    const messages = await chat.getBranchMessages();

    expect(chat.sessionId).toBe('s1');
    expect(messages).toEqual([
      expect.objectContaining({ id: 'm1', role: 'assistant' }),
    ]);
    chat.dispose();
  });

  it('sends text through the client and leaves streamed output to event handlers', async () => {
    const client = new AgentClient('http://localhost:5135');
    const events: unknown[] = [];
    client.onAny((event) => {
      events.push(event);
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream(
      { type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'response' },
    ));

    const chat = client.chat.session({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    await chat.sendText('hello');

    expect(events).toEqual([
      expect.objectContaining({ type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'response' }),
    ]);
    chat.dispose();
  });
});
