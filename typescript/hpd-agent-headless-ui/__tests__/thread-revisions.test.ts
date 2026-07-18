import { describe, expect, it, vi } from 'vitest';
import {
  EventTypes,
  mapThreadMessages,
  type AgentClient,
  type Thread,
  type ThreadMessage,
} from '@hpd-research/hpd-agent-client';
import {
  canEditThreadMessage,
  canRetryThreadMessage,
  createThreadRevisionController,
  ThreadRevisionError,
} from '../src/index.js';

function thread(id: string): Thread {
  return {
    id,
    sessionId: 's1',
    defaultAgentId: 'agent-1',
    name: id,
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActivity: '2026-01-01T00:00:00.000Z',
    messageCount: 1,
    kind: 'MainAgent',
    visibility: 'Visible',
    childThreads: [],
    totalForks: 0,
  };
}

function message(id: string, role: string, text: string): ThreadMessage {
  return {
    id,
    role,
    timestamp: '2026-01-01T00:00:00.000Z',
    contents: [{ $type: 'text', text }],
  };
}

function fakeClient(messages: ThreadMessage[] = transcript()): AgentClient {
  return {
    getThreadMessages: vi.fn(async () => messages),
    forkThread: vi.fn(async () => thread('fork-1')),
    run: vi.fn(async () => undefined),
  } as unknown as AgentClient;
}

function transcript(): ThreadMessage[] {
  return [
    message('system-1', 'system', 'System instructions.'),
    message('developer-1', 'developer', 'Developer guidance.'),
    message('user-1', 'user', 'Explain the design.'),
    message('assistant-1', 'assistant', 'The design is old.'),
    message('user-2', 'user', 'Make it shorter.'),
    message('assistant-2', 'assistant', 'Short version.'),
    message('tool-1', 'tool', 'Tool result.'),
  ];
}

function controller(client: AgentClient) {
  return createThreadRevisionController({
    client,
    agentId: 'agent',
    sessionId: 's1',
    threadId: 'main',
    loadMessages: async () => mapThreadMessages(
      await (client as unknown as { getThreadMessages(): Promise<ThreadMessage[]> }).getThreadMessages()),
  });
}

