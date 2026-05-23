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

  it('posts text input to the scoped stream endpoint and emits parsed events', async () => {
    const events: unknown[] = [];
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent((event) => events.push(event));

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: stream({ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }),
      text: async () => '',
    } as Response);

    await transport.run({
      type: EventTypes.USER_TEXT_INPUT,
      sessionId: 's1',
      agentId: 'a1',
      branchId: 'main',
      text: 'Hi',
      runConfig: { modelId: 'm' },
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/branches/main/stream',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ text: 'Hi', runConfig: { modelId: 'm' } }),
      }),
    );
    expect(events).toEqual([{ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }]);
  });

  it('posts bidirectional responses to their response endpoints after scope is known', async () => {
    const transport = new SseTransport('http://localhost:5135');
    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      text: async () => '',
    } as Response);

    await transport.run({
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
    await transport.connect({ agentId: 'a1', sessionId: 's1', branchId: 'main' });
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => null,
    } as Response);

    await expect(transport.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    })).rejects.toMatchObject({
      name: 'AgentError',
      code: 'STALE_RESPONSE',
    } satisfies Partial<AgentError>);
  });

  it('disconnects an active stream', async () => {
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

    const run = transport.run({
      type: EventTypes.USER_TEXT_INPUT,
      sessionId: 's1',
      agentId: 'a1',
      branchId: 'main',
      text: 'Hi',
    });

    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(transport.connected).toBe(true);
    transport.disconnect();
    streamController!.close();
    await run;
    expect(transport.connected).toBe(false);
  });
});
