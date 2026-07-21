import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AgentClient } from '../src/client.js';
import { EventTypes } from '../src/types/events.js';

function sseStream(...events: object[]): ReadableStream<Uint8Array> {
  return new ReadableStream({
    start(controller) {
      const payload = events
        .map((event, index) => `id: 1:${index + 1}\ndata: ${JSON.stringify(event)}\n\n`)
        .join('');
      controller.enqueue(new TextEncoder().encode(payload));
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

function okSubmission(): Response {
  return {
    ok: true,
    body: null,
    text: async () => JSON.stringify({
      disposition: 'queued',
      threadExecutionId: 'run-1',
      startedAt: '2026-07-15T00:00:00Z',
    }),
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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(order).toEqual([
      'typed:Hello',
      'any:TEXT_DELTA',
      'any:MESSAGE_TURN_FINISHED',
    ]);
  });

  it('preserves unknown custom events for onAny handlers', async () => {
    const events: unknown[] = [];
    const client = new AgentClient('http://localhost:5135');
    client.onAny((event) => events.push(event));

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream({
      version: '1.0',
      type: 'EXECUTE_COMMAND_PROCESS_EXITED',
      toolCallId: 'call-1',
      command: 'bun test',
      exitCode: 0,
      durationMilliseconds: 321,
    }));

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(events).toEqual([{
      version: '1.0',
      type: 'EXECUTE_COMMAND_PROCESS_EXITED',
      toolCallId: 'call-1',
      command: 'bun test',
      exitCode: 0,
      durationMilliseconds: 321,
    }]);
  });

  it('dispatches typed tool call end events with function-call metadata', async () => {
    const events: Array<{
      callId: string;
      messageId: string;
      name: string;
      argsJson: string;
    }> = [];
    const client = new AgentClient('http://localhost:5135');

    client.on(EventTypes.TOOL_CALL_END, (event) => {
      events.push({
        callId: event.callId,
        messageId: event.messageId,
        name: event.name,
        argsJson: event.argsJson,
      });
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream({
      version: '1.0',
      type: EventTypes.TOOL_CALL_END,
      callId: 'call-1',
      messageId: 'msg-1',
      name: 'Calculator',
      argsJson: '{"x":1}',
    }));

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(events).toEqual([{
      callId: 'call-1',
      messageId: 'msg-1',
      name: 'Calculator',
      argsJson: '{"x":1}',
    }]);
  });

  it('dispatches typed thread execution terminal outcomes', async () => {
    const finished = vi.fn();
    const client = new AgentClient('http://localhost:5135');

    client.on(EventTypes.THREAD_EXECUTION_FINISHED, (event) => {
      finished(event.threadExecutionId, event.outcome, event.finishedAt, event.error?.message);
    });

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(okStream({
      version: '1.0',
      type: EventTypes.THREAD_EXECUTION_FINISHED,
      threadExecutionId: 'execution-1',
      agentId: 'agent-1',
      outcome: 'Failed',
      finishedAt: '2026-07-15T00:00:01Z',
      error: { type: 'ProviderError', message: 'Provider failed.' },
    }));

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(finished).toHaveBeenCalledWith(
      'execution-1',
      'Failed',
      '2026-07-15T00:00:01Z',
      'Provider failed.',
    );
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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(typed).not.toHaveBeenCalled();
    expect(any).not.toHaveBeenCalled();
  });

  it('posts USER_MESSAGES_INPUT events to the scoped inputs endpoint', async () => {
    const client = new AgentClient('http://localhost:5135');
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(okSubmission());

    const runConfig = { providerKey: 'anthropic', modelId: 'claude-sonnet-4-6' };
    await client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Hello' }],
      }],
      sessionId: 'session-123',
      agentId: 'agent-1',
      threadId: 'main',
      runConfig,
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/agent-1/sessions/session-123/threads/main/inputs',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          type: EventTypes.USER_MESSAGES_INPUT,
          messages: [{
            role: 'user',
            contents: [{ $type: 'text', text: 'Hello' }],
          }],
          sessionId: 'session-123',
          agentId: 'agent-1',
          threadId: 'main',
          runConfig,
        }),
      })
    );
  });

  it('passes configured headers and credentials through HTTP transports', async () => {
    const client = new AgentClient({
      baseUrl: 'http://localhost:5135',
      headers: { Authorization: 'Bearer test-token' },
      credentials: 'include',
    });
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(okSubmission());

    await client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Hello' }],
      }],
      sessionId: 'session-123',
      agentId: 'agent-1',
      threadId: 'main',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/agent-1/sessions/session-123/threads/main/inputs',
      expect.objectContaining({
        credentials: 'include',
        headers: expect.objectContaining({
          Authorization: 'Bearer test-token',
          'Content-Type': 'application/json',
        }),
      })
    );
  });

  it('keeps relative API base URLs relative for desktop shells', async () => {
    const client = new AgentClient('/api/hpd-agent');
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(okSubmission());

    await client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Hello' }],
      }],
      sessionId: 'session-123',
      agentId: 'agent-1',
      threadId: 'main',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/hpd-agent/agents/agent-1/sessions/session-123/threads/main/inputs',
      expect.objectContaining({ method: 'POST' }),
    );
  });

  it('routes permission response inputs through submitInput()', async () => {
    const client = new AgentClient('http://localhost:5135');
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(okStream())
      .mockResolvedValueOnce({
        ok: true,
        text: async () => '',
      } as Response);

    await client.start({ agentId: 'agent-1', sessionId: 'session-123', threadId: 'main' });
    await client.submitInput({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'perm-1',
      sourceName: 'PermissionMiddleware',
      approved: true,
      choice: 'allow_always',
    });

    expect(fetchSpy).toHaveBeenCalledWith(
      'http://localhost:5135/agents/agent-1/sessions/session-123/threads/main/responses',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          version: '1.0',
          type: EventTypes.PERMISSION_RESPONSE,
          permissionId: 'perm-1',
          sourceName: 'PermissionMiddleware',
          approved: true,
          choice: 1,
        }),
      })
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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errors).toEqual(['stream broke']);
  });

  it('auto responds to client tool invoke requests registered in the tool registry', async () => {
    const client = new AgentClient('http://localhost:5135');
    client.tools.register('echo', () => 'done');

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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchSpy).toHaveBeenLastCalledWith(
      'http://localhost:5135/agents/agent-1/sessions/session-123/threads/main/responses',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          version: '1.0',
          type: EventTypes.CLIENT_TOOL_INVOKE_OUTCOME,
          requestId: 'req-1',
          capabilities: ['client-tool:echo'],
          outcome: 'Completed',
          content: [{ type: 'text', text: 'done' }],
        }),
      })
    );
  });

  it('does not auto respond to targeted client tool requests for another responder', async () => {
    const client = new AgentClient({
      baseUrl: 'http://localhost:5135',
      responderId: 'browser-a',
      responderGroups: ['web-ui'],
    });
    client.tools.register('echo', () => 'done');

    const fetchSpy = vi.spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(okStream({
        version: '1.0',
        type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
        requestId: 'req-1',
        toolName: 'browser.echo',
        arguments: {},
        responsePolicy: 'targetedResponder',
        target: {
          responderId: 'browser-b',
          requiredCapabilities: ['client-tool:echo'],
        },
      }));

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  it('uses the resumable SSE transport', () => {
    const client = new AgentClient('http://localhost:5135');

    expect((client as any).transport.constructor.name).toBe('SseTransport');
  });

  it('aborts an active live subscription', async () => {
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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });

    await new Promise((resolve) => setTimeout(resolve, 10));
    client.disconnectLive();

    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(client.connected).toBe(false);
  });

  it('remains logically connected while SSE is recovering from EOF', async () => {
    const client = new AgentClient('http://localhost:5135');

    expect(client.connected).toBe(false);

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

    await client.start({ sessionId: 'session-123', agentId: 'agent-1', threadId: 'main' });

    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(client.connected).toBe(true);

    await new Promise((resolve) => setTimeout(resolve, 60));
    expect(client.connected).toBe(true);
    await client.stop();
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(client.connected).toBe(false);
  });
});
