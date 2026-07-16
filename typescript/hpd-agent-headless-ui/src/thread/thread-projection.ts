import {
  EventTypes,
  AgentMessagePolicyProperties,
  formatToolResultPayload,
  isAgentRequestEvent,
  isAgentResponseEvent,
  isErrorEvent,
  type AgentMessagePersistence,
  type AgentMessageSource,
  type AgentMessageVisibility,
  type AIContent,
  type AgentEvent,
  type AgentRequestEvent,
  type KnownAgentEvent,
  type ThreadRun,
} from '@hpd-research/hpd-agent-client';
import type {
  ClientToolRequest,
  ClarificationRequest,
  Message,
  MessagePlacement,
  MessageRole,
  PermissionRequest,
  RuntimeRequest,
  RuntimeRequestBase,
  ThreadActivity,
  ThreadContextUsage,
  ThreadProjection,
  ThreadProjectionListener,
  ThreadProjectionSnapshot,
  ThreadRunView,
  ThreadSnapshot,
  ThreadTimelineItem,
  ThreadWorkGroup,
  ThreadWorkPart,
  ToolCall,
  Unsubscribe,
} from './types.js';

interface ProjectionContext {
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
  timestamp?: string;
}

interface ProjectionEventContext {
  eventFlowId?: string;
  sequenceNumber?: number;
  timestamp?: string;
}

interface MessagePolicy {
  source?: AgentMessageSource;
  visibility?: AgentMessageVisibility;
  persistence?: AgentMessagePersistence;
}

const missingEventTimestamp = new Date(0);

export function createThreadProjection(): ThreadProjection {
  return new ThreadProjectionImpl();
}

class ThreadProjectionImpl implements ThreadProjection {
  private snapshot: ThreadProjectionSnapshot = createInitialSnapshot();
  private listeners = new Set<ThreadProjectionListener>();
  private muted = false;

  getSnapshot(): ThreadProjectionSnapshot {
    return cloneSnapshot(this.snapshot);
  }

  subscribe(listener: ThreadProjectionListener): Unsubscribe {
    this.listeners.add(listener);
    listener(this.getSnapshot());
    return () => {
      this.listeners.delete(listener);
    };
  }

  rehydrate(snapshot: ThreadSnapshot): void {
    const threadRun = selectThreadRun(snapshot.activeRun, snapshot.runs);
    this.snapshot = {
      ...createInitialSnapshot(),
      thread: snapshot.thread ?? this.snapshot.thread,
      threadRun,
      currentRunId: threadRun?.status === 'active' ? threadRun.runtimeRunId : null,
      error: threadRun?.status === 'failed' ? threadRun.errorMessage ?? 'Thread run failed' : null,
    };
    this.snapshot = refreshSnapshot(this.snapshot);

    this.muted = true;
    try {
      for (const event of snapshot.events) {
        this.project(event);
      }
    } finally {
      this.muted = false;
    }

    const replayedThreadRun = selectThreadRun(snapshot.activeRun, snapshot.runs);
    if (replayedThreadRun) {
      const replayedRunIsActive = replayedThreadRun.status === 'active';
      this.snapshot = refreshSnapshot({
        ...this.snapshot,
        threadRun: replayedThreadRun,
        currentRunId: replayedRunIsActive
          ? this.snapshot.currentRunId ?? replayedThreadRun.runtimeRunId
          : this.snapshot.currentRunId,
        error: replayedThreadRun.status === 'failed'
          ? replayedThreadRun.errorMessage ?? 'Thread run failed'
          : this.snapshot.error,
      });
    }

    this.emit();
  }

