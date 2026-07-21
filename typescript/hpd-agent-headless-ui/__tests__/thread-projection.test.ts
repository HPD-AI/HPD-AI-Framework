import { describe, expect, it } from 'vitest';
import { EventTypes, type AgentEvent } from '@hpd-research/hpd-agent-client';
import { createThreadProjection, eventBelongsToScope } from '../src/index.js';

describe('createThreadProjection', () => {
  it('projects durable subagent invocation lifecycle and child routing identity', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.SUBAGENT_INVOCATION_STARTED,
      invocationId: 'inv-1',
      parentToolCallId: 'call-1',
      childAgentId: 'reviewer-agent',
      childSessionId: 'session-1',
      childThreadId: 'child-1',
      roleName: 'Reviewer',
      taskName: 'Review architecture',
      mode: 'Synchronous',
      timestamp: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.SUBAGENT_INVOCATION_COMPLETED,
      invocationId: 'inv-1',
      summary: 'Looks good',
      timestamp: '2026-01-01T00:00:01.000Z',
    });

    expect(projection.getSnapshot().subAgentInvocations).toEqual([{
      invocationId: 'inv-1',
      parentToolCallId: 'call-1',
      childAgentId: 'reviewer-agent',
      childSessionId: 'session-1',
      childThreadId: 'child-1',
      roleName: 'Reviewer',
      taskName: 'Review architecture',
      mode: 'Synchronous',
      status: 'completed',
      summary: 'Looks good',
      startedAt: '2026-01-01T00:00:00.000Z',
      completedAt: '2026-01-01T00:00:01.000Z',
    }]);
  });

  it('projects message turn usage into context usage and the completed work group', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.MESSAGE_TURN_STARTED,
      messageTurnId: 'turn-1',
      conversationId: 'conv-1',
      agentName: 'Agent',
      timestamp: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.MESSAGE_TURN_FINISHED,
      messageTurnId: 'turn-1',
      conversationId: 'conv-1',
      agentName: 'Agent',
      duration: 'PT1S',
      timestamp: '2026-01-01T00:00:01.000Z',
      usage: {
        inputTokenCount: 900,
        outputTokenCount: 100,
        totalTokenCount: 1000,
        cachedInputTokenCount: 250,
        reasoningTokenCount: 40,
      },
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.contextUsage).toMatchObject({
      turnId: 'turn-1',
      conversationId: 'conv-1',
      usage: {
        inputTokenCount: 900,
        outputTokenCount: 100,
        totalTokenCount: 1000,
        cachedInputTokenCount: 250,
        reasoningTokenCount: 40,
      },
    });
    expect(snapshot.workGroups[0].usage).toMatchObject({
      totalTokenCount: 1000,
    });
  });

  it('rehydrates settled thread events through the same path as live projection', () => {
    const events: AgentEvent[] = [
      {
        type: EventTypes.TEXT_MESSAGE_START,
        messageId: 'm1',
        role: 'user',
        source: 'UserInput',
        visibility: 'Transcript',
        additionalProperties: {
          quote: {
            text: 'quoted context',
            messageId: 'source-message',
          },
        },
        timestamp: '2026-01-01T00:00:00.000Z',
      },
      {
        type: EventTypes.CONTENT_ADDED,
        messageId: 'm1',
        role: 'user',
        content: { $type: 'text', text: 'hello' },
        timestamp: '2026-01-01T00:00:00.000Z',
      },
      {
        type: EventTypes.TEXT_MESSAGE_END,
        messageId: 'm1',
        timestamp: '2026-01-01T00:00:00.000Z',
      },
      {
        type: EventTypes.TEXT_MESSAGE_START,
        messageId: 'm2',
        role: 'assistant',
        source: 'AssistantOutput',
        visibility: 'Transcript',
        timestamp: '2026-01-01T00:00:01.000Z',
      },
      {
        type: EventTypes.CONTENT_ADDED,
        messageId: 'm2',
        role: 'assistant',
        content: { $type: 'reasoning', text: 'thinking' },
        timestamp: '2026-01-01T00:00:01.000Z',
      },
      {
        type: EventTypes.CONTENT_ADDED,
        messageId: 'm2',
        role: 'assistant',
        content: { $type: 'text', text: 'hi there' },
        timestamp: '2026-01-01T00:00:01.000Z',
      },
      {
        type: EventTypes.TEXT_MESSAGE_END,
        messageId: 'm2',
        timestamp: '2026-01-01T00:00:01.000Z',
      },
    ];

    const replayed = createThreadProjection();
    const live = createThreadProjection();
    replayed.rehydrate({ events });
    for (const event of events) live.project(event);

    const snapshot = replayed.getSnapshot();
    expect(snapshot.transcriptMessages).toEqual(live.getSnapshot().transcriptMessages);
    expect(snapshot.transcriptMessages).toHaveLength(2);
    expect(snapshot.transcriptMessages[0].content).toBe('hello');
    expect(snapshot.transcriptMessages[0].additionalProperties).toEqual({
      quote: {
        text: 'quoted context',
        messageId: 'source-message',
      },
    });
    expect(snapshot.transcriptMessages[1].content).toBe('hi there');
    expect(snapshot.transcriptMessages[1].reasoning).toBe('thinking');
    expect(snapshot.timeline.map((item) => item.type)).toEqual(['message', 'message']);
    expect(snapshot.activity.streaming).toBe(false);
    expect(snapshot.canSend).toBe(true);
  });

  it('projects text deltas outside a turn into a transcript message', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
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
    expect(snapshot.transcriptMessages[0].content).toBe('hello');
    expect(snapshot.transcriptMessages[0].streaming).toBe(false);
    expect(snapshot.activity.streaming).toBe(false);
  });

  it('does not render hidden policy messages during live projection or rehydration', () => {
    const events = [{
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'bg1',
      role: 'system',
      source: 'BackgroundNotification',
      visibility: 'Hidden',
      persistence: 'ThreadHistory',
      additionalProperties: {
        'hpd.message.source': 'BackgroundNotification',
        'hpd.message.visibility': 'Hidden',
        'hpd.message.persistence': 'ThreadHistory',
      },
    }, {
      type: EventTypes.TEXT_DELTA,
      messageId: 'bg1',
      text: '<background-task-notifications />',
    }, {
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'bg1',
    }] satisfies AgentEvent[];

    const live = createThreadProjection();
    for (const event of events) live.project(event);

    const hydrated = createThreadProjection();
    hydrated.rehydrate({ events });

    for (const snapshot of [live.getSnapshot(), hydrated.getSnapshot()]) {
      expect(snapshot.transcriptMessages).toEqual([]);
      expect(snapshot.timeline).toEqual([]);
      expect(snapshot.workGroups).toEqual([]);
      expect(snapshot.activity.streaming).toBe(false);
    }
  });

  it('preserves message policy on projected transcript messages', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'u1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'u1',
      text: 'hello',
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'u1',
      source: 'UserInput',
      visibility: 'Transcript',
      content: 'hello',
    }]);
  });

  it('reconciles an optimistic user row when durable input admission events arrive', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'optimistic:user:1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      clientInputId: 'client-1',
      optimistic: true,
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'optimistic:user:1',
      text: 'what tools do you have',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'optimistic:user:1',
    });

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'm-user-1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      clientInputId: 'client-1',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'm-user-1',
      text: 'what tools do you have',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'm-user-1',
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'm-user-1',
      role: 'user',
      content: 'what tools do you have',
      placement: 'transcript',
      clientInputId: 'client-1',
      streaming: false,
    }]);
    expect(snapshot.timeline).toHaveLength(1);
    expect(snapshot.timeline[0]).toMatchObject({
      type: 'message',
      id: 'message:m-user-1',
      message: { id: 'm-user-1' },
    });
  });

  it('keeps live user input in the transcript during a running turn', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.MESSAGE_TURN_STARTED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      timestamp: '2026-01-01T00:00:01.000Z',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'u1',
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'u1',
      text: 'list files',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'u1',
      eventFlowId: 'turn1',
    });

    let snapshot = projection.getSnapshot();
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'u1',
      role: 'user',
      content: 'list files',
      placement: 'transcript',
      turnId: 'turn1',
    }]);
    expect(snapshot.workGroups[0].parts).toEqual([]);
    expect(snapshot.timeline.map((item) => item.type)).toEqual(['work', 'message']);

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'Here are the files.',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'a1',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.MESSAGE_TURN_FINISHED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      duration: '00:00:01',
      timestamp: '2026-01-01T00:00:02.000Z',
    });

    snapshot = projection.getSnapshot();
    expect(snapshot.transcriptMessages.map((message) => message.content))
      .toEqual(['list files', 'Here are the files.']);
    expect(snapshot.timeline.map((item) => item.type)).toEqual(['work', 'message', 'message']);
  });

  it('projects durable message events live without duplicating runtime deltas', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'm1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      authorName: 'Agent',
      createdAt: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.CONTENT_ADDED,
        messageId: 'm1',
        role: 'assistant',
        content: { $type: 'text', text: 'hello' },
    });
    projection.project({
      type: EventTypes.CONTENT_ADDED,
        messageId: 'm1',
        role: 'assistant',
        content: { $type: 'text', text: 'hello' },
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'm1',
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'm1',
      role: 'assistant',
      authorName: 'Agent',
      content: 'hello',
      streaming: false,
    }]);
  });

  it('projects a turn into work, retains completed tools, and promotes final assistant text', () => {
    const projection = createThreadProjection();

    const events = [{
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
    }, {
      type: EventTypes.MESSAGE_TURN_STARTED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      timestamp: '2026-01-01T00:00:01.000Z',
    }, {
      type: EventTypes.REASONING_MESSAGE_START,
      messageId: 'r1',
      role: 'assistant',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.REASONING_DELTA,
      messageId: 'r1',
      text: 'thinking',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.REASONING_MESSAGE_END,
      messageId: 'r1',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'done',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.TOOL_CALL_START,
      callId: 'tool1',
      name: 'ReadFile',
      messageId: 'a1',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.TOOL_CALL_ARGS,
      callId: 'tool1',
      argsJson: '{"path":"README.md"}',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.TOOL_CALL_RESULT,
      callId: 'tool1',
      result: { text: 'contents' },
      eventFlowId: 'turn1',
    }] satisfies AgentEvent[];

    for (const event of events) projection.project(event);

    let snapshot = projection.getSnapshot();
    expect(snapshot.workGroups).toHaveLength(1);
    expect(snapshot.workGroups[0]).toMatchObject({
      turnId: 'turn1',
      conversationId: 'conv1',
      executionId: 'run1',
      status: 'working',
    });
    expect(snapshot.workGroups[0].parts.map((part) => part.type))
      .toEqual(['reasoning', 'assistant-draft', 'tool']);
    expect(snapshot.workGroups[0].parts.find((part) => part.type === 'tool')).toMatchObject({
      tool: {
        callId: 'tool1',
        status: 'complete',
        resultText: 'contents',
        turnId: 'turn1',
        executionId: 'run1',
      },
    });
    expect(snapshot.activeTools).toHaveLength(0);
    expect(snapshot.transcriptMessages).toEqual([]);

    const completionEvents = [{
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'a1',
      eventFlowId: 'turn1',
    }, {
      type: EventTypes.MESSAGE_TURN_FINISHED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      duration: '00:00:01',
      timestamp: '2026-01-01T00:00:02.000Z',
    }] satisfies AgentEvent[];

    for (const event of completionEvents) projection.project(event);

    snapshot = projection.getSnapshot();
    expect(snapshot.workGroups[0]).toMatchObject({
      status: 'worked',
      openByDefault: false,
      finalMessageId: 'a1',
    });
    expect(snapshot.transcriptMessages).toMatchObject([{
      id: 'a1',
      content: 'done',
      placement: 'final',
      turnId: 'turn1',
      conversationId: 'conv1',
      executionId: 'run1',
    }]);
    expect(snapshot.timeline.map((item) => item.type)).toEqual(['work', 'message']);

    const rehydrated = createThreadProjection();
    rehydrated.rehydrate({
      events: [...events, ...completionEvents],
      executions: [],
      activeExecution: null,
    });

    expect(rehydrated.getSnapshot().timeline).toEqual(snapshot.timeline);
    expect(rehydrated.getSnapshot().workGroups).toEqual(snapshot.workGroups);
    expect(rehydrated.getSnapshot().transcriptMessages).toEqual(snapshot.transcriptMessages);
  });

  it('keeps reasoning in work when reasoning and answer share a message id', () => {
    const events = [{
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
      timestamp: '2026-01-01T00:00:00.000Z',
    }, {
      type: EventTypes.MESSAGE_TURN_STARTED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      timestamp: '2026-01-01T00:00:01.000Z',
    }, {
      type: EventTypes.REASONING_MESSAGE_START,
      messageId: 'shared-message',
      role: 'assistant',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:02.000Z',
    }, {
      type: EventTypes.REASONING_DELTA,
      messageId: 'shared-message',
      text: 'private chain of thought',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:03.000Z',
    }, {
      type: EventTypes.REASONING_MESSAGE_END,
      messageId: 'shared-message',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:04.000Z',
    }, {
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'shared-message',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:05.000Z',
    }, {
      type: EventTypes.TEXT_DELTA,
      messageId: 'shared-message',
      text: 'public answer',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:06.000Z',
    }, {
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'shared-message',
      eventFlowId: 'turn1',
      timestamp: '2026-01-01T00:00:07.000Z',
    }, {
      type: EventTypes.MESSAGE_TURN_FINISHED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      duration: '00:00:01',
      timestamp: '2026-01-01T00:00:08.000Z',
    }] satisfies AgentEvent[];

    const live = createThreadProjection();
    for (const event of events) live.project(event);

    const hydrated = createThreadProjection();
    hydrated.rehydrate({ events, executions: [], activeExecution: null });

    for (const snapshot of [live.getSnapshot(), hydrated.getSnapshot()]) {
      expect(snapshot.timeline.map((item) => item.type)).toEqual(['work', 'message']);
      expect(snapshot.workGroups[0]).toMatchObject({
        label: 'Agent',
        status: 'worked',
      });
      expect(snapshot.workGroups[0].parts).toMatchObject([
        { type: 'reasoning', text: 'private chain of thought' },
        { type: 'assistant-draft', message: { content: 'public answer' } },
      ]);
      expect(snapshot.transcriptMessages).toMatchObject([{
        id: 'shared-message',
        content: 'public answer',
        placement: 'final',
      }]);
      expect(snapshot.transcriptMessages[0]).not.toHaveProperty('reasoning');
    }

    expect(hydrated.getSnapshot().timeline).toEqual(live.getSnapshot().timeline);
  });

  it('preserves multiple tool call order through completion and turn collapse', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
    });
    projection.project({
      type: EventTypes.MESSAGE_TURN_STARTED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      timestamp: '2026-01-01T00:00:01.000Z',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId: 'a1',
      role: 'assistant',
      source: 'AssistantOutput',
      visibility: 'Transcript',
      eventFlowId: 'turn1',
    });

    for (let index = 1; index <= 5; index += 1) {
      projection.project({
        type: EventTypes.TOOL_CALL_START,
        callId: `tool-${index}`,
        name: 'EditFile',
        messageId: 'a1',
        eventFlowId: 'turn1',
      });
      projection.project({
        type: EventTypes.TOOL_CALL_ARGS,
        callId: `tool-${index}`,
        argsJson: JSON.stringify({ index }),
        eventFlowId: 'turn1',
      });
    }

    expect(readWorkToolIds(projection.getSnapshot().workGroups[0]))
      .toEqual(['tool-1', 'tool-2', 'tool-3', 'tool-4', 'tool-5']);
    expect(projection.getSnapshot().activeTools.map((tool) => tool.callId))
      .toEqual(['tool-1', 'tool-2', 'tool-3', 'tool-4', 'tool-5']);

    for (let index = 5; index >= 1; index -= 1) {
      projection.project({
        type: EventTypes.TOOL_CALL_RESULT,
        callId: `tool-${index}`,
        result: { text: `result ${index}` },
        eventFlowId: 'turn1',
      });
    }

    let snapshot = projection.getSnapshot();
    expect(readWorkToolIds(snapshot.workGroups[0]))
      .toEqual(['tool-1', 'tool-2', 'tool-3', 'tool-4', 'tool-5']);
    expect(readWorkToolStatuses(snapshot.workGroups[0]))
      .toEqual(['complete', 'complete', 'complete', 'complete', 'complete']);
    expect(snapshot.activeTools).toEqual([]);

    projection.project({
      type: EventTypes.TEXT_DELTA,
      messageId: 'a1',
      text: 'Finished edits',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId: 'a1',
      eventFlowId: 'turn1',
    });
    projection.project({
      type: EventTypes.MESSAGE_TURN_FINISHED,
      messageTurnId: 'turn1',
      conversationId: 'conv1',
      agentName: 'Agent',
      duration: '00:00:01',
      timestamp: '2026-01-01T00:00:02.000Z',
    });

    snapshot = projection.getSnapshot();
    expect(snapshot.workGroups[0].openByDefault).toBe(false);
    expect(snapshot.workGroups[0].status).toBe('worked');
    expect(readWorkToolIds(snapshot.workGroups[0]))
      .toEqual(['tool-1', 'tool-2', 'tool-3', 'tool-4', 'tool-5']);
    expect(snapshot.timeline.map((item) => item.type)).toEqual(['work', 'message']);
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
    expect(projection.getSnapshot().pendingRuntimeRequests).toHaveLength(1);
    expect(projection.getSnapshot().pendingRuntimeRequests[0]).toMatchObject({
      id: 'p1',
      kind: 'permission',
      request: { permissionId: 'p1' },
    });

    projection.project({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId: 'p1',
      sourceName: 'permission',
      approved: true,
    });
    expect(projection.getSnapshot().pendingRuntimeRequests).toHaveLength(0);
  });

  it('tracks custom request events through the generic runtime request model', () => {
    const projection = createThreadProjection();

    projection.project({
      type: 'CUSTOM_REQUEST',
      requestId: 'custom-1',
      sourceName: 'custom-source',
      responsePolicy: 'targetedResponder',
      target: { responderGroup: 'reviewers' },
      visibility: 'eligibleRespondersOnly',
      timestamp: '2026-01-01T00:00:00.000Z',
    });

    expect(projection.getSnapshot().pendingRuntimeRequests[0]).toMatchObject({
      id: 'custom-1',
      kind: 'custom',
      sourceName: 'custom-source',
      requestEventType: 'CUSTOM_REQUEST',
      responsePolicy: 'targetedResponder',
      target: { responderGroup: 'reviewers' },
      visibility: 'eligibleRespondersOnly',
    });

    projection.project({
      type: 'CUSTOM_REVIEW_REQUEST',
      requestId: 'custom-2',
      sourceName: 'custom-source',
      responsePolicy: 'firstValidResponseWins',
      visibility: 'allObservers',
      prompt: 'Approve this custom operation?',
    });

    expect(projection.getSnapshot().pendingRuntimeRequests[1]).toMatchObject({
      id: 'custom-2',
      kind: 'custom',
      sourceName: 'custom-source',
      requestEventType: 'CUSTOM_REVIEW_REQUEST',
      responsePolicy: 'firstValidResponseWins',
      visibility: 'allObservers',
      event: {
        prompt: 'Approve this custom operation?',
      },
    });
  });

  it('tracks thread execution lifecycle', () => {
    const projection = createThreadProjection();

    projection.project({
      type: EventTypes.THREAD_EXECUTION_STARTED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      startedAt: '2026-01-01T00:00:00.000Z',
      sessionId: 's1',
      threadId: 'main',
    });
    expect(projection.getSnapshot().threadExecution?.status).toBe('active');
    expect(projection.getSnapshot().activity.streaming).toBe(true);

    projection.project({
      type: EventTypes.THREAD_EXECUTION_FINISHED,
      threadExecutionId: 'run1',
      agentId: 'agent',
      outcome: 'Succeeded',
      finishedAt: '2026-01-01T00:00:02.000Z',
      sessionId: 's1',
      threadId: 'main',
    });
    expect(projection.getSnapshot().threadExecution?.status).toBe('succeeded');
    expect(projection.getSnapshot().activity.streaming).toBe(false);
  });

  it('projects generic wire-level error events', () => {
    const projection = createThreadProjection();

    projection.project({
      type: 'CUSTOM_DOMAIN_ERROR',
      isError: true,
      errorMessage: 'custom failure',
      sessionId: 's1',
      threadId: 'main',
    });

    expect(projection.getSnapshot().error).toBe('custom failure');
    expect(projection.getSnapshot().canSend).toBe(false);
  });

  it('rehydrates interrupted thread executions without marking the thread as streaming', () => {
    const projection = createThreadProjection();

    projection.rehydrate({
      events: [],
      executions: [{
        threadExecutionId: 'run1',
        agentId: 'agent',
        sessionId: 's1',
        threadId: 'main',
        status: 'interrupted',
        startedAt: '2026-01-01T00:00:00.000Z',
        finishedAt: null,
        error: null,
        modelBackgroundOperation: null,
        backgroundTasks: [],
        backgroundHandles: [],
      }],
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.threadExecution?.status).toBe('interrupted');
    expect(snapshot.activity.streaming).toBe(false);
    expect(snapshot.canSend).toBe(true);
  });

  it('rehydrates background operation details from thread executions', () => {
    const projection = createThreadProjection();

    projection.rehydrate({
      events: [],
      activeExecution: {
        threadExecutionId: 'run1',
        agentId: 'agent',
        sessionId: 's1',
        threadId: 'main',
        status: 'active',
        startedAt: '2026-01-01T00:00:00.000Z',
        finishedAt: null,
        error: null,
        modelBackgroundOperation: {
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
        backgroundHandles: [{
          handleId: 'handle1',
          name: 'Long process',
          handleKind: 'Process',
          sourceKind: 'Command',
          status: 'running',
          supportedOperations: 'Status, Read, Stop',
          registeredAt: '2026-01-01T00:00:01.000Z',
        }],
      },
    });

    const snapshot = projection.getSnapshot();
    expect(snapshot.threadExecution?.status).toBe('active');
    expect(snapshot.threadExecution?.modelBackgroundOperation?.continuationToken).toBe('token');
    expect(snapshot.threadExecution?.backgroundTasks).toHaveLength(1);
    expect(snapshot.threadExecution?.backgroundHandles?.[0]?.handleId).toBe('handle1');
    expect(snapshot.activity.streaming).toBe(true);
  });
});

function readWorkToolIds(work: ReturnType<ReturnType<typeof createThreadProjection>['getSnapshot']>['workGroups'][number]): string[] {
  return work.parts
    .filter((part) => part.type === 'tool')
    .map((part) => part.tool.callId);
}

function readWorkToolStatuses(work: ReturnType<ReturnType<typeof createThreadProjection>['getSnapshot']>['workGroups'][number]): string[] {
  return work.parts
    .filter((part) => part.type === 'tool')
    .map((part) => part.tool.status);
}

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
      { type: EventTypes.THREAD_EXECUTION_STARTED, threadExecutionId: 'r1', agentId: 'a2', startedAt: 'now' },
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
