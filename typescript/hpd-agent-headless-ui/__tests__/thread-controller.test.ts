import { describe, expect, it, vi } from 'vitest';
import { EventTypes, type AgentClient, type EventSubscription } from '@hpd-research/hpd-agent-client';
import { createThreadController } from '../src/index.js';

type TestAgentClient = AgentClient & {
  __emit: (event: never) => Promise<void>;
};

function subscription(dispose: () => void = () => {}): EventSubscription {
  return { dispose: vi.fn(dispose) };
}

function fakeClient(): TestAgentClient {
  const handlers: Array<(event: never) => void | Promise<void>> = [];
  const client = {
    connected: false,
    start: vi.fn(async () => {
      client.connected = true;
    }),
    stop: vi.fn(async () => {
      client.connected = false;
    }),
    run: vi.fn(async () => {}),
    submitInput: vi.fn(async () => {}),
    onAny: vi.fn((handler: (event: never) => void | Promise<void>) => {
      handlers.push(handler);
      return subscription(() => {
        const index = handlers.indexOf(handler);
        if (index >= 0) handlers.splice(index, 1);
      });
    }),
    onError: vi.fn(() => subscription()),
    getThread: vi.fn(async () => null),
    getThreadEvents: vi.fn(async () => []),
    getThreadRuns: vi.fn(async () => []),
    getActiveThreadRun: vi.fn(async () => null),
    __emit: async (event: never) => {
      for (const handler of handlers) await handler(event);
    },
  };
  return client as unknown as TestAgentClient;
}

