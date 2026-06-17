import {
  EventTypes,
  type AgentEvent,
  type BranchRun,
  type KnownAgentEvent,
} from '@hpd-research/hpd-agent-client';
import { mapBranchMessages } from '../internal/map-branch-message.js';
import { formatToolResultPayload } from '../internal/tool-result.js';
import type {
  BranchProjection,
  BranchProjectionListener,
  BranchProjectionSnapshot,
  BranchRunView,
  BranchSnapshot,
  ClientToolRequest,
  ClarificationRequest,
  Message,
  MessageRole,
  PermissionRequest,
  ToolCall,
  Unsubscribe,
} from './types.js';

export function createBranchProjection(): BranchProjection {
  return new BranchProjectionImpl();
}

class BranchProjectionImpl implements BranchProjection {
  private snapshot: BranchProjectionSnapshot = createInitialSnapshot();
  private listeners = new Set<BranchProjectionListener>();

  getSnapshot(): BranchProjectionSnapshot {
    return cloneSnapshot(this.snapshot);
  }

  subscribe(listener: BranchProjectionListener): Unsubscribe {
    this.listeners.add(listener);
    listener(this.getSnapshot());
    return () => {
      this.listeners.delete(listener);
    };
  }

  rehydrate(snapshot: BranchSnapshot): void {
    const branchRun = selectBranchRun(snapshot.activeRun, snapshot.runs);
    this.snapshot = {
      ...this.snapshot,
      branch: snapshot.branch ?? this.snapshot.branch,
      messages: snapshot.messages ? mapBranchMessages(snapshot.messages) : this.snapshot.messages,
      activeTools: [],
      pendingPermissions: [],
      pendingClarifications: [],
      pendingClientToolRequests: [],
      branchRun,
      streaming: branchRun?.status === 'active',
      reasoning: false,
      currentTurnId: null,
      currentConversationId: null,
      error: branchRun?.status === 'failed' ? branchRun.errorMessage ?? 'Branch run failed' : null,
    };
    this.emit();
  }