  project(event: AgentEvent): void {
    const known = event as KnownAgentEvent;

    switch (known.type) {
      case EventTypes.CONTENT_ADDED:
        this.onContentAdded(known, known.messageId, known.content);
        break;
      case EventTypes.TEXT_MESSAGE_START:
        this.onTextMessageStart(known, known.messageId, known.role);
        break;
      case EventTypes.TEXT_DELTA:
        this.onTextDelta(known.messageId, known.text);
        break;
      case EventTypes.TEXT_MESSAGE_END:
        this.onTextMessageEnd(known.messageId);
        break;
      case EventTypes.REASONING_MESSAGE_START:
        this.onReasoningMessageStart(known, known.messageId);
        break;
      case EventTypes.REASONING_DELTA:
        this.onReasoningDelta(known, known.messageId, known.text);
        break;
      case EventTypes.REASONING_MESSAGE_END:
        this.onReasoningMessageEnd(known.messageId);
        break;
      case EventTypes.TOOL_CALL_START:
        this.onToolCallStart(known, {
          callId: known.callId,
          name: known.name,
          messageId: known.messageId,
          toolharnessName: known.toolharnessName,
          callType: known.callType,
        });
        break;
      case EventTypes.TOOL_CALL_ARGS:
        this.onToolCallArgs(known.callId, known.argsJson);
        break;
      case EventTypes.TOOL_CALL_RESULT:
        this.onToolCallResult(known);
        break;
      case EventTypes.TOOL_CALL_END:
        this.onToolCallEnd(known);
        break;
      case EventTypes.PERMISSION_REQUEST:
        this.addPermission({
          permissionId: known.permissionId,
          sourceName: known.sourceName,
          functionName: known.functionName,
          description: known.description,
          callId: known.callId,
          arguments: known.arguments,
        }, known);
        break;
      case EventTypes.PERMISSION_RESPONSE:
        this.removePendingRequest(known.permissionId);
        break;
      case EventTypes.CLARIFICATION_REQUEST:
        this.addClarification({
          requestId: known.requestId,
          sourceName: known.sourceName,
          question: known.question,
          agentName: known.agentName,
          options: known.options,
        }, known);
        break;
      case EventTypes.CLARIFICATION_RESPONSE:
        this.removeClarification(known.requestId);
        break;
      case EventTypes.CLIENT_TOOL_INVOKE_REQUEST:
        this.addClientToolRequest({
          requestId: known.requestId,
          sourceName: known.sourceName,
          toolName: known.toolName,
          callId: known.callId,
          arguments: known.arguments,
          description: known.description,
          responsePolicy: known.responsePolicy,
          target: known.target,
          visibility: known.visibility,
        }, known);
        break;
      case EventTypes.CLIENT_TOOL_INVOKE_OUTCOME:
        this.removeClientToolRequest(known.requestId);
        break;
      case EventTypes.MESSAGE_TURN_STARTED:
        this.startWorkGroup({
          turnId: known.messageTurnId,
          conversationId: known.conversationId,
          runId: this.snapshot.currentRunId,
          eventFlowId: known.eventFlowId,
          sequenceNumber: known.threadSequenceNumber,
        }, known.agentName, known.timestamp);
        break;
      case EventTypes.MESSAGE_TURN_FINISHED:
        this.finishWorkGroup(known.messageTurnId, 'worked', known.timestamp, undefined, known.usage);
        this.snapshot = refreshSnapshot({
          ...this.snapshot,
          currentTurnId: null,
          currentConversationId: known.conversationId,
        });
        this.emit();
        break;
      case EventTypes.MESSAGE_TURN_ERROR:
        this.finishWorkGroup(
          this.snapshot.currentTurnId ?? event.eventFlowId ?? null,
          'failed',
          event.timestamp,
          known.errorMessage,
        );
        this.snapshot = refreshSnapshot({
          ...this.snapshot,
          error: known.errorMessage,
        });
        this.emit();
        break;
      case EventTypes.THREAD_RUN_STARTED:
        this.snapshot = refreshSnapshot({
          ...this.snapshot,
          threadRun: {
            runtimeRunId: known.runtimeRunId,
            agentId: known.agentId,
            status: 'active',
            startedAt: known.startedAt,
          },
          currentRunId: known.runtimeRunId,
          error: null,
        });
        this.emit();
        break;
      case EventTypes.THREAD_RUN_COMPLETED: {
        const status = known.errorType
          ? 'failed'
          : known.cancelled
            ? 'cancelled'
            : 'completed';
        const currentRun = this.snapshot.threadRun;
        if (status === 'failed' || status === 'cancelled') {
          this.finishWorkGroup(this.snapshot.currentTurnId, status, event.timestamp, known.errorMessage ?? null);
        }
        this.snapshot = refreshSnapshot({
          ...this.snapshot,
          threadRun: {
            runtimeRunId: known.runtimeRunId,
            agentId: known.agentId,
            status,
            startedAt: currentRun && currentRun.runtimeRunId === known.runtimeRunId
              ? currentRun.startedAt
              : undefined,
            errorType: known.errorType,
            errorMessage: known.errorMessage,
          },
          currentRunId: null,
          error: known.errorMessage ?? this.snapshot.error,
        });
        this.emit();
        break;
      }
      default:
        if (isErrorEvent(event)) {
          this.snapshot = refreshSnapshot({
            ...this.snapshot,
            error: event.errorMessage,
          });
          this.emit();
        } else if (isAgentResponseEvent(event)) {
          this.removePendingRequest(event.requestId);
        } else if (isAgentRequestEvent(event)) {
          this.addCustomRequest(event);
        }
        break;
    }
  }

