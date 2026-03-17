import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { AgentClient } from '../src/client.js';
import { EventTypes } from '../src/types/events.js';

describe('AgentClient', () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('should stream events with typed handlers', async () => {
    const textDeltas: string[] = [];
    const client = new AgentClient('http://localhost:5135');

    // Mock the transport
    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n' +
              'data: {"version":"1.0","type":"TEXT_DELTA","text":" World","messageId":"msg-1"}\n\n' +
              'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onTextDelta: (text) => textDeltas.push(text),
    });

    expect(textDeltas).toEqual(['Hello', ' World']);
  });

  it('should call onComplete when MESSAGE_TURN_FINISHED is received', async () => {
    const client = new AgentClient('http://localhost:5135');
    const completeHandler = vi.fn();

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onComplete: completeHandler,
    });

    expect(completeHandler).toHaveBeenCalled();
  });

  it('should handle permission requests', async () => {
    const client = new AgentClient('http://localhost:5135');

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"PERMISSION_REQUEST","permissionId":"p1","sourceName":"test","functionName":"read_file","description":"Read a file","callId":"c1","arguments":{}}\n\n'
          )
        );
        controller.close();
      },
    });

    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce({
        ok: true,
        body: mockStream,
        text: async () => '',
      } as Response)
      .mockResolvedValueOnce({
        ok: true,
        text: async () => '',
      } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onPermissionRequest: async (request) => {
        expect(request.functionName).toBe('read_file');
        return { approved: true, choice: 'allow_always' };
      },
    });

    // Wait for async operations
    await new Promise((r) => setTimeout(r, 50));

    // Verify permission response was sent
    expect(fetchSpy).toHaveBeenCalledTimes(2);
    expect(fetchSpy).toHaveBeenLastCalledWith(
      'http://localhost:5135/sessions/session-123/branches/main/permissions/respond',
      expect.objectContaining({
        method: 'POST',
      })
    );
  });

  it('should use WebSocket transport when specified', () => {
    const client = new AgentClient({
      baseUrl: 'http://localhost:5135',
      transport: 'websocket',
    });

    expect((client as any).transport.constructor.name).toBe('WebSocketTransport');
  });

  it('should use SSE transport by default', () => {
    const client = new AgentClient('http://localhost:5135');

    expect((client as any).transport.constructor.name).toBe('SseTransport');
  });

  it('should handle tool call events', async () => {
    const client = new AgentClient('http://localhost:5135');
    const toolCalls: { callId: string; name: string }[] = [];

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"TOOL_CALL_START","callId":"call-1","name":"get_weather","messageId":"msg-1"}\n\n' +
              'data: {"version":"1.0","type":"TOOL_CALL_ARGS","callId":"call-1","argsJson":"{\\"city\\":\\"NYC\\"}"}\n\n' +
              'data: {"version":"1.0","type":"TOOL_CALL_END","callId":"call-1"}\n\n' +
              'data: {"version":"1.0","type":"TOOL_CALL_RESULT","callId":"call-1","result":"72F and sunny"}\n\n' +
              'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onToolCallStart: (callId, name) => toolCalls.push({ callId, name }),
    });

    expect(toolCalls).toEqual([{ callId: 'call-1', name: 'get_weather' }]);
  });

  it('should handle reasoning events', async () => {
    const client = new AgentClient('http://localhost:5135');
    const reasoningTexts: string[] = [];

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"REASONING_DELTA","messageId":"msg-1","text":"Let me think about this..."}\n\n' +
              'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onReasoning: (text) => reasoningTexts.push(text),
    });

    expect(reasoningTexts).toEqual(['Let me think about this...']);
  });

  it('should handle agent turn events', async () => {
    const client = new AgentClient('http://localhost:5135');
    const turns: { start: number[]; end: number[] } = { start: [], end: [] };

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"AGENT_TURN_STARTED","iteration":0}\n\n' +
              'data: {"version":"1.0","type":"AGENT_TURN_FINISHED","iteration":0}\n\n' +
              'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onTurnStart: (iteration) => turns.start.push(iteration),
      onTurnEnd: (iteration) => turns.end.push(iteration),
    });

    expect(turns.start).toEqual([0]);
    expect(turns.end).toEqual([0]);
  });

  it('should handle MESSAGE_TURN_ERROR', async () => {
    const client = new AgentClient('http://localhost:5135');
    const errorHandler = vi.fn();

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"MESSAGE_TURN_ERROR","message":"Something went wrong"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await expect(
      client.stream('session-123', 'main', [{ content: 'Hi' }], {
        onError: errorHandler,
      })
    ).rejects.toThrow('Something went wrong');

    expect(errorHandler).toHaveBeenCalledWith('Something went wrong');
  });

  it('should call onEvent for every event', async () => {
    const client = new AgentClient('http://localhost:5135');
    const allEvents: any[] = [];

    const mockStream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          new TextEncoder().encode(
            'data: {"version":"1.0","type":"TEXT_DELTA","text":"Hello","messageId":"msg-1"}\n\n' +
              'data: {"version":"1.0","type":"MESSAGE_TURN_FINISHED","messageTurnId":"turn-1","conversationId":"conv-1","agentName":"TestAgent","duration":"00:00:01","timestamp":"2024-01-01T00:00:00Z"}\n\n'
          )
        );
        controller.close();
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    await client.stream('session-123', 'main', [{ content: 'Hi' }], {
      onEvent: (event) => allEvents.push(event),
    });

    expect(allEvents).toHaveLength(2);
    expect(allEvents.map((e) => e.type)).toEqual([
      EventTypes.TEXT_DELTA,
      EventTypes.MESSAGE_TURN_FINISHED,
    ]);
  });

  it('should abort streaming', async () => {
    const client = new AgentClient('http://localhost:5135');
    const controller = new AbortController();

    // Create a stream that responds to abort
    let streamController: ReadableStreamDefaultController<Uint8Array>;
    const mockStream = new ReadableStream({
      start(ctrl) {
        streamController = ctrl;
      },
      cancel() {
        // Called when aborted
      },
    });

    vi.spyOn(globalThis, 'fetch').mockImplementation(async (_url, options) => {
      // Listen for abort and close stream
      options?.signal?.addEventListener('abort', () => {
        try {
          streamController?.close();
        } catch {
          // Stream may already be closed
        }
      });
      return {
        ok: true,
        body: mockStream,
        text: async () => '',
      } as Response;
    });

    const streamPromise = client.stream('session-123', 'main', [{ content: 'Hi' }], {}, { signal: controller.signal });

    // Abort after a short delay
    setTimeout(() => controller.abort(), 10);

    // Should resolve (not reject) on abort
    await expect(streamPromise).resolves.toBeUndefined();
  }, 10000);

  it('should report streaming state correctly', async () => {
    const client = new AgentClient('http://localhost:5135');

    expect(client.streaming).toBe(false);

    const mockStream = new ReadableStream({
      start(controller) {
        // Delay closing to check streaming state
        setTimeout(() => controller.close(), 50);
      },
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      body: mockStream,
      text: async () => '',
    } as Response);

    const streamPromise = client.stream('session-123', 'main', [{ content: 'Hi' }], {});

    // Wait for connection
    await new Promise((r) => setTimeout(r, 10));
    expect(client.streaming).toBe(true);

    await streamPromise;
    expect(client.streaming).toBe(false);
  });
});