  project(event: AgentEvent): void {
    const known = event as KnownAgentEvent;

    switch (known.type) {
      case EventTypes.TEXT_MESSAGE_START:
        this.onTextMessageStart(known.messageId, known.role);
        break;
      case EventTypes.TEXT_DELTA:
        this.onTextDelta(known.messageId, known.text);
        break;
      case EventTypes.TEXT_MESSAGE_END:
        this.onTextMessageEnd(known.messageId);
        break;
      case EventTypes.REASONING_MESSAGE_START:
        this.onReasoningMessageStart(known.messageId, known.role);
        break;
      case EventTypes.REASONING_DELTA:
        this.onReasoningDelta(known.messageId, known.text);
        break;
      case EventTypes.REASONING_MESSAGE_END:
        this.onReasoningMessageEnd(known.messageId);
        break;
      case EventTypes.TOOL_CALL_START:
        this.onToolCallStart({
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
        this.onToolCallEnd(known.callId);
        break;
      case EventTypes.PERMISSION_REQUEST:
        this.addPermission({
          permissionId: known.permissionId,
          sourceName: known.sourceName,
          functionName: known.functionName,
          description: known.description,
          callId: known.callId,
          arguments: known.arguments,
        });
        break;
      case EventTypes.PERMISSION_APPROVED:
        this.removePermission(known.permissionId);
        break;
      case EventTypes.PERMISSION_DENIED:
        this.removePermission(known.permissionId);
        break;
      case EventTypes.CLARIFICATION_REQUEST:
        this.addClarification({
          requestId: known.requestId,
          sourceName: known.sourceName,
          question: known.question,
          agentName: known.agentName,
          options: known.options,
        });
        break;
      case EventTypes.CLARIFICATION_RESPONSE:
        this.removeClarification(known.requestId);
        break;
      case EventTypes.CLIENT_TOOL_INVOKE_REQUEST:
        this.addClientToolRequest({
          requestId: known.requestId,
          toolName: known.toolName,
          callId: known.callId,
          arguments: known.arguments,
          description: known.description,
        });
        break;
      case EventTypes.CLIENT_TOOL_INVOKE_RESPONSE:
        this.removeClientToolRequest(known.requestId);
        break;
      case EventTypes.AGENT_REQUEST_RESOLVED:
      case EventTypes.AGENT_REQUEST_EXPIRED:
      case EventTypes.AGENT_REQUEST_CANCELLED:
        this.removePendingRequest(known.requestId);
        break;
      case EventTypes.MESSAGE_TURN_STARTED:
        this.snapshot = {
          ...this.snapshot,
          currentTurnId: known.messageTurnId,
          currentConversationId: known.conversationId,
          streaming: true,
          error: null,
        };
        this.emit();
        break;
      case EventTypes.MESSAGE_TURN_FINISHED:
        this.snapshot = {
          ...this.snapshot,
          currentTurnId: null,
          currentConversationId: known.conversationId,
          streaming: false,
          reasoning: false,
        };
        this.emit();
        break;
      case EventTypes.MESSAGE_TURN_ERROR:
        this.snapshot = {
          ...this.snapshot,
          error: known.message,
          streaming: false,
          reasoning: false,
        };
        this.emit();
        break;
      case EventTypes.BRANCH_RUN_STARTED:
        this.snapshot = {
          ...this.snapshot,
          branchRun: {
            runtimeRunId: known.runtimeRunId,
            agentId: known.agentId,
            status: 'active',
            startedAt: known.startedAt,
          },
          streaming: true,
          error: null,
        };
        this.emit();
        break;
      case EventTypes.BRANCH_RUN_COMPLETED: {
        const status = known.errorType
          ? 'failed'
          : known.cancelled
            ? 'cancelled'
            : 'completed';
        this.snapshot = {
          ...this.snapshot,
          branchRun: {
            runtimeRunId: known.runtimeRunId,
            agentId: known.agentId,
            status,
            startedAt: this.snapshot.branchRun?.runtimeRunId === known.runtimeRunId
              ? this.snapshot.branchRun.startedAt
              : undefined,
            errorType: known.errorType,
            errorMessage: known.errorMessage,
          },
          streaming: false,
          reasoning: false,
          error: known.errorMessage ?? this.snapshot.error,
        };
        this.emit();
        break;
      }
      default:
        break;
    }
  }

  clearError(): void {
    this.snapshot = {
      ...this.snapshot,
      error: null,
    };
    this.emit();
  }

  reset(): void {
    this.snapshot = createInitialSnapshot();
    this.emit();
  }

  private onTextMessageStart(messageId: string, role: string): void {
    const messages = upsertMessage(this.snapshot.messages, messageId, role, (message) => ({
      ...message,
      streaming: true,
      thinking: false,
    }));
    this.snapshot = {
      ...this.snapshot,
      messages,
      streaming: true,
      error: null,
    };
    this.emit();
  }

  private onTextDelta(messageId: string, text: string): void {
    const messages = updateMessage(this.snapshot.messages, messageId, (message) => ({
      ...message,
      content: message.content + text,
    }));
    this.snapshot = { ...this.snapshot, messages };
    this.emit();
  }

  private onTextMessageEnd(messageId: string): void {
    const messages = updateMessage(this.snapshot.messages, messageId, (message) => ({
      ...message,
      streaming: false,
    }));
    this.snapshot = {
      ...this.snapshot,
      messages,
      streaming: hasStreamingMessages(messages),
    };
    this.emit();
  }

  private onReasoningMessageStart(messageId: string, role: string): void {
    const messages = upsertMessage(this.snapshot.messages, messageId, role, (message) => ({
      ...message,
      streaming: true,
      thinking: true,
      reasoning: message.reasoning ?? '',
    }));
    this.snapshot = {
      ...this.snapshot,
      messages,
      reasoning: true,
      streaming: true,
      error: null,
    };
    this.emit();
  }

  private onReasoningDelta(messageId: string, text: string): void {
    const messages = updateMessage(this.snapshot.messages, messageId, (message) => ({
      ...message,
      reasoning: (message.reasoning ?? '') + text,
    }));
    this.snapshot = { ...this.snapshot, messages };
    this.emit();
  }

  private onReasoningMessageEnd(messageId: string): void {
    const messages = updateMessage(this.snapshot.messages, messageId, (message) => ({
      ...message,
      streaming: false,
      thinking: false,
    }));
    this.snapshot = {
      ...this.snapshot,
      messages,
      reasoning: false,
      streaming: hasStreamingMessages(messages),
    };
    this.emit();
  }

  private onToolCallStart(input: {
    callId: string;
    name: string;
    messageId: string;
    toolharnessName?: string;
    callType?: ToolCall['callType'];
  }): void {
    const toolCall: ToolCall = {
      callId: input.callId,
      name: input.name,
      messageId: input.messageId,
      status: 'pending',
      startTime: new Date(),
      toolharnessName: input.toolharnessName,
      callType: input.callType,
    };

    const activeTools = [
      ...this.snapshot.activeTools.filter((tool) => tool.callId !== input.callId),
      toolCall,
    ];
    const messages = upsertMessage(this.snapshot.messages, input.messageId, 'assistant', (message) => ({
      ...message,
      toolCalls: [
        ...message.toolCalls.filter((tool) => tool.callId !== input.callId),
        toolCall,
      ],
    }));

    this.snapshot = { ...this.snapshot, activeTools, messages };
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
      endTime: new Date(),
      toolharnessName: event.toolharnessName ?? tool.toolharnessName,
      callType: event.callType ?? tool.callType,
    }));
    this.snapshot = {
      ...this.snapshot,
      activeTools: this.snapshot.activeTools.filter((tool) => tool.callId !== event.callId),
    };
    this.emit();
  }

  private onToolCallEnd(callId: string): void {
    this.replaceToolCall(callId, (tool) => ({
      ...tool,
      status: tool.status === 'complete' ? tool.status : 'complete',
      endTime: tool.endTime ?? new Date(),
    }));
    this.snapshot = {
      ...this.snapshot,
      activeTools: this.snapshot.activeTools.filter((tool) => tool.callId !== callId),
    };
    this.emit();
  }

  private replaceToolCall(callId: string, updater: (tool: ToolCall) => ToolCall): void {
    const activeTools = this.snapshot.activeTools.map((tool) =>
      tool.callId === callId ? updater(tool) : tool);
    const messages = this.snapshot.messages.map((message) => ({
      ...message,
      toolCalls: message.toolCalls.map((tool) =>
        tool.callId === callId ? updater(tool) : tool),
    }));
    this.snapshot = { ...this.snapshot, activeTools, messages };
    this.emit();
  }

  private addPermission(request: PermissionRequest): void {
    this.snapshot = {
      ...this.snapshot,
      pendingPermissions: [
        ...this.snapshot.pendingPermissions.filter((item) => item.permissionId !== request.permissionId),
        request,
      ],
    };
    this.emit();
  }

  private removePermission(permissionId: string): void {
    this.snapshot = {
      ...this.snapshot,
      pendingPermissions: this.snapshot.pendingPermissions.filter((item) => item.permissionId !== permissionId),
    };
    this.emit();
  }

  private addClarification(request: ClarificationRequest): void {
    this.snapshot = {
      ...this.snapshot,
      pendingClarifications: [
        ...this.snapshot.pendingClarifications.filter((item) => item.requestId !== request.requestId),
        request,
      ],
    };
    this.emit();
  }

  private removeClarification(requestId: string): void {
    this.snapshot = {
      ...this.snapshot,
      pendingClarifications: this.snapshot.pendingClarifications.filter((item) => item.requestId !== requestId),
    };
    this.emit();
  }

  private addClientToolRequest(request: ClientToolRequest): void {
    this.snapshot = {
      ...this.snapshot,
      pendingClientToolRequests: [
        ...this.snapshot.pendingClientToolRequests.filter((item) => item.requestId !== request.requestId),
        request,
      ],
    };
    this.emit();
  }

  private removeClientToolRequest(requestId: string): void {
    this.snapshot = {
      ...this.snapshot,
      pendingClientToolRequests: this.snapshot.pendingClientToolRequests.filter((item) => item.requestId !== requestId),
    };
    this.emit();
  }

  private removePendingRequest(requestId: string): void {
    this.snapshot = {
      ...this.snapshot,
      pendingPermissions: this.snapshot.pendingPermissions.filter((item) => item.permissionId !== requestId),
      pendingClarifications: this.snapshot.pendingClarifications.filter((item) => item.requestId !== requestId),
      pendingClientToolRequests: this.snapshot.pendingClientToolRequests.filter((item) => item.requestId !== requestId),
    };
    this.emit();
  }

  private emit(): void {
    const snapshot = this.getSnapshot();
    for (const listener of this.listeners) {
      listener(snapshot);
    }
  }
}