describe('createThreadController', () => {
  it('rehydrates and connects to the exact thread scope', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.start();

    expect(client.getThread).toHaveBeenCalledWith('s1', 'main');
    expect(client.getThreadEvents).toHaveBeenCalledWith('s1', 'main');
    expect(client.start).toHaveBeenCalledWith({
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      signal: undefined,
    });
  });

  it('stamps sendMessage inputs with thread scope', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.sendMessage({
      contents: [{ $type: 'text', text: 'hello' }],
      additionalProperties: {
        quote: {
          text: 'selected text',
          messageId: 'message-1',
          source: 'selection',
        },
      },
    });

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'hello' }],
        additionalProperties: {
          quote: {
            text: 'selected text',
            messageId: 'message-1',
            source: 'selection',
          },
        },
      }],
      runConfig: undefined,
      clientInputId: expect.any(String),
    });
  });

  it('sends structured user message contents', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.sendMessage({
      contents: [
        { $type: 'text', text: 'inspect this' },
        {
          $type: 'uri',
          uri: 'hpd-content://content-1',
          mediaType: 'image/png',
          additionalProperties: {
            contentId: 'content-1',
            version: 'v1',
            name: 'screen.png',
            sizeBytes: 123,
          },
        },
      ],
    });

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      messages: [{
        role: 'user',
        contents: [
          { $type: 'text', text: 'inspect this' },
          {
            $type: 'uri',
            uri: 'hpd-content://content-1',
            mediaType: 'image/png',
            additionalProperties: {
              contentId: 'content-1',
              version: 'v1',
              name: 'screen.png',
              sizeBytes: 123,
            },
          },
        ],
      }],
      runConfig: undefined,
      clientInputId: expect.any(String),
    });
  });

  it('optimistically projects sent text before backend hydration', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.sendMessage({ contents: [{ $type: 'text', text: 'hello now' }] });

    expect(controller.projection.getSnapshot().transcriptMessages).toMatchObject([{
      role: 'user',
      content: 'hello now',
      streaming: false,
      placement: 'optimistic',
      clientInputId: expect.any(String),
    }]);
  });

  it('reconciles optimistic user text from durable runtime events', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.start();

    await controller.sendMessage({ contents: [{ $type: 'text', text: 'hello durable' }] });
    const input = vi.mocked(client.run).mock.calls[0][0];

    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      sessionId: 's1',
      threadId: 'main',
      messageId: 'm-user-1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      clientInputId: input.clientInputId,
    } as never);
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      sessionId: 's1',
      threadId: 'main',
      messageId: 'm-user-1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      clientInputId: input.clientInputId,
    } as never);
    await client.__emit({
      type: EventTypes.TEXT_DELTA,
      sessionId: 's1',
      threadId: 'main',
      messageId: 'm-user-1',
      text: 'hello durable',
    } as never);
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_END,
      sessionId: 's1',
      threadId: 'main',
      messageId: 'm-user-1',
    } as never);

    const snapshot = controller.projection.getSnapshot();
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'm-user-1',
      role: 'user',
      content: 'hello durable',
      streaming: false,
      placement: 'transcript',
      clientInputId: expect.any(String),
    }]);
    expect(snapshot.transcriptMessages[0].id.startsWith('optimistic:')).toBe(false);
    expect(snapshot.timeline).toHaveLength(1);
    expect(snapshot.timeline[0]).toMatchObject({
      type: 'message',
      message: { id: 'm-user-1' },
    });
  });

  it('blocks sendMessage locally while the thread is busy', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    controller.projection.project({
      type: EventTypes.THREAD_RUN_STARTED,
      runtimeRunId: 'run-1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
    });

    await expect(controller.sendMessage({ contents: [{ $type: 'text', text: 'busy submit' }] }))
      .rejects.toThrow('Thread message submission is blocked: busy.');
    expect(client.run).not.toHaveBeenCalled();
  });

  it('responds to pending client-tool requests with stamped thread scope', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    controller.projection.project({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 'tool-1',
      sourceName: 'HPD.Agent.ClientTools',
      toolName: 'pickFile',
      callId: 'call-1',
      arguments: { accept: 'image/*' },
      responsePolicy: 'targetedResponder',
    });

    await controller.answerClientToolRequest('tool-1', 'selected screenshot.png', {
      responderId: 'desktop',
      capabilities: ['client-tool:pickFile'],
    });

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.CLIENT_TOOL_INVOKE_OUTCOME,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      requestId: 'tool-1',
      outcome: 'Completed',
      content: [{ type: 'text', text: 'selected screenshot.png' }],
      errorMessage: undefined,
      clientOperationId: undefined,
      handleKind: undefined,
      supportedOperations: undefined,
      augmentation: undefined,
      responderId: 'desktop',
      responderGroup: undefined,
      capabilities: ['client-tool:pickFile'],
    });
  });

  it('allows runtime request responses while normal text submission is busy-blocked', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    controller.projection.project({
      type: EventTypes.THREAD_RUN_STARTED,
      runtimeRunId: 'run-1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
    });
    controller.projection.project({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'call-1',
    });

    await controller.approve('p1');

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.PERMISSION_RESPONSE,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
      choice: 'ask',
    });
  });

  it('does not respond to unknown client-tool requests', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.answerClientToolRequest('missing', 'ignored');

    expect(client.run).not.toHaveBeenCalled();
  });

  it('projects only events that belong to its thread scope by default', async () => {
    const client = fakeClient() as AgentClient & {
      __emit(event: unknown): Promise<void>;
    };
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.connect();
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a0',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
    });
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      sessionId: 's1',
      threadId: 'other',
    });
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a2',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(controller.projection.getSnapshot().transcriptMessages.map((message) => message.id)).toEqual(['a2']);
  });

  it('can opt into scope-less compatibility events', async () => {
    const client = fakeClient() as AgentClient & {
      __emit(event: unknown): Promise<void>;
    };
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      allowScopeLessEvents: true,
    });

    await controller.connect();
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
    });

    expect(controller.projection.getSnapshot().transcriptMessages.map((message) => message.id)).toEqual(['a1']);
  });

  it('can detach listeners without stopping a caller-owned client', async () => {
    const client = fakeClient() as AgentClient & {
      __emit(event: unknown): Promise<void>;
    };
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      stopClientOnDisconnect: false,
    });

    await controller.connect();
    await controller.disconnect();
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.stop).not.toHaveBeenCalled();
    expect(controller.projection.getSnapshot().transcriptMessages).toEqual([]);
  });
});