  clearError(): void {
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      error: null,
    });
    this.emit();
  }

  reset(): void {
    this.snapshot = createInitialSnapshot();
    this.emit();
  }

  private startWorkGroup(context: ProjectionContext, agentName?: string, startedAt?: string): void {
    const work = createWorkGroup(context, agentName, startedAt);
    const workGroups = upsertWorkGroup(this.snapshot.workGroups, work);
    const timeline = upsertTimelineItem(this.snapshot.timeline, createWorkTimelineItem(work));
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      workGroups,
      timeline,
      currentTurnId: context.turnId,
      currentConversationId: context.conversationId,
      currentRunId: context.runId,
      error: null,
    });
    this.emit();
  }

  private finishWorkGroup(
    turnId: string | null,
    status: ThreadWorkGroup['status'],
    completedAt?: string,
    error?: string | null,
    usage?: ThreadContextUsage['usage'] | null,
  ): void {
    const work = findCurrentWorkGroup(this.snapshot, turnId);
    if (!work) return;

    const finalMessage = findFinalAssistantDraft(work);
    const updatedWork: ThreadWorkGroup = {
      ...work,
      status,
      openByDefault: false,
      completedAt: completedAt ?? new Date().toISOString(),
      error,
      finalMessageId: finalMessage?.id ?? work.finalMessageId,
      usage: usage ?? work.usage,
    };

    let transcriptMessages = this.snapshot.transcriptMessages;
    let timeline = upsertTimelineItem(this.snapshot.timeline, createWorkTimelineItem(updatedWork));

    if (finalMessage && status === 'worked') {
      const promoted = {
        ...finalMessage,
        streaming: false,
        thinking: false,
        placement: 'final' as const,
      };
      transcriptMessages = upsertTranscriptMessage(transcriptMessages, promoted);
      timeline = upsertTimelineItem(timeline, createMessageTimelineItem(promoted));
    }

    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      workGroups: upsertWorkGroup(this.snapshot.workGroups, updatedWork),
      transcriptMessages,
      timeline,
      contextUsage: usage
        ? {
            usage,
            turnId: work.turnId,
            conversationId: work.conversationId,
            runId: work.runId,
            updatedAt: completedAt,
          }
        : this.snapshot.contextUsage,
    });
  }

  private onTextMessageStart(event: AgentEvent, messageId: string, role: string): void {
    const context = this.createContext(event);
    const policy = readMessagePolicy(event);
    if (policy.visibility === 'Hidden' || !policy.visibility) return;

    const clientInputId = readStringProperty(event, 'clientInputId') ?? null;
    const placement = readBooleanProperty(event, 'optimistic')
      ? 'optimistic'
      : resolveMessagePlacement(context, policy);
    const message = {
      ...createMessage(messageId, role, context, placement, clientInputId, readEventTimestamp(event), policy),
      additionalProperties: readRecordProperty(event, 'additionalProperties') ?? undefined,
      authorName: readStringProperty(event, 'authorName') ?? undefined,
    };
    const streamingMessage = {
      ...message,
      streaming: true,
      thinking: false,
    };

    if (placement === 'transcript' || placement === 'optimistic') {
      const { transcriptMessages, timeline } = upsertTranscriptMessageAndTimeline(
        this.snapshot.transcriptMessages,
        this.snapshot.timeline,
        streamingMessage,
      );
      this.snapshot = refreshSnapshot({
        ...this.snapshot,
        transcriptMessages,
        timeline,
        error: null,
      });
    } else {
      this.putWorkPart(context, {
        type: 'assistant-draft',
        id: `draft:${messageId}`,
        message: streamingMessage,
      });
      this.snapshot = refreshSnapshot({ ...this.snapshot, error: null });
    }
    this.emit();
  }

  private startMessageFromEvent(event: AgentEvent, messageId: string, role: string, authorName?: string): void {
    if (messageExists(this.snapshot, messageId)) return;

    const context = this.createContext(event);
    const policy = readMessagePolicy(event);
    if (policy.visibility === 'Hidden' || !policy.visibility) return;

    const clientInputId = readStringProperty(event, 'clientInputId') ?? null;
    const placement = readBooleanProperty(event, 'optimistic')
      ? 'optimistic'
      : resolveMessagePlacement(context, policy);
    const message = {
      ...createMessage(messageId, role, context, placement, clientInputId, readEventTimestamp(event), policy),
      additionalProperties: readRecordProperty(event, 'additionalProperties') ?? undefined,
      authorName,
    };

    if (placement === 'transcript' || placement === 'optimistic') {
      const { transcriptMessages, timeline } = upsertTranscriptMessageAndTimeline(
        this.snapshot.transcriptMessages,
        this.snapshot.timeline,
        message,
      );
      this.snapshot = refreshSnapshot({
        ...this.snapshot,
        transcriptMessages,
        timeline,
        error: null,
      });
    } else {
      this.putWorkPart(context, {
        type: 'assistant-draft',
        id: `draft:${messageId}`,
        message,
      });
      this.snapshot = refreshSnapshot({ ...this.snapshot, error: null });
    }
    this.emit();
  }

  private onContentAdded(event: AgentEvent, messageId: string, content: unknown): void {
    if (!messageExists(this.snapshot, messageId)) {
      this.startMessageFromEvent(
        event,
        messageId,
        readStringProperty(event, 'role') ?? 'assistant',
        readStringProperty(event, 'authorName') ?? undefined);
    }

    const contentType = readContentType(content);
    if (contentType === 'text') {
      const text = readStringProperty(content, 'text');
      if (text) this.appendMessageText(messageId, text);
      return;
    }

    if (contentType === 'reasoning') {
      const text = readStringProperty(content, 'text');
      if (text) this.appendMessageReasoning(messageId, text);
      return;
    }

    if (isAIContent(content)) {
      this.appendMessageContent(messageId, content);
    }
  }

  private appendMessageText(messageId: string, text: string): void {
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, messageId, (message) => ({
      ...message,
      content: appendUniqueText(message.content, text),
      contents: [...message.contents, { $type: 'text', text }],
    })));
    this.emit();
  }

  private appendMessageContent(messageId: string, content: AIContent): void {
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, messageId, (message) => ({
      ...message,
      contents: [...message.contents, content],
    })));
    this.emit();
  }

  private appendMessageReasoning(messageId: string, text: string): void {
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, messageId, (message) => ({
      ...message,
      reasoning: appendUniqueText(message.reasoning ?? '', text),
      thinking: false,
    })));
    this.emit();
  }

  private onTextDelta(messageId: string, text: string): void {
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, messageId, (message) => ({
      ...message,
      content: message.content + text,
      contents: [...message.contents, { $type: 'text', text }],
    })));
    this.emit();
  }

  private onTextMessageEnd(messageId: string): void {
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, messageId, (message) => ({
      ...message,
      streaming: false,
    })));
    this.emit();
  }

  private onReasoningMessageStart(event: AgentEvent, messageId: string): void {
    const context = this.createContext(event);
    this.putWorkPart(context, {
      type: 'reasoning',
      id: `reasoning:${messageId}`,
      messageId,
      text: '',
      status: 'streaming',
      eventFlowId: context.eventFlowId,
      sequenceNumber: context.sequenceNumber,
    });
    this.snapshot = refreshSnapshot({ ...this.snapshot, error: null });
    this.emit();
  }

  private onReasoningDelta(event: AgentEvent, messageId: string, text: string): void {
    const context = this.createContext(event);
    this.putWorkPart(context, {
      type: 'reasoning',
      id: `reasoning:${messageId}`,
      messageId,
      text,
      status: 'streaming',
      eventFlowId: context.eventFlowId,
      sequenceNumber: context.sequenceNumber,
    }, appendReasoningPart);
    this.emit();
  }

  private onReasoningMessageEnd(messageId: string): void {
    this.snapshot = refreshSnapshot(updateWorkPart(this.snapshot, `reasoning:${messageId}`, (part) =>
      part.type === 'reasoning' ? { ...part, status: 'complete' } : part));
    this.emit();
  }

  private onToolCallStart(event: AgentEvent, input: {
    callId: string;
    name: string;
    messageId: string;
    toolharnessName?: string;
    callType?: ToolCall['callType'];
  }): void {
    const context = this.createContext(event);
    const toolCall: ToolCall = {
      callId: input.callId,
      name: input.name,
      messageId: input.messageId,
      status: 'pending',
      startTime: readEventTimestamp(event),
      toolharnessName: input.toolharnessName,
      callType: input.callType,
      turnId: context.turnId,
      conversationId: context.conversationId,
      runId: context.runId,
      eventFlowId: context.eventFlowId,
      sequenceNumber: context.sequenceNumber,
      groupKey: input.toolharnessName ?? input.callType ?? input.name,
    };

    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      activeTools: [
        ...this.snapshot.activeTools.filter((tool) => tool.callId !== input.callId),
        toolCall,
      ],
    });
    this.putWorkPart(context, {
      type: 'tool',
      id: `tool:${input.callId}`,
      tool: toolCall,
    });
    this.snapshot = refreshSnapshot(updateMessageEverywhere(this.snapshot, input.messageId, (message) => ({
      ...message,
      toolCalls: [
        ...message.toolCalls.filter((tool) => tool.callId !== input.callId),
        toolCall,
      ],
    })));
    this.emit();
  }

  private onToolCallArgs(callId: string, argsJson: string): void {
    let args: unknown;
    let status: ToolCall['status'] = 'executing';
    let error: string | undefined;

    try {
      args = JSON.parse(argsJson);
    } catch {
      status = 'error';
      error = 'Invalid arguments';
    }

    this.replaceToolCall(callId, (tool) => ({
      ...tool,
      args,
      status,
      error,
    }));
  }

  private onToolCallResult(event: Extract<KnownAgentEvent, { type: typeof EventTypes.TOOL_CALL_RESULT }>): void {
    this.replaceToolCall(event.callId, (tool) => ({
      ...tool,
      name: event.name ?? tool.name,
      result: event.result,
      resultText: formatToolResultPayload(event.result),
      status: 'complete',
      endTime: readEventTimestamp(event),
      toolharnessName: event.toolharnessName ?? tool.toolharnessName,
      callType: event.callType ?? tool.callType,
    }));
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      activeTools: this.snapshot.activeTools.filter((tool) => tool.callId !== event.callId),
    });
    this.emit();
  }

  private onToolCallEnd(event: Extract<KnownAgentEvent, { type: typeof EventTypes.TOOL_CALL_END }>): void {
    this.replaceToolCall(event.callId, (tool) => ({
      ...tool,
      status: tool.status === 'complete' ? tool.status : 'complete',
      endTime: tool.endTime ?? readEventTimestamp(event),
    }));
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      activeTools: this.snapshot.activeTools.filter((tool) => tool.callId !== event.callId),
    });
    this.emit();
  }

  private replaceToolCall(callId: string, updater: (tool: ToolCall) => ToolCall): void {
    const activeTools = this.snapshot.activeTools.map((tool) =>
      tool.callId === callId ? updater(tool) : tool);

    const withMessages = updateAllMessages(this.snapshot, (message) => ({
      ...message,
      toolCalls: message.toolCalls.map((tool) =>
        tool.callId === callId ? updater(tool) : tool),
    }));

    const workGroups = withMessages.workGroups.map((work) => ({
      ...work,
      parts: work.parts.map((part) =>
        part.type === 'tool' && part.tool.callId === callId
          ? { ...part, tool: updater(part.tool) }
          : part),
    }));

    this.snapshot = refreshSnapshot({
      ...withMessages,
      activeTools,
      workGroups,
      timeline: syncWorkTimeline(withMessages.timeline, workGroups),
    });
    this.emit();
  }

  private addPermission(request: PermissionRequest, event?: AgentEvent): void {
    const base = this.createRequestBase({
      id: request.permissionId,
      sourceName: request.sourceName,
      requestEventType: event?.type ?? EventTypes.PERMISSION_REQUEST,
    });
    this.addRuntimeRequest({
      ...base,
      kind: 'permission',
      request,
      event,
    }, event);
  }

  private addClarification(request: ClarificationRequest, event?: AgentEvent): void {
    const base = this.createRequestBase({
      id: request.requestId,
      sourceName: request.sourceName,
      requestEventType: event?.type ?? EventTypes.CLARIFICATION_REQUEST,
    });
    this.addRuntimeRequest({
      ...base,
      kind: 'clarification',
      request,
      event,
    }, event);
  }

  private removeClarification(requestId: string): void {
    this.removePendingRequest(requestId);
  }

  private addClientToolRequest(request: ClientToolRequest, event?: AgentEvent): void {
    const base = this.createRequestBase({
      id: request.requestId,
      sourceName: request.sourceName ?? 'HPD.Agent.ClientTools',
      requestEventType: event?.type ?? EventTypes.CLIENT_TOOL_INVOKE_REQUEST,
      responsePolicy: request.responsePolicy,
      target: request.target,
      visibility: request.visibility,
    });
    this.addRuntimeRequest({
      ...base,
      kind: 'client-tool',
      request,
      event,
    }, event);
  }

  private removeClientToolRequest(requestId: string): void {
    this.removePendingRequest(requestId);
  }

  private addRuntimeRequest(request: RuntimeRequest, event?: ProjectionEventContext): void {
    const context = this.createContext(event);
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      pendingRuntimeRequests: upsertRuntimeRequest(this.snapshot.pendingRuntimeRequests, request),
      timeline: upsertTimelineItem(this.snapshot.timeline, {
        type: 'runtime-request',
        id: `request:${request.id}`,
        request,
        turnId: context.turnId,
        conversationId: context.conversationId,
        runId: context.runId,
      }),
    });
    this.emit();
  }

  private removePendingRequest(requestId: string): void {
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      pendingRuntimeRequests: this.snapshot.pendingRuntimeRequests.filter((item) => item.id !== requestId),
      timeline: this.snapshot.timeline.filter((item) =>
        item.type !== 'runtime-request' || item.request.id !== requestId),
    });
    this.emit();
  }

  private addCustomRequest(event: AgentRequestEvent): void {
    const base = this.createRequestBase({
      id: event.requestId,
      sourceName: event.sourceName,
      requestEventType: event.type,
      responsePolicy: event.responsePolicy,
      target: event.target,
      visibility: event.visibility,
    });

    this.addRuntimeRequest({
      ...base,
      kind: 'custom',
      event,
    }, event);
  }

  private createRequestBase(input: {
    id: string;
    sourceName: string;
    requestEventType: string;
    expectedResponseEventType?: string;
    responsePolicy?: RuntimeRequestBase['responsePolicy'];
    target?: RuntimeRequestBase['target'];
    visibility?: RuntimeRequestBase['visibility'];
    startedAt?: string;
  }): RuntimeRequestBase {
    const existing = this.snapshot.pendingRuntimeRequests.find((item) => item.id === input.id);
    return {
      id: input.id,
      kind: existing?.kind ?? 'custom',
      sourceName: input.sourceName,
      requestEventType: input.requestEventType,
      expectedResponseEventType: input.expectedResponseEventType ?? existing?.expectedResponseEventType,
      responsePolicy: input.responsePolicy ?? existing?.responsePolicy,
      target: input.target ?? existing?.target,
      visibility: input.visibility ?? existing?.visibility,
      startedAt: input.startedAt ?? existing?.startedAt,
    };
  }

  private createContext(event?: ProjectionEventContext): ProjectionContext {
    return {
      turnId: event?.eventFlowId ?? this.snapshot.currentTurnId ?? null,
      conversationId: this.snapshot.currentConversationId,
      runId: this.snapshot.currentRunId ?? this.snapshot.threadRun?.runtimeRunId ?? null,
      eventFlowId: event?.eventFlowId,
      sequenceNumber: event?.sequenceNumber,
      timestamp: event?.timestamp,
    };
  }

  private putWorkPart(
    context: ProjectionContext,
    part: ThreadWorkPart,
    merge: (existing: ThreadWorkPart, next: ThreadWorkPart) => ThreadWorkPart = (_existing, next) => next,
  ): void {
    const work = findCurrentWorkGroup(this.snapshot, context.turnId) ??
      createWorkGroup(context, undefined, context.timestamp ?? missingEventTimestamp.toISOString());
    const parts = upsertWorkPart(work.parts, part, merge);
    const updatedWork: ThreadWorkGroup = {
      ...work,
      parts,
      status: work.status === 'worked' ? 'working' : work.status,
      openByDefault: work.status === 'working' ? work.openByDefault : true,
    };
    const workGroups = upsertWorkGroup(this.snapshot.workGroups, updatedWork);
    this.snapshot = refreshSnapshot({
      ...this.snapshot,
      workGroups,
      timeline: upsertTimelineItem(this.snapshot.timeline, createWorkTimelineItem(updatedWork)),
      currentTurnId: context.turnId ?? this.snapshot.currentTurnId,
      currentConversationId: context.conversationId ?? this.snapshot.currentConversationId,
      currentRunId: context.runId ?? this.snapshot.currentRunId,
    });
  }

  private emit(): void {
    if (this.muted) return;
    const snapshot = this.getSnapshot();
    for (const listener of this.listeners) {
      listener(snapshot);
    }
  }
}

