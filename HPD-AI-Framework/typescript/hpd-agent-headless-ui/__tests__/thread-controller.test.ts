import { describe, expect, it, vi } from 'vitest';
import { EventTypes, type AgentClient, type EventSubscription } from '@hpd-research/hpd-agent-client';
import { createThreadController } from '../src/index.js';

function subscription(dispose: () => void = () => {}): EventSubscription {
  return { dispose: vi.fn(dispose) };
}

function fakeClient(): AgentClient {
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
    getThreadMessages: vi.fn(async () => []),
    getThreadEvents: vi.fn(async () => []),
    getThreadRuns: vi.fn(async () => []),
    getActiveThreadRun: vi.fn(async () => null),
    __emit: async (event: never) => {
      for (const handler of handlers) await handler(event);
    },
  };
  return client as unknown as AgentClient;
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
    expect(client.getThreadMessages).toHaveBeenCalledWith('s1', 'main');
    expect(client.start).toHaveBeenCalledWith({
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      signal: undefined,
    });
  });

  it('stamps sendText inputs with thread scope', async () => {
    const client = fakeClient();
    const controller = createThreadController({
      client,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    await controller.sendText('hello');

    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_TEXT_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
      text: 'hello',
      runConfig: undefined,
    });
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
    });
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      sessionId: 's1',
      threadId: 'other',
    });
    await client.__emit({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a2',
      role: 'assistant',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(controller.projection.getSnapshot().messages.map((message) => message.id)).toEqual(['a2']);
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
    });

    expect(controller.projection.getSnapshot().messages.map((message) => message.id)).toEqual(['a1']);
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
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(client.stop).not.toHaveBeenCalled();
    expect(controller.projection.getSnapshot().messages).toEqual([]);
  });
});
