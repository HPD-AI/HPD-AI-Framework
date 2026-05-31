import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentError } from '../src/errors.js';
import { SseTransport } from '../src/transports/sse.js';
import { EventTypes } from '../src/types/events.js';

function stream(...events: object[]): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join('')));
      controller.close();
    },
  });
}

describe('SseTransport runtime', () => {
  beforeEach(() => vi.resetAllMocks());
  afterEach(() => vi.restoreAllMocks());

  it('connects to the scoped live events endpoint and emits parsed events', async () => {
    const events: unknown[] = [];
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent((event) => events.push(event));

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: stream({ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }),
      text: async () => '',
    } as Response);

    await transport.connect({ sessionId: 's1', agentId: 'a1', branchId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/branches/main/events/live',
      expect.objectContaining({
        method: 'GET',
      }),
    );
    expect(events).toEqual([{ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }]);
  });

  it('submits text input to the scoped inputs endpoint', async () => {
    const transport = new SseTransport('http://localhost:5135');

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: null,
      text: async () => '',
    } as Response);

    await transport.submitInput({
      type: EventTypes.USER_TEXT_INPUT,
      sessionId: 's1',
      agentId: 'a1',
      branchId: 'main',
      text: 'Hi',
      runConfig: { modelId: 'm' },
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/branches/main/inputs',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ text: 'Hi', runConfig: { modelId: 'm' } }),
      }),
    );
  });

  it('posts bidirectional responses to their response endpoints after scope is known', async () => {
    const transport = new SseTransport('http://localhost:5135');

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, body: stream(), text: async () => '' } as Response)
      .mockResolvedValueOnce({
        ok: true,
        text: async () => '',
      } as Response);

    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });

    await transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/branches/main/permissions/respond',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('surfaces stale response conflicts as AgentError', async () => {
    const transport = new SseTransport('http://localhost:5135');
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, body: stream(), text: async () => '' } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        json: async () => null,
      } as Response);

    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });

    await expect(transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    })).rejects.toMatchObject({
      name: 'AgentError',
      code: 'STALE_RESPONSE',
    } satisfies Partial<AgentError>);
  });

  it('disconnects an active live subscription', async () => {
    const transport = new SseTransport('http://localhost:5135');
    let streamController: ReadableStreamDefaultController<Uint8Array>;
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: new ReadableStream({
        start(controller) {
          streamController = controller;
        },
      }),
      text: async () => '',
    } as Response);

    await transport.connect({ sessionId: 's1', agentId: 'a1', branchId: 'main' });

    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(transport.connected).toBe(true);
    transport.disconnect();
    streamController!.close();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(transport.connected).toBe(false);
  });
});