function createInitialSnapshot(): ThreadProjectionSnapshot {
  return refreshSnapshot({
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    contextUsage: null,
    threadRun: null,
    activity: createActivity({
      workGroups: [],
      activeTools: [],
      pendingRuntimeRequests: [],
      threadRun: null,
      error: null,
    }),
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: true,
  });
}

function refreshSnapshot(snapshot: ThreadProjectionSnapshot): ThreadProjectionSnapshot {
  const activity = createActivity(snapshot);
  return {
    ...snapshot,
    activity,
    canSend: activity.status === 'idle' && snapshot.error === null,
  };
}

function createActivity(snapshot: Pick<
  ThreadProjectionSnapshot,
  'workGroups' | 'activeTools' | 'pendingRuntimeRequests' | 'threadRun' | 'error'
>): ThreadActivity {
  const working = snapshot.workGroups.some((work) => work.status === 'working') ||
    snapshot.threadRun?.status === 'active' ||
    snapshot.activeTools.length > 0;
  const reasoning = snapshot.workGroups.some((work) =>
    work.parts.some((part) => part.type === 'reasoning' && part.status === 'streaming'));
  const failed = snapshot.error !== null || snapshot.threadRun?.status === 'failed';
  const cancelled = snapshot.threadRun?.status === 'cancelled';

  return {
    status: failed
      ? 'failed'
      : cancelled
        ? 'cancelled'
        : snapshot.pendingRuntimeRequests.length > 0
          ? 'requesting'
          : working
            ? 'working'
            : 'idle',
    streaming: working,
    reasoning,
    activeToolCount: snapshot.activeTools.length,
    pendingRequestCount: snapshot.pendingRuntimeRequests.length,
  };
}

