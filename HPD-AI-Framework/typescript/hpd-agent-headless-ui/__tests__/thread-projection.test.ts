import { describe, expect, it } from 'vitest';
import { EventTypes, type ThreadMessage } from '@hpd-research/hpd-agent-client';
import { createThreadProjection, eventBelongsToScope } from '../src/index.js';

describe('createThreadProjection', () => {
  it('rehydrates settled thread messages without streaming state', () => {
    const projection = createThreadProjection();
    const messages: ThreadMessage[] = [
      {
        id: 'm1',
        role: 'user',
        timestamp: '2026-01-01T00:00:00.000Z',
        contents: [{ $type: 'text', text: 'hello' }],
      },
      {
        id: 'm2',
        role: 'assistant',
        timestamp: '2026-01-01T00:00:01.000Z',
        contents: [
          { $type: 'reasoning', text: 'thinking' },
          { $type: 'text', text: 'hi there' },
        ],
      },
    ];

    projection.rehydrate({ messages });

    const snapshot = projection.getSnapshot();
    expect(snapshot.messages).toHaveLength(2);
    expect(snapshot.messages[0].content).toBe('hello');
    expect(snapshot.messages[1].content).toBe('hi there');
    expect(snapshot.messages[1].reasoning).toBe('thinking');
    expect(snapshot.streaming).toBe(false);
    expect(snapshot.canSend).toBe(true);
  });

  it('projects text deltas into a live assistant message', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'hel',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'lo',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'a1',
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.messages[0].content).toBe('hello');
    expect(snapshot.messages[0].streaming).toBe(false);
    expect(snapshot.streaming).toBe(false);
  });

  it('tracks pending permission requests until approval or denial events', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'c1',
    });
    expect(projection.getSnapshot().pendingPermissions).toHaveLength(1);

    projection.project({
      type: EventTypes.PERMISSION_APPROVED,
      permissionId: 'p1',
      sourceName: 'permission',
    });
    expect(projection.getSnapshot().pendingPermissions).toHaveLength(0);
  });

  it('clears pending runtime requests from request lifecycle terminal events', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.PERMISSION_REQUEST,
      permissionId: 'p1',
      sourceName: 'permission',
      functionName: 'Bash',
      callId: 'c1',
    });
    projection.project({
      type: EventTypes.CLARIFICATION_REQUEST,
      requestId: 'c1',
      sourceName: 'clarification',
      question: 'Which tenant?',
    });
    projection.project({
      type: EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      requestId: 't1',
      toolName: 'pickFile',
      callId: 'tc1',
      arguments: {},
    });

    expect(projection.getSnapshot().pendingPermissions).toHaveLength(1);
    expect(projection.getSnapshot().pendingClarifications).toHaveLength(1);
    expect(projection.getSnapshot().pendingClientToolRequests).toHaveLength(1);

    projection.project({
      type: EventTypes.AGENT_REQUEST_RESOLVED,
      requestId: 'p1',
      sourceName: 'permission',
      requestEventType: 'PermissionRequestEvent',
      responseEventType: 'PermissionResponseEvent',
      resolvedAt: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.AGENT_REQUEST_EXPIRED,
      requestId: 'c1',
      sourceName: 'clarification',
      requestEventType: 'ClarificationRequestEvent',
      timeout: '00:01:00',
      expiredAt: '2026-01-01T00:00:01.000Z',
    });
    projection.project({
      type: EventTypes.AGENT_REQUEST_CANCELLED,
      requestId: 't1',
      sourceName: 'client-tools',
      requestEventType: 'ClientToolInvokeRequestEvent',
      cancelledAt: '2026-01-01T00:00:02.000Z',
    });

    expect(projection.getSnapshot().pendingPermissions).toHaveLength(0);
    expect(projection.getSnapshot().pendingClarifications).toHaveLength(0);
    expect(projection.getSnapshot().pendingClientToolRequests).toHaveLength(0);
  });

  it('tracks thread run lifecycle', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.THREAD_RUN_STARTED,
      runtimeRunId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
      sessionId: 's1',
      threadId: 'main',
    });
    expect(projection.getSnapshot().threadRun?.status).toBe('active');
    expect(projection.getSnapshot().streaming).toBe(true);

    projection.project({
      type: EventTypes.THREAD_RUN_COMPLETED,
      runtimeRunId: 'run1',
      agentId: 'agent',
      cancelled: false,
      sessionId: 's1',
      threadId: 'main',
    });
    expect(projection.getSnapshot().threadRun?.status).toBe('completed');
    expect(projection.getSnapshot().streaming).toBe(false);
  });

  it('rehydrates interrupted thread runs without marking the thread as streaming', () => {
    const projection = createThreadProjection();

    projection.rehydrate({
      runs: [{
        runtimeRunId: 'run1',
        agentId: 'agent',
        sessionId: 's1',
        threadId: 'main',
        status: 'interrupted',
        startedAt: '2026-01-01T00:00:00.000Z',
        completedAt: null,
        error: null,
        backgroundOperation: null,
        backgroundTasks: [],
      }],
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.threadRun?.status).toBe('interrupted');
    expect(snapshot.streaming).toBe(false);
    expect(snapshot.canSend).toBe(true);
  });

  it('rehydrates background operation details from thread runs', () => {
    const projection = createThreadProjection();

    projection.rehydrate({
      activeRun: {
        runtimeRunId: 'run1',
        agentId: 'agent',
        sessionId: 's1',
        threadId: 'main',
        status: 'active',
        startedAt: '2026-01-01T00:00:00.000Z',
        completedAt: null,
        error: null,
        backgroundOperation: {
          status: 'InProgress',
          operationId: 'op1',
          statusMessage: 'Queued by provider',
          continuationToken: 'token',
        },
        backgroundTasks: [{
          taskId: 'task1',
          name: 'Long task',
          status: 'started',
          startedAt: '2026-01-01T00:00:01.000Z',
        }],
      },
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.threadRun?.status).toBe('active');
    expect(snapshot.threadRun?.backgroundOperation?.continuationToken).toBe('token');
    expect(snapshot.threadRun?.backgroundTasks).toHaveLength(1);
    expect(snapshot.streaming).toBe(true);
  });
});

describe('eventBelongsToScope', () => {
  it('matches events scoped to the same session and thread', () => {
    expect(eventBelongsToScope(
      { type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'x', sessionId: 's1', threadId: 'main' },
      { agentId: 'a1', sessionId: 's1', threadId: 'main' },
    )).toBe(true);
  });

  it('rejects events scoped to another thread', () => {
    expect(eventBelongsToScope(
      { type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'x', sessionId: 's1', threadId: 'other' },
      { agentId: 'a1', sessionId: 's1', threadId: 'main' },
    )).toBe(false);
  });

  it('rejects events scoped to another agent', () => {
    expect(eventBelongsToScope(
      { type: EventTypes.THREAD_RUN_STARTED, runtimeRunId: 'r1', agentId: 'a2', startedAt: 'now' },
      { agentId: 'a1', sessionId: 's1', threadId: 'main' },
    )).toBe(false);
  });

  it('requires an explicit option for scope-less events', () => {
    const event = { type: EventTypes.TEXT_DELTA, messageId: 'm1', text: 'x' };
    const scope = { agentId: 'a1', sessionId: 's1', threadId: 'main' };

    expect(eventBelongsToScope(event, scope)).toBe(false);
    expect(eventBelongsToScope(event, scope, { allowScopeLess: true })).toBe(true);
  });
});
