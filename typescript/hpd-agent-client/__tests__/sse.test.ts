import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { SseTransport, ThreadJournalRebasedError } from '../src/transports/sse.js';
import { EventTypes } from '../src/types/events.js';

function stream(...events: object[]): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(events
        .map((event, index) => `id: 1:${index + 1}\ndata: ${JSON.stringify(event)}\n\n`)
        .join('')));
      controller.close();
    },
  });
}

function committedStream(sequenceNumber: number, event: object): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      controller.enqueue(new TextEncoder().encode(
        `id: 1:${sequenceNumber}\ndata: ${JSON.stringify(event)}\n\n`,
      ));
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

    await transport.connect({ sessionId: 's1', agentId: 'a1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=1:0',
      expect.objectContaining({
        method: 'GET',
      }),
    );
    expect(events).toEqual([{ type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'm1' }]);
    transport.disconnect();
  });

  it('submits message input to the scoped inputs endpoint', async () => {
    const transport = new SseTransport('http://localhost:5135');

    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: null,
      text: async () => JSON.stringify({
        disposition: 'queued',
        threadExecutionId: 'run-1',
        startedAt: '2026-07-15T00:00:00Z',
      }),
    } as Response);

    const result = await transport.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      sessionId: 's1',
      agentId: 'a1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Hi' }],
      }],
      runConfig: { modelId: 'm' },
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/inputs',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          type: EventTypes.USER_MESSAGES_INPUT,
          sessionId: 's1',
          agentId: 'a1',
          threadId: 'main',
          messages: [{
            role: 'user',
            contents: [{ $type: 'text', text: 'Hi' }],
          }],
          runConfig: { modelId: 'm' },
        }),
      }),
    );
    expect(result).toEqual({
      disposition: 'queued',
      threadExecutionId: 'run-1',
      startedAt: '2026-07-15T00:00:00Z',
      activeExecution: null,
    });
  });

  it('posts request responses to their response endpoints after scope is known', async () => {
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent(() => {});

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, body: stream(), text: async () => '' } as Response)
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          status: 'Accepted',
          requestId: 'p1',
          accepted: true,
        }),
      } as Response);

    await transport.connect({ agentId: 'a1', sessionId: 's1', threadId: 'main' });

    const result = await transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/responses',
      expect.objectContaining({ method: 'POST' }),
    );
    expect(result).toEqual({
      status: 'accepted',
      requestId: 'p1',
      message: null,
      accepted: true,
    });
    transport.disconnect();
  });

  it('returns stale response conflicts as structured response results', async () => {
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent(() => {});
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, body: stream(), text: async () => '' } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        json: async () => null,
      } as Response);

    await transport.connect({ agentId: 'a1', sessionId: 's1', threadId: 'main' });

    await expect(transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    })).resolves.toEqual({
      status: 'alreadyResolved',
      requestId: 'p1',
      message: 'Response was not accepted because the request is no longer pending',
      accepted: false,
    });
    transport.disconnect();
  });

  it('returns server-provided middleware response conflict results', async () => {
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent(() => {});
    vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({ ok: true, body: stream(), text: async () => '' } as Response)
      .mockResolvedValueOnce({
        ok: false,
        status: 409,
        json: async () => ({
          title: 'Thread runtime is not active',
          errors: {
            ThreadRuntimeNotActive: ['The thread exists, but no runtime is waiting for this response.'],
          },
        }),
      } as Response);

    await transport.connect({ agentId: 'a1', sessionId: 's1', threadId: 'main' });

    await expect(transport.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    })).resolves.toEqual({
      status: 'notFound',
      requestId: 'p1',
      message: 'The thread exists, but no runtime is waiting for this response.',
      accepted: false,
    });
    transport.disconnect();
  });

  it('disconnects an active live subscription', async () => {
    const transport = new SseTransport('http://localhost:5135');
    transport.onEvent(() => {});
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

    await transport.connect({ sessionId: 's1', agentId: 'a1', threadId: 'main' });

    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(transport.connected).toBe(true);
    transport.disconnect();
    streamController!.close();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(transport.connected).toBe(false);
  });

  it('reconnects after EOF using the last acknowledged committed sequence', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const events: string[] = [];
    transport.onEvent((event) => {
      events.push((event as { text: string }).text);
      if (events.length === 2) transport.disconnect();
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        body: committedStream(5, { type: EventTypes.TEXT_DELTA, text: 'first', messageId: 'm1' }),
        text: async () => '',
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        body: committedStream(6, { type: EventTypes.TEXT_DELTA, text: 'second', messageId: 'm1' }),
        text: async () => '',
      } as Response);

    await transport.connect({
      sessionId: 's1',
      agentId: 'a1',
      threadId: 'main',
      after: { generation: 1, sequenceNumber: 4 },
    });
    await vi.waitFor(() => expect(events).toEqual(['first', 'second']), { timeout: 2_000 });

    expect(fetchSpy.mock.calls.map(([url]) => String(url))).toEqual([
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=1:4',
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=1:5',
    ]);
  });

  it('delivers live events without advancing the reconnect cursor', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const events: string[] = [];
    transport.onEvent((event) => {
      events.push((event as { text: string }).text);
      if (events.length === 2) transport.disconnect();
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        body: new ReadableStream({
          start(controller) {
            controller.enqueue(new TextEncoder().encode(
              'event: live-agent-event\n' +
              `data: ${JSON.stringify({ type: EventTypes.TEXT_DELTA, text: 'live' })}\n\n`,
            ));
            controller.close();
          },
        }),
        text: async () => '',
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        body: committedStream(5, { type: EventTypes.TEXT_DELTA, text: 'committed' }),
        text: async () => '',
      } as Response);

    await transport.connect({
      sessionId: 's1',
      agentId: 'a1',
      threadId: 'main',
      after: { generation: 1, sequenceNumber: 4 },
    });
    await vi.waitFor(() => expect(events).toEqual(['live', 'committed']), { timeout: 2_000 });

    expect(fetchSpy.mock.calls.map(([url]) => String(url))).toEqual([
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=1:4',
      'http://localhost:5135/agents/a1/sessions/s1/threads/main/events?after=1:4',
    ]);
  });

  it('does not dispatch the next event until the previous event is acknowledged', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const events: string[] = [];
    let acknowledgeFirst!: () => void;
    const firstAcknowledged = new Promise<void>((resolve) => {
      acknowledgeFirst = resolve;
    });
    transport.onEvent(async (event) => {
      events.push((event as { text: string }).text);
      if (events.length === 1) await firstAcknowledged;
    });
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
      ok: true,
      body: new ReadableStream({
        start(controller) {
          controller.enqueue(new TextEncoder().encode(
            `id: 1:1\ndata: ${JSON.stringify({ type: EventTypes.TEXT_DELTA, text: 'first' })}\n\n` +
            `id: 1:2\ndata: ${JSON.stringify({ type: EventTypes.TEXT_DELTA, text: 'second' })}\n\n`,
          ));
          controller.close();
        },
      }),
      text: async () => '',
    } as Response);

    await transport.connect({ sessionId: 's1', agentId: 'a1', threadId: 'main' });
    await vi.waitFor(() => expect(events).toEqual(['first']));
    acknowledgeFirst();
    await vi.waitFor(() => expect(events).toEqual(['first', 'second']));
    transport.disconnect();
  });

  it('stops stale observation and reports a journal rebase control result', async () => {
    const transport = new SseTransport('http://localhost:5135');
    const errors: Error[] = [];
    transport.onEvent(() => undefined);
    transport.onError((error) => errors.push(error));
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: new ReadableStream({
        start(controller) {
          controller.enqueue(new TextEncoder().encode(
            'event: thread-journal-rebased\n' +
            'data: {"previousGeneration":1,"currentGeneration":2}\n\n',
          ));
          controller.close();
        },
      }),
      text: async () => '',
    } as Response);

    await transport.connect({
      sessionId: 's1',
      agentId: 'a1',
      threadId: 'main',
      after: { generation: 1, sequenceNumber: 9 },
    });

    await vi.waitFor(() => expect(errors).toHaveLength(1));
    expect(errors[0]).toBeInstanceOf(ThreadJournalRebasedError);
    expect(errors[0]).toMatchObject({ previousGeneration: 1, currentGeneration: 2 });
    expect(transport.connected).toBe(false);
  });
});