function cloneSnapshot(snapshot: ThreadProjectionSnapshot): ThreadProjectionSnapshot {
  const cloned = {
    ...snapshot,
    timeline: snapshot.timeline.map(cloneTimelineItem),
    workGroups: snapshot.workGroups.map(cloneWorkGroup),
    transcriptMessages: snapshot.transcriptMessages.map(cloneMessage),
    activeTools: snapshot.activeTools.map(cloneToolCall),
    pendingRuntimeRequests: snapshot.pendingRuntimeRequests.map(cloneRuntimeRequest),
    contextUsage: cloneContextUsage(snapshot.contextUsage),
    activity: { ...snapshot.activity },
  };
  return refreshSnapshot(cloned);
}

function cloneTimelineItem(item: ThreadTimelineItem): ThreadTimelineItem {
  if (item.type === 'message') {
    return { ...item, message: cloneMessage(item.message) };
  }
  if (item.type === 'work') {
    return { ...item, work: cloneWorkGroup(item.work) };
  }
  if (item.type === 'runtime-request') {
    return { ...item, request: cloneRuntimeRequest(item.request) };
  }
  return { ...item };
}

function cloneWorkGroup(work: ThreadWorkGroup): ThreadWorkGroup {
  return {
    ...work,
    usage: work.usage ? cloneUsageDetails(work.usage) : work.usage,
    parts: work.parts.map(cloneWorkPart),
  };
}

