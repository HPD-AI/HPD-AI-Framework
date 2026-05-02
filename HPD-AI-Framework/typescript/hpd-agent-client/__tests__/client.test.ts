import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentClient } from '../src/client.js';
import { EventTypes } from '../src/types/events.js';

function sseStream(...events: object[]): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      const payload = events.map((event) => `data: ${JSON.stringify(event)}\n\n`).join('');
      controller.enqueue(new TextEncoder().encode(payload));
      controller.close();
    },
  });
}

function okStream(...events: object[]): Response {
  return {
    ok: true,
    body: sseStream(...events),
    text: async () => '',
  } as Response;
}

describe('AgentClient', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('dispatches exact typed handlers before onAny handlers', async () => {
    const order: string[] = [];
    const client = new AgentClient('http://localhost:5135');

    client.on(EventTypes.TEXT_DELTA, (event) => {
      order.push(`typed:${event.text}`);
    });
    client.onAny((event) => {
      order.push(`any:${event.type}`);
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream(
      { version: '1.0', type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'msg-1' },
      {
        version: '1.0',
        type: EventTypes.MESSAGE_TURN_FINISHED,
        messageTurnId: 'turn-1',
        conversationId: 'conv-1',
        agentName: 'TestAgent',
        duration: '00:00:01',
        timestamp: '2024-01-01T00:00:00Z',
      }
    ));

    await client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hi',
      sessionId: 'session-123',
      branchId: 'main',
    });

    expect(order).toEqual([
      'typed:Hello',
      'any:TEXT_DELTA',
      'any:MESSAGE_TURN_FINISHED',
    ]);
  });

  it('disposes typed and onAny subscriptions', async () => {
    const typed = vi.fn();
    const any = vi.fn();
    const client = new AgentClient('http://localhost:5135');

    const typedSub = client.on(EventTypes.TEXT_DELTA, typed);
    const anySub = client.onAny(any);
    typedSub.dispose();
    anySub.dispose();

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream(
      { version: '1.0', type: EventTypes.TEXT_DELTA, text: 'Hello', messageId: 'msg-1' }
    ));

    await client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hi',
      sessionId: 'session-123',
      branchId: 'main',
    });

    expect(typed).not.toHaveBeenCalled();
    expect(any).not.toHaveBeenCalled();
  });

  it('posts USER_TEXT_INPUT events to the scoped SSE stream endpoint', async () => {
    const client = new AgentClient('http://localhost:5135');
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream());

    const runConfig = { providerKey: 'anthropic', modelId: 'claude-sonnet-4-6' };
    await client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hello',
      sessionId: 'session-123',
      branchId: 'main',
      agentId: 'agent-1',
      runConfig,
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/sessions/session-123/branches/main/stream',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          type: EventTypes.USER_TEXT_INPUT,
          text: 'Hello',
          sessionId: 'session-123',
          branchId: 'main',
          agentId: 'agent-1',
          runConfig,
        }),
      })
    );
  });

  it('routes permission response inputs through run()', async () => {
    const client = new AgentClient('http://localhost:5135');
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      text: async () => '',
    } as Response);

    await client.start({ sessionId: 'session-123', branchId: 'main' });
    await client.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'perm-1',
      sourceName: 'PermissionMiddleware',
      approved: true,
      choice: 'allow_once',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/sessions/session-123/branches/main/permissions/respond',
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('dispatches transport errors through onError handlers', async () => {
    const client = new AgentClient('http://localhost:5135');
    const errors: string[] = [];
    client.onError((error) => errors.push(error.message));

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: new ReadableStream({
        start(controller) {
          controller.error(new Error('stream broke'));
        },
      }),
      text: async () => '',
    } as Response);

    await client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hi',
      sessionId: 'session-123',
      branchId: 'main',
    });

    expect(errors).toEqual(['stream broke']);
  });

  it('auto responds to client tool invoke requests when configured', async () => {
    const client = new AgentClient({
      baseUrl: 'http://localhost:5135',
      onClientToolInvoke: async (request) => ({
        requestId: request.requestId,
        success: true,
        content: [{ type: 'text', text: 'done' }],
      }),
    });

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(okStream({
        version: '1.0',
        type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
        requestId: 'req-1',
        toolName: 'browser.echo',
        arguments: {},
      }))
      .mockResolvedValueOnce({
        ok: true,
        text: async () => '',
      } as Response);

    await client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'invoke',
      sessionId: 'session-123',
      branchId: 'main',
    });

    expect(fetchSpy).toHaveBeenLastCalledWith(
      'http://localhost:5135/sessions/session-123/branches/main/client-tools/respond',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          type: EventTypes.CLIENT_TOOL_INVOKE_RESPONSE,
          requestId: 'req-1',
          content: [{ type: 'text', text: 'done' }],
          success: true,
        }),
      })
    );
  });

  it('uses WebSocket transport when specified', () => {
    const client = new AgentClient({
      baseUrl: 'http://localhost:5135',
      transport: 'websocket',
    });

    expect((client as any).transport.constructor.name).toBe('WebSocketTransport');
  });

  it('uses SSE transport by default', () => {
    const client = new AgentClient('http://localhost:5135');

    expect((client as any).transport.constructor.name).toBe('SseTransport');
  });

  it('aborts an active run', async () => {
    const client = new AgentClient('http://localhost:5135');

    let streamController: ReadableStreamDefaultController<Uint8Array>;
    const mockStream = new ReadableStream({
      start(controller) {
        streamController = controller;
      },
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, options) => {
      options?.signal?.addEventListener('abort', () => {
        streamController.close();
      });
      return {
        ok: true,
        body: mockStream,
        text: async () => '',
      } as Response;
    });

    const runPromise = client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hi',
      sessionId: 'session-123',
      branchId: 'main',
    });

    await new Promise((resolve) => setTimeout(resolve, 10));
    client.abort();

    await expect(runPromise).resolves.toBeUndefined();
  });

  it('reports streaming state from transport connection state', async () => {
    const client = new AgentClient('http://localhost:5135');

    expect(client.streaming).toBe(false);

    const mockStream = new ReadableStream({
      start(controller) {
        setTimeout(() => controller.close(), 50);
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    const runPromise = client.run({
      type: EventTypes.USER_TEXT_INPUT,
      text: 'Hi',
      sessionId: 'session-123',
      branchId: 'main',
    });

    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(client.streaming).toBe(true);

    await runPromise;
    expect(client.streaming).toBe(false);
  });
});
