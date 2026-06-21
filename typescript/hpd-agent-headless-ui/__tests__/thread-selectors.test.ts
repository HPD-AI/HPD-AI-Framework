import { describe, expect, it } from 'vitest';
import {
  canSubmitText,
  getActiveToolCalls,
  getBlockingRuntimeRequests,
  getBranchChoiceLabel,
  getBranchChoicePosition,
  getInspectableRuntimeChildren,
  getLatestMessage,
  getLastAssistantMessage,
  getLastUserMessage,
  getMessageById,
  getMessageStatus,
  getParentThreadId,
  getPendingRuntimeRequests,
  getRuntimeChildGroups,
  getSubAgentRuntimeChildCount,
  getSubAgentRuntimeChildren,
  getTextSubmissionState,
  getThreadErrors,
  getThreadDisplayName,
  getThreadKindLabel,
  getLatestThreadError,
  getThreadTimeline,
  getThreadWorkGroups,
  getToolCallDuration,
  getTranscriptMessages,
  getVisibleRuntimeChildren,
  hasPendingRuntimeRequests,
  hasThreadErrors,
  hasActivePathChoices,
  hasForkGroups,
  hasSubAgentRuntimeChildren,
  isHiddenThread,
  isMainAgentThread,
  isSubAgentThread,
  isThreadBusy,
  isToolCallActive,
  isVisibleThread,
  type Message,
  type Thread,
  type ThreadBranchNavigationSnapshot,
  type ThreadProjectionSnapshot,
  type ToolCall,
} from '../src/index.js';

function snapshot(overrides: Partial<ThreadProjectionSnapshot> = {}): ThreadProjectionSnapshot {
  const state = {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    threadRun: null,
    activity: {
      status: 'idle' as const,
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    },
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: true,
    ...overrides,
  };
  return {
    ...state,
    activity: {
      status: state.error
        ? 'failed'
        : state.pendingRuntimeRequests.length > 0
          ? 'requesting'
          : state.threadRun?.status === 'active' || state.activeTools.length > 0 ||
              state.workGroups.some((work) => work.status === 'working')
            ? 'working'
            : 'idle',
      streaming: state.threadRun?.status === 'active' ||
        state.activeTools.length > 0 ||
        state.workGroups.some((work) => work.status === 'working'),
      reasoning: state.workGroups.some((work) =>
        work.parts.some((part) => part.type === 'reasoning' && part.status === 'streaming')),
      activeToolCount: state.activeTools.length,
      pendingRequestCount: state.pendingRuntimeRequests.length,
    },
  };
}

function message(overrides: Partial<Message> = {}): Message {
  return {
    id: 'm1',
    role: 'assistant',
    content: '',
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    runId: null,
    placement: 'transcript',
    ...overrides,
  };
}

function toolCall(overrides: Partial<ToolCall> = {}): ToolCall {
  return {
    callId: 'call1',
    name: 'Bash',
    messageId: 'a1',
    status: 'pending',
    startTime: new Date('2026-01-01T00:00:00.000Z'),
    turnId: null,
    conversationId: null,
    runId: null,
    ...overrides,
  };
}

function navigation(overrides: Partial<ThreadBranchNavigationSnapshot> = {}): ThreadBranchNavigationSnapshot {
  const graph = { threads: [], forkGroups: [], runtimeChildren: [] };
  return {
    sessionId: 's1',
    threadId: 'main',
    graph,
    current: null,
    threads: [],
    forkGroups: [],
    activePathChoices: [],
    runtimeChildren: [],
    hasRuntimeChildren: false,
    ...overrides,
  };
}

function thread(id: string, overrides: Partial<Thread> = {}): Thread {
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
    ...overrides,
  };
}