function cloneContextUsage(contextUsage: ThreadContextUsage | null): ThreadContextUsage | null {
  if (!contextUsage) return null;
  return {
    ...contextUsage,
    usage: cloneUsageDetails(contextUsage.usage),
  };
}

function cloneUsageDetails<T extends ThreadContextUsage['usage']>(usage: T): T {
  return {
    ...usage,
    additionalCounts: usage.additionalCounts
      ? { ...usage.additionalCounts }
      : usage.additionalCounts,
  };
}

function cloneWorkPart(part: ThreadWorkPart): ThreadWorkPart {
  if (part.type === 'assistant-draft') {
    return { ...part, message: cloneMessage(part.message) };
  }
  if (part.type === 'tool') {
    return { ...part, tool: cloneToolCall(part.tool) };
  }
  if (part.type === 'tool-group') {
    return {
      ...part,
      group: {
        ...part.group,
        tools: part.group.tools.map(cloneToolCall),
      },
    };
  }
  return { ...part };
}

function cloneMessage(message: Message): Message {
  return {
    ...message,
    additionalProperties: message.additionalProperties
      ? { ...message.additionalProperties }
      : undefined,
    contents: message.contents.map(cloneAIContent),
    toolCalls: message.toolCalls.map(cloneToolCall),
  };
}

function cloneToolCall(tool: ToolCall): ToolCall {
  return { ...tool };
}

function cloneAIContent(content: AIContent): AIContent {
  if (typeof structuredClone === 'function') {
    return structuredClone(content);
  }
  return JSON.parse(JSON.stringify(content)) as AIContent;
}

function createWorkGroup(context: ProjectionContext, agentName?: string, startedAt?: string): ThreadWorkGroup {
  const id = workGroupId(context.turnId, context.runId);
  return {
    id,
    turnId: context.turnId,
    conversationId: context.conversationId,
    runId: context.runId,
    status: 'working',
    label: agentName ?? 'Work',
    openByDefault: true,
    parts: [],
    startedAt,
    completedAt: null,
  };
}

function workGroupId(turnId: string | null, runId: string | null): string {
  if (turnId) return `turn:${turnId}`;
  if (runId) return `run:${runId}`;
  return 'work:current';
}

function findCurrentWorkGroup(snapshot: ThreadProjectionSnapshot, turnId: string | null): ThreadWorkGroup | null {
  if (turnId) {
    return snapshot.workGroups.find((work) => work.turnId === turnId) ?? null;
  }
  return snapshot.workGroups.find((work) => work.status === 'working') ?? null;
}

function findFinalAssistantDraft(work: ThreadWorkGroup): Message | null {
  for (let index = work.parts.length - 1; index >= 0; index -= 1) {
    const part = work.parts[index];
    if (part.type === 'assistant-draft' && part.message.role === 'assistant') {
      return part.message;
    }
  }
  return null;
}

function resolveMessagePlacement(context: ProjectionContext, policy: MessagePolicy): MessagePlacement {
  if (policy.source === 'AssistantOutput' && context.turnId) return 'work';
  return 'transcript';
}