function createInitialSnapshot(): BranchProjectionSnapshot {
  return {
    branch: null,
    messages: [],
    streaming: false,
    reasoning: false,
    activeTools: [],
    pendingPermissions: [],
    pendingClarifications: [],
    pendingClientToolRequests: [],
    branchRun: null,
    currentTurnId: null,
    currentConversationId: null,
    error: null,
    canSend: true,
  };
}

function cloneSnapshot(snapshot: BranchProjectionSnapshot): BranchProjectionSnapshot {
  return {
    ...snapshot,
    messages: snapshot.messages.map((message) => ({
      ...message,
      toolCalls: message.toolCalls.map((tool) => ({ ...tool })),
    })),
    activeTools: snapshot.activeTools.map((tool) => ({ ...tool })),
    pendingPermissions: snapshot.pendingPermissions.map((item) => ({ ...item })),
    pendingClarifications: snapshot.pendingClarifications.map((item) => ({ ...item })),
    pendingClientToolRequests: snapshot.pendingClientToolRequests.map((item) => ({ ...item })),
    canSend: !snapshot.streaming &&
      snapshot.pendingPermissions.length === 0 &&
      snapshot.pendingClarifications.length === 0 &&
      snapshot.error === null,
  };
}

function upsertMessage(
  messages: Message[],
  messageId: string,
  role: string,
  updater: (message: Message) => Message,
): Message[] {
  const existing = messages.find((message) => message.id === messageId);
  if (existing) {
    return messages.map((message) => message.id === messageId ? updater(message) : message);
  }

  const message: Message = {
    id: messageId,
    role: role as MessageRole,
    content: '',
    streaming: false,
    thinking: false,
    timestamp: new Date(),
    toolCalls: [],
  };

  return [...messages, updater(message)];
}

function updateMessage(
  messages: Message[],
  messageId: string,
  updater: (message: Message) => Message,
): Message[] {
  return messages.map((message) => message.id === messageId ? updater(message) : message);
}

function hasStreamingMessages(messages: Message[]): boolean {
  return messages.some((message) => message.streaming);
}

function selectBranchRun(activeRun?: BranchRun | null, runs?: BranchRun[]): BranchRunView | null {
  if (activeRun) return mapBranchRun(activeRun);
  if (!runs || runs.length === 0) return null;
  return mapBranchRun(runs[runs.length - 1]);
}

function mapBranchRun(run: BranchRun): BranchRunView {
  return {
    runtimeRunId: run.runtimeRunId,
    agentId: run.agentId,
    status: run.status,
    startedAt: run.startedAt,
    completedAt: run.completedAt,
    errorType: run.error?.type,
    errorMessage: run.error?.message,
  };
}