describe('thread selectors', () => {
  it('selects latest and transcript messages without exposing the message array', () => {
    const messages = [
      message({
        id: 'u1',
        role: 'user',
        content: 'hello',
        timestamp: new Date('2026-01-01T00:00:00.000Z'),
      }),
      message({
        id: 'a1',
        role: 'assistant',
        content: 'hi',
        timestamp: new Date('2026-01-01T00:00:01.000Z'),
      }),
    ];
    const state = snapshot({ transcriptMessages: messages });

    expect(getLatestMessage(state)?.id).toBe('a1');
    expect(getTranscriptMessages(state).map((message) => message.id)).toEqual(['u1', 'a1']);
    expect(getTranscriptMessages(state)).not.toBe(state.transcriptMessages);
    expect(getLatestMessage(snapshot())).toBeNull();
  });

  it('selects messages by role and id', () => {
    const state = snapshot({
      transcriptMessages: [
        message({ id: 'u1', role: 'user' }),
        message({ id: 'a1', role: 'assistant' }),
        message({ id: 'u2', role: 'user' }),
      ],
    });

    expect(getLastUserMessage(state)?.id).toBe('u2');
    expect(getLastAssistantMessage(state)?.id).toBe('a1');
    expect(getMessageById(state, 'u1')?.role).toBe('user');
    expect(getMessageById(state, 'missing')).toBeNull();
  });

  it('derives message status from thinking streaming and active tool calls', () => {
    expect(getMessageStatus(message({ thinking: true, streaming: true }))).toBe('thinking');
    expect(getMessageStatus(message({ streaming: true }))).toBe('streaming');
    expect(getMessageStatus(message({ toolCalls: [toolCall({ status: 'executing' })] }))).toBe('executing');
    expect(getMessageStatus(message({ toolCalls: [toolCall({ status: 'complete' })] }))).toBe('complete');
    expect(getMessageStatus(message())).toBe('complete');
  });

  it('selects active tool calls without exposing the active tool array', () => {
    const activeTools = [toolCall({ status: 'executing' })];
    const state = snapshot({ activeTools });

    expect(getActiveToolCalls(state)).toEqual(activeTools);
    expect(getActiveToolCalls(state)).not.toBe(state.activeTools);
  });

  it('derives tool active state and completed duration', () => {
    expect(isToolCallActive(toolCall({ status: 'pending' }))).toBe(true);
    expect(isToolCallActive(toolCall({ status: 'executing' }))).toBe(true);
    expect(isToolCallActive(toolCall({ status: 'complete' }))).toBe(false);
    expect(isToolCallActive(toolCall({ status: 'error' }))).toBe(false);
    expect(getToolCallDuration(toolCall({
      startTime: new Date('2026-01-01T00:00:00.000Z'),
      endTime: new Date('2026-01-01T00:00:02.500Z'),
    }))).toBe(2500);
    expect(getToolCallDuration(toolCall())).toBeNull();
  });

  it('combines pending runtime requests with stable discriminants', () => {
    const state = snapshot({
      pendingRuntimeRequests: [
        {
          id: 'p1',
          kind: 'permission',
          sourceName: 'permission',
          requestEventType: 'PERMISSION_REQUEST',
          request: {
            permissionId: 'p1',
            sourceName: 'permission',
            functionName: 'Bash',
            callId: 'call1',
          },
        },
        {
          id: 'c1',
          kind: 'clarification',
          sourceName: 'clarification',
          requestEventType: 'CLARIFICATION_REQUEST',
          request: {
            requestId: 'c1',
            sourceName: 'clarification',
            question: 'Which tenant?',
          },
        },
        {
          id: 't1',
          kind: 'client-tool',
          sourceName: 'HPD.Agent.ClientTools',
          requestEventType: 'CLIENT_TOOL_INVOKE_REQUEST',
          request: {
            requestId: 't1',
            toolName: 'pickFile',
            callId: 'call2',
            arguments: {},
          },
        },
      ],
    });

    expect(getPendingRuntimeRequests(state).map((request) => request.kind))
      .toEqual(['permission', 'clarification', 'client-tool']);
    expect(getPendingRuntimeRequests(state)[0]).toMatchObject({
      kind: 'permission',
      id: 'p1',
      request: { permissionId: 'p1' },
    });
    expect(hasPendingRuntimeRequests(state)).toBe(true);
    expect(getBlockingRuntimeRequests(state).map((request) => request.kind))
      .toEqual(['permission', 'clarification', 'client-tool']);
    expect(getBlockingRuntimeRequests(snapshot())).toEqual([]);
    expect(hasPendingRuntimeRequests(snapshot())).toBe(false);
  });

  it('selects timeline and work groups without exposing projection arrays', () => {
    const work = {
      id: 'turn:t1',
      turnId: 't1',
      conversationId: 'c1',
      runId: 'r1',
      status: 'worked' as const,
      label: 'Worked',
      openByDefault: false,
      parts: [],
    };
    const state = snapshot({
      workGroups: [work],
      timeline: [{
        type: 'work',
        id: 'timeline:turn:t1',
        work,
        turnId: 't1',
        conversationId: 'c1',
        runId: 'r1',
      }],
    });

    expect(getThreadWorkGroups(state).map((item) => item.id)).toEqual(['turn:t1']);
    expect(getThreadTimeline(state).map((item) => item.id)).toEqual(['timeline:turn:t1']);
    expect(getThreadWorkGroups(state)).not.toBe(state.workGroups);
    expect(getThreadTimeline(state)).not.toBe(state.timeline);
    expect(getThreadWorkGroups(state, { completedWork: 'hidden' })).toEqual([]);
    expect(getThreadTimeline(state, { completedWork: 'hidden' })).toEqual([]);
  });

  it('treats active work active runs tools and runtime requests as busy', () => {
    expect(isThreadBusy(snapshot())).toBe(false);
    expect(isThreadBusy(snapshot({
      workGroups: [{
        id: 'turn:t1',
        turnId: 't1',
        conversationId: 'c1',
        runId: null,
        status: 'working',
        label: 'Working',
        openByDefault: true,
        parts: [],
      }],
    }))).toBe(true);
    expect(isThreadBusy(snapshot({
      threadRun: {
        runtimeRunId: 'run1',
        agentId: 'agent',
        status: 'active',
      },
    }))).toBe(true);
    expect(isThreadBusy(snapshot({
      activeTools: [toolCall()],
    }))).toBe(true);
    expect(isThreadBusy(snapshot({
      pendingRuntimeRequests: [{
        id: 't1',
        kind: 'custom',
        sourceName: 'custom',
        requestEventType: 'CUSTOM_REQUEST',
      }],
    }))).toBe(true);
  });

  it('only allows text submission when the snapshot can send and is not busy', () => {
    expect(canSubmitText(snapshot())).toBe(true);
    expect(canSubmitText(snapshot({ canSend: false }))).toBe(false);
    expect(canSubmitText(snapshot({
      workGroups: [{
        id: 'turn:t1',
        turnId: 't1',
        conversationId: 'c1',
        runId: null,
        status: 'working',
        label: 'Working',
        openByDefault: true,
        parts: [],
      }],
    }))).toBe(false);
    expect(canSubmitText(snapshot({
      threadRun: {
        runtimeRunId: 'run1',
        agentId: 'agent',
        status: 'active',
      },
    }))).toBe(false);
  });

  it('explains text submission state for adapters', () => {
    expect(getTextSubmissionState(snapshot())).toEqual({ canSubmit: true, reason: null });
    expect(getTextSubmissionState(snapshot({ error: 'boom' }))).toEqual({
      canSubmit: false,
      reason: 'error',
    });
    expect(getTextSubmissionState(snapshot({
      workGroups: [{
        id: 'turn:t1',
        turnId: 't1',
        conversationId: 'c1',
        runId: null,
        status: 'working',
        label: 'Working',
        openByDefault: true,
        parts: [],
      }],
    }))).toEqual({
      canSubmit: false,
      reason: 'busy',
    });
    expect(getTextSubmissionState(snapshot({
      pendingRuntimeRequests: [{
        id: 'r1',
        kind: 'custom',
        sourceName: 'custom',
        requestEventType: 'CUSTOM_REQUEST',
      }],
    }))).toEqual({
      canSubmit: false,
      reason: 'runtime-request',
    });
    expect(getTextSubmissionState(snapshot({ canSend: false }))).toEqual({
      canSubmit: false,
      reason: 'not-sendable',
    });
  });

  it('normalizes thread errors for framework adapters', () => {
    const errors = getThreadErrors(snapshot({
      error: 'turn failed',
      threadRun: {
        runtimeRunId: 'run1',
        agentId: 'agent',
        status: 'failed',
        errorType: 'InvalidOperationException',
        errorMessage: 'model failed',
      },
      workGroups: [{
        id: 'turn:t1',
        turnId: 't1',
        conversationId: 'c1',
        runId: 'run1',
        status: 'failed',
        label: 'Work failed',
        openByDefault: true,
        error: 'work failed',
        parts: [{
          type: 'tool',
          id: 'tool:call1',
          tool: toolCall({
            status: 'error',
            error: 'tool failed',
            turnId: 't1',
            conversationId: 'c1',
            runId: 'run1',
          }),
        }],
      }],
    }));

    expect(errors.map((error) => [error.kind, error.message])).toEqual([
      ['run', 'model failed'],
      ['work', 'work failed'],
      ['tool', 'tool failed'],
      ['thread', 'turn failed'],
    ]);
    expect(getLatestThreadError(snapshot({ error: 'turn failed' }))).toMatchObject({
      kind: 'thread',
      message: 'turn failed',
    });
    expect(hasThreadErrors(snapshot({ error: 'turn failed' }))).toBe(true);
    expect(hasThreadErrors(snapshot())).toBe(false);
  });

  it('derives fork-group navigation display metadata', () => {
    const forkGroup = {
      id: 'main@m1',
      sourceThreadId: 'main',
      forkedAtMessageId: 'm1',
      forkedAtMessageIndex: 0,
      choiceMessageIndex: 1,
      members: [
        {
          threadId: 'main',
          name: 'Main',
          index: 0,
          isSource: true,
          messageCount: 3,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
        {
          threadId: 'alt',
          name: 'Alternative',
          index: 1,
          isSource: false,
          messageCount: 3,
          createdAt: '2026-01-01T00:00:00.000Z',
          lastActivity: '2026-01-01T00:00:00.000Z',
        },
      ],
    };
    const nav = navigation({
      threadId: 'alt',
      current: thread('alt', {
        name: 'Alternative',
        messageCount: 3,
        forkedFrom: 'main',
        forkedAtMessageId: 'm1',
        forkedAtMessageIndex: 0,
      }),
      forkGroups: [forkGroup],
      activePathChoices: [{
        group: forkGroup,
        selectedMember: forkGroup.members[1],
        selectedThreadId: 'alt',
        relationship: 'exact-member',
        previous: forkGroup.members[0],
        next: null,
        position: { current: 2, total: 2 },
      }],
    });

    expect(hasForkGroups(nav)).toBe(true);
    expect(hasActivePathChoices(nav)).toBe(true);
    expect(getBranchChoicePosition(nav.activePathChoices[0])).toEqual({ current: 2, total: 2 });
    expect(getBranchChoiceLabel(nav.activePathChoices[0])).toBe('Fork 2 / 2');
    expect(hasActivePathChoices(navigation())).toBe(false);
  });

  it('classifies thread metadata for subagent inspection UI', () => {
    const main = thread('main', { description: 'Primary conversation' });
    const subAgent = thread('subagent/reviewer/run-1', {
      name: undefined,
      kind: 'SubAgent',
      visibility: 'Hidden',
      parentSessionId: 's1',
      parentThreadId: 'main',
      subAgentName: 'Reviewer',
      subAgentRunId: 'run-1',
    });

    expect(isMainAgentThread(main)).toBe(true);
    expect(isSubAgentThread(main)).toBe(false);
    expect(isSubAgentThread(subAgent)).toBe(true);
    expect(isHiddenThread(subAgent)).toBe(true);
    expect(isVisibleThread(main)).toBe(true);
    expect(getParentThreadId(subAgent)).toBe('main');
    expect(getThreadDisplayName(subAgent)).toBe('Reviewer');
    expect(getThreadKindLabel(subAgent)).toBe('Subagent');
    expect(getThreadKindLabel(main)).toBe('Thread');
  });

  it('splits runtime children by subagent and visibility metadata', () => {
    const visibleChild = {
      threadId: 'child-visible',
      parentSessionId: 's1',
      parentThreadId: 'main',
      name: 'child-visible',
      kind: 'MainAgent' as const,
      visibility: 'Visible' as const,
      messageCount: 1,
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
    };
    const subAgentChild = {
      threadId: 'subagent/reviewer/run-1',
      parentSessionId: 's1',
      parentThreadId: 'main',
      name: 'Reviewer',
      kind: 'SubAgent',
      visibility: 'Hidden',
      subAgentName: 'Reviewer',
      messageCount: 1,
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
    } as const;
    const nav = navigation({
      current: thread('main'),
      runtimeChildren: [visibleChild, subAgentChild],
      hasRuntimeChildren: true,
    });

    expect(hasSubAgentRuntimeChildren(nav)).toBe(true);
    expect(getSubAgentRuntimeChildCount(nav)).toBe(1);
    expect(getSubAgentRuntimeChildren(nav).map((child) => child.threadId)).toEqual(['subagent/reviewer/run-1']);
    expect(getVisibleRuntimeChildren(nav).map((child) => child.threadId)).toEqual(['child-visible']);
    expect(getInspectableRuntimeChildren(nav).map((child) => child.threadId))
      .toEqual(['child-visible', 'subagent/reviewer/run-1']);
    expect(getRuntimeChildGroups(nav)).toEqual({
      subAgents: [subAgentChild],
      visible: [visibleChild],
      hidden: [subAgentChild],
    });
  });
});