function upsertWorkGroup(workGroups: ThreadWorkGroup[], work: ThreadWorkGroup): ThreadWorkGroup[] {
  if (workGroups.some((item) => item.id === work.id)) {
    return workGroups.map((item) => item.id === work.id ? work : item);
  }
  return [...workGroups, work];
}

function upsertWorkPart(
  parts: ThreadWorkPart[],
  part: ThreadWorkPart,
  merge: (existing: ThreadWorkPart, next: ThreadWorkPart) => ThreadWorkPart,
): ThreadWorkPart[] {
  const existing = parts.find((item) => item.id === part.id);
  if (existing) {
    return parts.map((item) => item.id === part.id ? merge(item, part) : item);
  }
  return [...parts, part];
}

function appendReasoningPart(existing: ThreadWorkPart, next: ThreadWorkPart): ThreadWorkPart {
  if (existing.type !== 'reasoning' || next.type !== 'reasoning') return next;
  return {
    ...existing,
    text: existing.text + next.text,
    eventFlowId: next.eventFlowId ?? existing.eventFlowId,
    sequenceNumber: next.sequenceNumber ?? existing.sequenceNumber,
    status: next.status,
  };
}

function upsertTimelineItem(items: ThreadTimelineItem[], item: ThreadTimelineItem): ThreadTimelineItem[] {
  if (items.some((candidate) => candidate.id === item.id)) {
    return items.map((candidate) => candidate.id === item.id ? item : candidate);
  }
  return [...items, item];
}

function createWorkTimelineItem(work: ThreadWorkGroup): ThreadTimelineItem {
  return {
    type: 'work',
    id: `timeline:${work.id}`,
    work,
    turnId: work.turnId,
    conversationId: work.conversationId,
    runId: work.runId,
  };
}

function createMessageTimelineItem(message: Message): ThreadTimelineItem {
  return {
    type: 'message',
    id: `message:${message.id}`,
    message,
    turnId: message.turnId,
    conversationId: message.conversationId,
    runId: message.runId,
    eventFlowId: message.eventFlowId,
    sequenceNumber: message.sequenceNumber,
  };
}

function syncWorkTimeline(timeline: ThreadTimelineItem[], workGroups: ThreadWorkGroup[]): ThreadTimelineItem[] {
  return timeline.map((item) => {
    if (item.type !== 'work') return item;
    const work = workGroups.find((candidate) => `timeline:${candidate.id}` === item.id);
    return work ? createWorkTimelineItem(work) : item;
  });
}

function createMessage(
  messageId: string,
  role: string,
  context: ProjectionContext,
  placement: MessagePlacement,
  clientInputId: string | null = null,
  timestamp = new Date(),
  policy: MessagePolicy = {},
): Message {
  return {
    id: messageId,
    role: role as MessageRole,
    content: '',
    contents: [],
    streaming: false,
    thinking: false,
    timestamp,
    toolCalls: [],
    turnId: context.turnId,
    conversationId: context.conversationId,
    runId: context.runId,
    eventFlowId: context.eventFlowId,
    sequenceNumber: context.sequenceNumber,
    placement,
    clientInputId,
    source: policy.source,
    visibility: policy.visibility,
    persistence: policy.persistence,
  };
}

function readMessagePolicy(event: AgentEvent): MessagePolicy {
  const additionalProperties = readRecordProperty(event, 'additionalProperties');
  const source = toAgentMessageSource(
    readStringProperty(event, 'source') ??
    readStringFromRecord(additionalProperties, AgentMessagePolicyProperties.SOURCE),
  );
  const visibility = toAgentMessageVisibility(
    readStringProperty(event, 'visibility') ??
    readStringFromRecord(additionalProperties, AgentMessagePolicyProperties.VISIBILITY),
  );
  const persistence = toAgentMessagePersistence(
    readStringProperty(event, 'persistence') ??
    readStringFromRecord(additionalProperties, AgentMessagePolicyProperties.PERSISTENCE),
  );

  return { source, visibility, persistence };
}

function readStringFromRecord(record: Record<string, unknown> | undefined, key: string): string | undefined {
  const value = record?.[key];
  return typeof value === 'string' ? value : undefined;
}

function toAgentMessageSource(value: string | undefined): AgentMessageSource | undefined {
  return value === 'Unspecified' ||
    value === 'UserInput' ||
    value === 'AssistantOutput' ||
    value === 'SystemInstruction' ||
    value === 'RuntimeContext' ||
    value === 'BackgroundNotification' ||
    value === 'ToolResult' ||
    value === 'PermissionResponse' ||
    value === 'Steering' ||
    value === 'Internal'
    ? value
    : undefined;
}

function toAgentMessageVisibility(value: string | undefined): AgentMessageVisibility | undefined {
  return value === 'Transcript' ||
    value === 'Hidden' ||
    value === 'Diagnostic'
    ? value
    : undefined;
}

function toAgentMessagePersistence(value: string | undefined): AgentMessagePersistence | undefined {
  return value === 'ThreadHistory' ||
    value === 'ModelContextOnly' ||
    value === 'None'
    ? value
    : undefined;
}

function readEventTimestamp(event: AgentEvent): Date {
  return event.timestamp ? new Date(event.timestamp) : new Date(missingEventTimestamp);
}

function upsertTranscriptMessage(messages: Message[], message: Message): Message[] {
  if (messages.some((item) => item.id === message.id)) {
    return messages.map((item) => item.id === message.id ? message : item);
  }
  return [...messages, message];
}

