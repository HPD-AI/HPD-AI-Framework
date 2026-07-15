import { describe, expect, it, vi } from 'vitest';
import {
  EventTypes,
  type AgentClient,
  type Thread,
  type ThreadMessage,
} from '@hpd-research/hpd-agent-client';
import {
  canEditMessage,
  canRetryMessage,
  createThreadRevisionState,
  createThreadStateFromRevision,
  ThreadRevisionStateError,
  type ThreadRevisionStateSnapshot,
} from '../src/index.js';
import type {
  Message,
  ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';

function thread(id: string): Thread {
  return {
    id,
    sessionId: 's1',
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

function uiMessage(role: Message['role'], overrides: Partial<Message> = {}): Message {
  return {
    id: `${role}-1`,
    role,
    content: 'hello',
    streaming: false,
    thinking: false,
    timestamp: new Date(),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    runId: null,
    placement: 'transcript',
    ...overrides,
  };
}

function fakeClient(messages: ThreadMessage[] = transcript()): AgentClient {
  const client = {
    connected: false,
    start: vi.fn(async () => {
      client.connected = true;
    }),
    stop: vi.fn(async () => {
      client.connected = false;
    }),
    onAny: vi.fn(() => ({ dispose: vi.fn() })),
    onError: vi.fn(() => ({ dispose: vi.fn() })),
    getThread: vi.fn(async () => thread('fork-1')),
    getThreadEvents: vi.fn(async () => []),
    getThreadRuns: vi.fn(async () => []),
    getThreadState: vi.fn(async () => ({ latestSequenceNumber: 0, events: [], activeRun: null })),
    getThreadMessages: vi.fn(async () => messages),
    forkThread: vi.fn(async () => thread('fork-1')),
    run: vi.fn(async () => undefined),
  };

  return client as unknown as AgentClient;
}

function transcript(): ThreadMessage[] {
  return [
    message('system-1', 'system', 'System instructions.'),
    message('user-1', 'user', 'Explain the design.'),
    message('assistant-1', 'assistant', 'The design is old.'),
  ];
}

function createState(client: AgentClient, callbacks: {
  onRevisionCreated?: (result: ThreadRevisionResult) => void;
  onError?: (error: Error) => void;
} = {}) {
  return createThreadRevisionState({
    client,
    agentId: 'agent',
    sessionId: 's1',
    threadId: 'main',
    ...callbacks,
  });
}

describe('createThreadRevisionState', () => {
  it('wraps retry as Svelte-readable revision state', async () => {
    const client = fakeClient();
    const onRevisionCreated = vi.fn();
    const revisions = createState(client, { onRevisionCreated });
    const observed: ThreadRevisionStateSnapshot[] = [];
    const unsubscribe = revisions.subscribe((snapshot) => observed.push(snapshot));

    const result = await revisions.forkAndRetryMessage('assistant-1', {
      runConfig: { modelId: 'careful' },
    });

    expect(result.threadId).toBe('fork-1');
    expect(revisions.getSnapshot()).toMatchObject({
      running: false,
      lastRevision: result,
      error: null,
    });
    expect(observed.some((snapshot) =>
      snapshot.running
        && snapshot.activeKind === 'retry'
        && snapshot.activeClickedMessageId === 'assistant-1',
    )).toBe(true);
    expect(onRevisionCreated).toHaveBeenCalledWith(result);
    expect(client.run).toHaveBeenCalledWith({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Explain the design.' }],
      }],
      runConfig: { modelId: 'careful' },
    });

    unsubscribe();
  });

  it('wraps first-message edit as a root fork revision', async () => {
    const client = fakeClient([
      message('user-1', 'user', 'Start here.'),
      message('assistant-1', 'assistant', 'Answer.'),
    ]);
    const onRevisionCreated = vi.fn();
    const revisions = createState(client, { onRevisionCreated });

    const result = await revisions.forkAndEditMessage('user-1', 'Start somewhere better.', {
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
    expect(client.run).toHaveBeenCalledWith(expect.objectContaining({
      type: EventTypes.USER_MESSAGES_INPUT,
      threadId: 'fork-1',
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: 'Start somewhere better.' }],
      }],
    }));
    expect(result).toMatchObject({
      kind: 'edit',
      clickedMessageId: 'user-1',
      inputMessageId: 'user-1',
      forkBoundaryMessageId: null,
      sentText: 'Start somewhere better.',
    });
    expect(revisions.getSnapshot().lastRevision).toBe(result);
    expect(onRevisionCreated).toHaveBeenCalledWith(result);
  });

  it('tracks edit failures and rethrows them', async () => {
    const client = fakeClient();
    const onError = vi.fn();
    const revisions = createState(client, { onError });

    await expect(revisions.forkAndEditMessage('assistant-1', 'Nope.'))
      .rejects.toMatchObject({ code: 'unsupported-message-role' });

    expect(revisions.getSnapshot().running).toBe(false);
    expect(revisions.getSnapshot().error?.name).toBe('ThreadRevisionError');
    expect(onError).toHaveBeenCalledWith(revisions.getSnapshot().error);
    expect(client.forkThread).not.toHaveBeenCalled();
    expect(client.run).not.toHaveBeenCalled();
  });

  it('does not report a created revision when resend fails after forking', async () => {
    const client = fakeClient();
    const onRevisionCreated = vi.fn();
    const onError = vi.fn();
    vi.mocked(client.run).mockRejectedValueOnce(new Error('run failed'));
    const revisions = createState(client, { onRevisionCreated, onError });

    await expect(revisions.forkAndRetryMessage('assistant-1'))
      .rejects.toThrow('run failed');

    expect(client.forkThread).toHaveBeenCalled();
    expect(revisions.getSnapshot()).toMatchObject({
      running: false,
      lastRevision: null,
    });
    expect(revisions.getSnapshot().error?.message).toBe('run failed');
    expect(onRevisionCreated).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledWith(revisions.getSnapshot().error);
  });

  it('rejects a second revision while one is already running', async () => {
    const client = fakeClient();
    const onError = vi.fn();
    let releaseRun: (() => void) | undefined;
    vi.mocked(client.run).mockImplementationOnce(async () => {
      await new Promise<void>((resolve) => {
        releaseRun = resolve;
      });
    });
    const revisions = createState(client, { onError });

    const first = revisions.forkAndRetryMessage('assistant-1');
    await vi.waitFor(() => {
      expect(revisions.getSnapshot().running).toBe(true);
    });

    await expect(revisions.forkAndEditMessage('user-1', 'Edited text.'))
      .rejects.toBeInstanceOf(ThreadRevisionStateError);

    expect(onError).toHaveBeenCalledWith(expect.objectContaining({
      name: 'ThreadRevisionStateError',
      code: 'revision-in-progress',
    }));
    expect(client.forkThread).toHaveBeenCalledTimes(1);
    expect(client.run).toHaveBeenCalledTimes(1);

    releaseRun?.();
    await first;
  });

  it('exposes revision action role policy for Message callbacks', () => {
    expect(canEditMessage(uiMessage('user'))).toBe(true);
    expect(canEditMessage(uiMessage('assistant'))).toBe(false);
    expect(canRetryMessage(uiMessage('user'))).toBe(true);
    expect(canRetryMessage(uiMessage('assistant'))).toBe(true);
    expect(canRetryMessage(uiMessage('tool'))).toBe(false);
    expect(canRetryMessage(uiMessage('assistant', { content: '' }))).toBe(false);
    expect(canRetryMessage(uiMessage('assistant', {
      content: '',
      toolCalls: [{
        callId: 'tool-1',
        name: 'ListDirectory',
        messageId: 'assistant-1',
        status: 'complete',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        turnId: null,
        conversationId: null,
        runId: null,
      }],
    }))).toBe(false);
  });

  it('creates and starts a ThreadState for a revision by default', async () => {
    const client = fakeClient();

    const threadState = await createThreadStateFromRevision({
      client,
      agentId: 'agent',
      sessionId: 's1',
      revision: 'fork-1',
      hydrateOptions: { includeRuns: true },
    });

    expect(threadState.controller.scope.threadId).toBe('fork-1');
    expect(client.getThread).toHaveBeenCalledWith('s1', 'fork-1');
    expect(client.getThreadRuns).toHaveBeenCalledWith('agent', 's1', 'fork-1');
    expect(client.start).toHaveBeenCalledWith({
      agentId: 'agent',
      sessionId: 's1',
      threadId: 'fork-1',
      afterSequenceNumber: 0,
      signal: undefined,
    });
    expect(threadState.getSnapshot().connected).toBe(true);
  });

  it('can create a rehydrated-only ThreadState for a revision', async () => {
    const client = fakeClient();

    const threadState = await createThreadStateFromRevision({
      client,
      agentId: 'agent',
      sessionId: 's1',
      revision: {
        kind: 'retry',
        thread: thread('fork-1'),
        threadId: 'fork-1',
        clickedMessageId: 'assistant-1',
        inputMessageId: 'user-1',
        forkBoundaryMessageId: 'system-1',
        sentText: 'Explain the design.',
      },
      hydrate: 'rehydrate',
    });

    expect(client.getThread).toHaveBeenCalledWith('s1', 'fork-1');
    expect(client.start).not.toHaveBeenCalled();
    expect(threadState.getSnapshot().connected).toBe(false);
  });

  it('can create an unhydrated ThreadState for app-controlled navigation', async () => {
    const client = fakeClient();

    const threadState = await createThreadStateFromRevision({
      client,
      agentId: 'agent',
      sessionId: 's1',
      revision: 'fork-1',
      hydrate: 'none',
    });

    expect(threadState.controller.scope.threadId).toBe('fork-1');
    expect(client.getThread).not.toHaveBeenCalled();
    expect(client.start).not.toHaveBeenCalled();
  });
});