describe('createThreadRevisionController', () => {
  it('exposes message role revision eligibility helpers', () => {
    expect(canEditThreadMessage({ role: 'user', text: 'Prompt' })).toBe(true);
    expect(canEditThreadMessage({ role: 'assistant', text: 'Answer' })).toBe(false);
    expect(canRetryThreadMessage({ role: 'user', text: 'Prompt' })).toBe(true);
    expect(canRetryThreadMessage({ role: 'assistant', text: 'Answer' })).toBe(true);
    expect(canRetryThreadMessage({ role: 'tool' })).toBe(false);
    expect(canEditThreadMessage({ role: 'user', placement: 'optimistic', text: 'Prompt' })).toBe(false);
    expect(canRetryThreadMessage({ role: 'user', placement: 'optimistic', text: 'Prompt' })).toBe(false);
    expect(canEditThreadMessage({ role: 'user', placement: 'work', text: 'Prompt' })).toBe(false);
    expect(canRetryThreadMessage({ role: 'assistant', placement: 'work', text: 'Draft' })).toBe(false);
    expect(canRetryThreadMessage({ role: 'assistant', placement: 'final', text: 'Answer' })).toBe(true);
    expect(canRetryThreadMessage({ role: 'assistant', placement: 'final', text: '' })).toBe(false);
    expect(canRetryThreadMessage({
      role: 'assistant',
      placement: 'final',
      text: '',
      toolCalls: [{}],
    })).toBe(false);
  });

  it('retries a user message by forking before it and sending the same text', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    const result = await revisions.forkAndRetryMessage('user-2', {
      runConfig: { modelId: 'careful' },
      fork: { name: 'retry shorter' },
    });

    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: 'assistant-1',
      name: 'retry shorter',
      metadata: {
        revisionKind: 'retry',
        clickedMessageId: 'user-2',
        inputMessageId: 'user-2',
        forkBoundaryMessageId: 'assistant-1',
      },
    });
    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Make it shorter.' }],
      }],
      runConfig: { modelId: 'careful' },
    });
    expect(result).toMatchObject({
      kind: 'retry',
      threadId: 'fork-1',
      clickedMessageId: 'user-2',
      inputMessageId: 'user-2',
      forkBoundaryMessageId: 'assistant-1',
      sentText: 'Make it shorter.',
    });
  });

  it('retries an assistant message by resending the previous user message', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    const result = await revisions.forkAndRetryMessage('assistant-2');

    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: 'assistant-1',
      metadata: {
        revisionKind: 'retry',
        clickedMessageId: 'assistant-2',
        inputMessageId: 'user-2',
        forkBoundaryMessageId: 'assistant-1',
      },
    });
    expect(client.run).toHaveBeenCalledWith(expect.objectContaining({
      type: EventTypes.USER_MESSAGES_INPUT,
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Make it shorter.' }],
      }],
    }));
    expect(result).toMatchObject({
      kind: 'retry',
      clickedMessageId: 'assistant-2',
      inputMessageId: 'user-2',
      forkBoundaryMessageId: 'assistant-1',
      sentText: 'Make it shorter.',
    });
  });

  it('edits a user message by forking before it and sending the replacement text', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    const result = await revisions.forkAndEditMessage('user-2', 'Make it one sentence.');

    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: 'assistant-1',
      metadata: {
        revisionKind: 'edit',
        clickedMessageId: 'user-2',
        inputMessageId: 'user-2',
        forkBoundaryMessageId: 'assistant-1',
      },
    });
    expect(client.run).toHaveBeenCalledWith(expect.objectContaining({
      type: EventTypes.USER_MESSAGES_INPUT,
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Make it one sentence.' }],
      }],
    }));
    expect(result).toMatchObject({
      kind: 'edit',
      clickedMessageId: 'user-2',
      inputMessageId: 'user-2',
      sentText: 'Make it one sentence.',
    });
  });

  it('rejects editing assistant messages because only user messages can be edited', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    await expect(revisions.forkAndEditMessage('assistant-2', 'Make it one sentence.'))
      .rejects.toMatchObject({
        name: 'ThreadRevisionError',
        code: 'unsupported-message-role',
      });
    expect(client.forkThread).not.toHaveBeenCalled();
    expect(client.run).not.toHaveBeenCalled();
  });

  it('rejects retrying non-user and non-assistant messages', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    await expect(revisions.forkAndRetryMessage('system-1'))
      .rejects.toMatchObject({
        name: 'ThreadRevisionError',
        code: 'unsupported-message-role',
      });
    await expect(revisions.forkAndRetryMessage('developer-1'))
      .rejects.toMatchObject({
        name: 'ThreadRevisionError',
        code: 'unsupported-message-role',
      });
    expect(client.forkThread).not.toHaveBeenCalled();
    expect(client.run).not.toHaveBeenCalled();
  });

  it('retries a first user message by forking from root', async () => {
    const client = fakeClient([
      message('user-1', 'user', 'Start here.'),
      message('assistant-1', 'assistant', 'Answer.'),
    ]);
    const revisions = controller(client);

    const result = await revisions.forkAndRetryMessage('assistant-1');

    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: null,
      metadata: {
        revisionKind: 'retry',
        clickedMessageId: 'assistant-1',
        inputMessageId: 'user-1',
        forkBoundaryMessageId: null,
      },
    });
    expect(client.run).toHaveBeenCalledWith(expect.objectContaining({
      type: EventTypes.USER_MESSAGES_INPUT,
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Start here.' }],
      }],
    }));
    expect(result).toMatchObject({
      clickedMessageId: 'assistant-1',
      inputMessageId: 'user-1',
      forkBoundaryMessageId: null,
      sentText: 'Start here.',
    });
  });

  it('edits a first user message by forking from root and sending replacement text', async () => {
    const client = fakeClient([
      message('user-1', 'user', 'Start here.'),
      message('assistant-1', 'assistant', 'Answer.'),
    ]);
    const revisions = controller(client);

    const result = await revisions.forkAndEditMessage('user-1', 'Start somewhere better.', {
      runConfig: { modelId: 'careful' },
      fork: { name: 'edited start' },
    });

    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: null,
      name: 'edited start',
      metadata: {
        revisionKind: 'edit',
        clickedMessageId: 'user-1',
        inputMessageId: 'user-1',
        forkBoundaryMessageId: null,
      },
    });
    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Start somewhere better.' }],
      }],
      runConfig: { modelId: 'careful' },
    });
    expect(result).toMatchObject({
      kind: 'edit',
      clickedMessageId: 'user-1',
      inputMessageId: 'user-1',
      forkBoundaryMessageId: null,
      sentText: 'Start somewhere better.',
    });
  });

  it('rejects missing or empty revision inputs before forking', async () => {
    const client = fakeClient();
    const revisions = controller(client);

    await expect(revisions.forkAndRetryMessage('missing'))
      .rejects.toBeInstanceOf(ThreadRevisionError);
    await expect(revisions.forkAndEditMessage('user-2', '   '))
      .rejects.toMatchObject({ code: 'empty-message' });
    expect(client.forkThread).not.toHaveBeenCalled();
    expect(client.run).not.toHaveBeenCalled();
  });

  it('resolves fork options after retry has normalized the clicked assistant to its input', async () => {
    const client = fakeClient();
    const revisions = controller(client);
    const fork = vi.fn((details) => ({
      name: `Retry ${details.inputMessageId}`,
      metadata: {
        userMetadata: true,
        inputMessageId: 'wrong-id',
      },
    }));

    const result = await revisions.forkAndRetryMessage('assistant-2', { fork });

    expect(fork).toHaveBeenCalledWith({
      kind: 'retry',
      clickedMessageId: 'assistant-2',
      inputMessageId: 'user-2',
      forkBoundaryMessageId: 'assistant-1',
      sentText: 'Make it shorter.',
    });
    expect(client.forkThread).toHaveBeenCalledWith('s1', 'main', {
      agentId: 'agent',
      fromMessageId: 'assistant-1',
      name: 'Retry user-2',
      metadata: {
        userMetadata: true,
        revisionKind: 'retry',
        clickedMessageId: 'assistant-2',
        inputMessageId: 'user-2',
        forkBoundaryMessageId: 'assistant-1',
      },
    });
    expect(result).toMatchObject({
      clickedMessageId: 'assistant-2',
      inputMessageId: 'user-2',
      forkBoundaryMessageId: 'assistant-1',
    });
  });
});