function upsertTranscriptMessageAndTimeline(
  messages: Message[],
  timeline: ThreadTimelineItem[],
  message: Message,
): { transcriptMessages: Message[]; timeline: ThreadTimelineItem[] } {
  const optimistic = message.clientInputId
    ? messages.find((item) =>
        item.clientInputId === message.clientInputId &&
        item.placement === 'optimistic' &&
        item.id !== message.id)
    : undefined;

  if (optimistic) {
    const reconciled: Message = {
      ...message,
      placement: message.placement === 'optimistic' ? 'optimistic' : 'transcript',
    };
    return {
      transcriptMessages: messages.map((item) => item.id === optimistic.id ? reconciled : item),
      timeline: timeline.map((item) =>
        item.type === 'message' && item.message.id === optimistic.id
          ? createMessageTimelineItem(reconciled)
          : item),
    };
  }

  return {
    transcriptMessages: upsertTranscriptMessage(messages, message),
    timeline: upsertTimelineItem(timeline, createMessageTimelineItem(message)),
  };
}

function updateMessageEverywhere(
  snapshot: ThreadProjectionSnapshot,
  messageId: string,
  updater: (message: Message) => Message,
): ThreadProjectionSnapshot {
  return updateAllMessages(snapshot, (message) => message.id === messageId ? updater(message) : message);
}

function messageExists(snapshot: ThreadProjectionSnapshot, messageId: string): boolean {
  return snapshot.transcriptMessages.some((message) => message.id === messageId) ||
    snapshot.workGroups.some((work) =>
      work.parts.some((part) => part.type === 'assistant-draft' && part.message.id === messageId));
}

function updateAllMessages(
  snapshot: ThreadProjectionSnapshot,
  updater: (message: Message) => Message,
): ThreadProjectionSnapshot {
  const transcriptMessages = snapshot.transcriptMessages.map(updater);
  const workGroups = snapshot.workGroups.map((work) => ({
    ...work,
    parts: work.parts.map((part) =>
      part.type === 'assistant-draft'
        ? { ...part, message: updater(part.message) }
        : part),
  }));
  const timeline = snapshot.timeline.map((item) => {
    if (item.type === 'message') {
      return createMessageTimelineItem(updater(item.message));
    }
    if (item.type === 'work') {
      const work = workGroups.find((candidate) => candidate.id === item.work.id);
      return work ? createWorkTimelineItem(work) : item;
    }
    return item;
  });
  return {
    ...snapshot,
    transcriptMessages,
    workGroups,
    timeline,
  };
}

function updateWorkPart(
  snapshot: ThreadProjectionSnapshot,
  partId: string,
  updater: (part: ThreadWorkPart) => ThreadWorkPart,
): ThreadProjectionSnapshot {
  const workGroups = snapshot.workGroups.map((work) => ({
    ...work,
    parts: work.parts.map((part) => part.id === partId ? updater(part) : part),
  }));
  return {
    ...snapshot,
    workGroups,
    timeline: syncWorkTimeline(snapshot.timeline, workGroups),
  };
}

function upsertRuntimeRequest(requests: RuntimeRequest[], request: RuntimeRequest): RuntimeRequest[] {
  return [
    ...requests.filter((item) => item.id !== request.id),
    request,
  ];
}

function readContentType(content: unknown): string | undefined {
  if (typeof content !== 'object' || content === null) return undefined;
  const contentType = (content as { $type?: unknown }).$type;
  return typeof contentType === 'string' ? contentType : undefined;
}

function isAIContent(content: unknown): content is AIContent {
  return readContentType(content) !== undefined;
}

function readStringProperty(content: unknown, key: string): string | undefined {
  if (typeof content !== 'object' || content === null) return undefined;
  const value = (content as Record<string, unknown>)[key];
  return typeof value === 'string' ? value : undefined;
}

function readRecordProperty(content: unknown, key: string): Record<string, unknown> | undefined {
  if (typeof content !== 'object' || content === null) return undefined;
  const value = (content as Record<string, unknown>)[key];
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function readBooleanProperty(content: unknown, key: string): boolean {
  if (typeof content !== 'object' || content === null) return false;
  return (content as Record<string, unknown>)[key] === true;
}

function appendUniqueText(existing: string, next: string): string {
  if (!next) return existing;
  if (!existing) return next;
  if (existing === next || existing.endsWith(next)) return existing;
  return existing + next;
}

function cloneRuntimeRequest(request: RuntimeRequest): RuntimeRequest {
  if (request.kind === 'permission') {
    return { ...request, request: { ...request.request } };
  }
  if (request.kind === 'clarification') {
    return { ...request, request: { ...request.request } };
  }
  if (request.kind === 'client-tool') {
    return { ...request, request: { ...request.request } };
  }
  return { ...request };
}

function selectThreadRun(activeRun?: ThreadRun | null, runs?: ThreadRun[]): ThreadRunView | null {
  if (activeRun) return mapThreadRun(activeRun);
  if (!runs || runs.length === 0) return null;
  return mapThreadRun(runs[runs.length - 1]);
}

function mapThreadRun(run: ThreadRun): ThreadRunView {
  return {
    runtimeRunId: run.runtimeRunId,
    agentId: run.agentId,
    status: run.status,
    startedAt: run.startedAt,
    completedAt: run.completedAt,
    errorType: run.error?.type,
    errorMessage: run.error?.message,
    modelBackgroundOperation: run.modelBackgroundOperation,
    backgroundTasks: run.backgroundTasks,
    backgroundHandles: run.backgroundHandles,
  };
}
