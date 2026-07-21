import type { Thread, ThreadRuntimeChild } from '@hpd-research/hpd-agent-client';
import type {
  Message,
  RuntimeRequest,
  ActivePathChoice,
  ThreadBranchNavigationSnapshot,
  ThreadBranchChoiceControl,
  ThreadBranchChoiceControlPlacement,
  ThreadErrorInfo,
  ThreadContextUsage,
  ThreadProjectionSnapshot,
  ThreadTimelineItem,
  ThreadTimelineMessageItem,
  ThreadWorkGroup,
  ToolCall,
} from './types.js';

export type MessageStatus = 'streaming' | 'thinking' | 'executing' | 'complete';

export type TextSubmissionBlockedReason =
  | 'error'
  | 'runtime-request'
  | 'busy'
  | 'not-sendable';

export interface TextSubmissionState {
  canSubmit: boolean;
  reason: TextSubmissionBlockedReason | null;
}

export interface BranchChoicePosition {
  current: number;
  total: number;
}

export interface RuntimeChildGroups {
  subAgents: ThreadRuntimeChild[];
  visible: ThreadRuntimeChild[];
  hidden: ThreadRuntimeChild[];
}

interface ThreadTimelineMessagePosition {
  item: ThreadTimelineMessageItem;
  messageIndex: number;
  timelineIndex: number;
}

export interface ThreadTimelineOptions {
  completedWork?: 'collapsed' | 'expanded' | 'hidden';
  runtimeRequests?: 'inline' | 'exclude';
}

export function getLatestMessage(snapshot: ThreadProjectionSnapshot): Message | null {
  return snapshot.transcriptMessages.at(-1) ?? null;
}

export function getLastUserMessage(snapshot: ThreadProjectionSnapshot): Message | null {
  return findLastMessageByRole(snapshot, 'user');
}

export function getLastAssistantMessage(snapshot: ThreadProjectionSnapshot): Message | null {
  return findLastMessageByRole(snapshot, 'assistant');
}

export function getMessageById(snapshot: ThreadProjectionSnapshot, messageId: string): Message | null {
  return snapshot.transcriptMessages.find((message) => message.id === messageId) ?? null;
}

export function getTranscriptMessages(snapshot: ThreadProjectionSnapshot): Message[] {
  return snapshot.transcriptMessages.map(cloneMessage);
}

export function getThreadTimeline(
  snapshot: ThreadProjectionSnapshot,
  options: ThreadTimelineOptions = {},
): ThreadTimelineItem[] {
  const runtimeRequests = options.runtimeRequests ?? 'inline';
  const completedWork = options.completedWork ?? 'collapsed';
  return snapshot.timeline
    .filter((item) => runtimeRequests === 'inline' || item.type !== 'runtime-request')
    .filter((item) =>
      item.type !== 'work' ||
      item.work.status === 'working' ||
      completedWork !== 'hidden')
    .map(cloneTimelineItem);
}

export function getThreadWorkGroups(
  snapshot: ThreadProjectionSnapshot,
  options: ThreadTimelineOptions = {},
): ThreadWorkGroup[] {
  const completedWork = options.completedWork ?? 'collapsed';
  return snapshot.workGroups
    .filter((work) => work.status === 'working' || completedWork !== 'hidden')
    .map(cloneWorkGroup);
}

export function getMessageStatus(message: Message): MessageStatus {
  if (message.thinking) return 'thinking';
  if (message.streaming) return 'streaming';
  if (message.toolCalls.some(isToolCallActive)) return 'executing';
  return 'complete';
}

export function getActiveToolCalls(snapshot: ThreadProjectionSnapshot): ToolCall[] {
  return [...snapshot.activeTools];
}

export function getThreadErrors(snapshot: ThreadProjectionSnapshot): ThreadErrorInfo[] {
  const errors: ThreadErrorInfo[] = [];
  const seen = new Set<string>();

  const add = (error: ThreadErrorInfo): void => {
    if (!error.message.trim()) return;
    const key = `${error.kind}:${error.id}:${error.message}`;
    if (seen.has(key)) return;
    seen.add(key);
    errors.push(error);
  };

  const threadExecution = snapshot.threadExecution;
  if (threadExecution?.status === 'failed' && threadExecution.errorMessage) {
    add({
      id: `execution:${threadExecution.threadExecutionId}`,
      kind: 'execution',
      message: threadExecution.errorMessage,
      type: threadExecution.errorType,
      executionId: threadExecution.threadExecutionId,
      recoverable: true,
    });
  }

  for (const work of snapshot.workGroups) {
    if (work.status === 'failed' && work.error) {
      add({
        id: `work:${work.id}`,
        kind: 'work',
        message: work.error,
        executionId: work.executionId,
        turnId: work.turnId,
        conversationId: work.conversationId,
        recoverable: true,
      });
    }

    for (const part of work.parts) {
      if (part.type === 'tool') {
        addToolError(part.tool, add);
      } else if (part.type === 'tool-group') {
        for (const tool of part.group.tools) {
          addToolError(tool, add);
        }
      }
    }
  }

  for (const tool of snapshot.activeTools) {
    addToolError(tool, add);
  }

  if (snapshot.error) {
    add({
      id: 'thread:error',
      kind: 'thread',
      message: snapshot.error,
      executionId: snapshot.currentExecutionId,
      turnId: snapshot.currentTurnId,
      conversationId: snapshot.currentConversationId,
      recoverable: true,
    });
  }

  return errors;
}

export function getLatestThreadError(snapshot: ThreadProjectionSnapshot): ThreadErrorInfo | null {
  return getThreadErrors(snapshot).at(-1) ?? null;
}

export function hasThreadErrors(snapshot: ThreadProjectionSnapshot): boolean {
  return getThreadErrors(snapshot).length > 0;
}

export function isToolCallActive(toolCall: ToolCall): boolean {
  return toolCall.status === 'pending' || toolCall.status === 'executing';
}

export function getToolCallDuration(toolCall: ToolCall): number | null {
  if (!toolCall.endTime) return null;
  return toolCall.endTime.getTime() - toolCall.startTime.getTime();
}

export function getPendingRuntimeRequests(snapshot: ThreadProjectionSnapshot): RuntimeRequest[] {
  return snapshot.pendingRuntimeRequests.map((request) => cloneRuntimeRequest(request));
}

export function getThreadContextUsage(snapshot: ThreadProjectionSnapshot): ThreadContextUsage | null {
  if (!snapshot.contextUsage) return null;
  return {
    ...snapshot.contextUsage,
    usage: {
      ...snapshot.contextUsage.usage,
      additionalCounts: snapshot.contextUsage.usage.additionalCounts
        ? { ...snapshot.contextUsage.usage.additionalCounts }
        : snapshot.contextUsage.usage.additionalCounts,
    },
  };
}

export function hasPendingRuntimeRequests(snapshot: ThreadProjectionSnapshot): boolean {
  return snapshot.pendingRuntimeRequests.length > 0;
}

export function getBlockingRuntimeRequests(snapshot: ThreadProjectionSnapshot): RuntimeRequest[] {
  return getPendingRuntimeRequests(snapshot);
}

export function isThreadBusy(snapshot: ThreadProjectionSnapshot): boolean {
  return snapshot.activity.status === 'working' || snapshot.activity.status === 'requesting';
}

export function canSubmitText(snapshot: ThreadProjectionSnapshot): boolean {
  return snapshot.canSend && !isThreadBusy(snapshot);
}

export function getTextSubmissionState(snapshot: ThreadProjectionSnapshot): TextSubmissionState {
  if (snapshot.error) {
    return { canSubmit: false, reason: 'error' };
  }

  if (hasPendingRuntimeRequests(snapshot)) {
    return { canSubmit: false, reason: 'runtime-request' };
  }

  if (isThreadBusy(snapshot)) {
    return { canSubmit: false, reason: 'busy' };
  }

  if (!snapshot.canSend) {
    return { canSubmit: false, reason: 'not-sendable' };
  }

  return { canSubmit: true, reason: null };
}

export function hasForkGroups(snapshot: ThreadBranchNavigationSnapshot): boolean {
  return snapshot.forkGroups.length > 0;
}

export function hasActivePathChoices(snapshot: ThreadBranchNavigationSnapshot): boolean {
  return snapshot.activePathChoices.length > 0;
}

export function getBranchChoicePosition(choice: ActivePathChoice): BranchChoicePosition {
  return choice.position;
}

export function getBranchChoiceLabel(choice: ActivePathChoice): string {
  const position = getBranchChoicePosition(choice);
  if (position.total <= 1) return '';
  return choice.selectedMember.isSource
    ? `Source (${position.current} / ${position.total})`
    : `Fork ${position.current} / ${position.total}`;
}

export function getThreadBranchChoiceControlsForTimeline(
  snapshot: ThreadBranchNavigationSnapshot,
  timeline: readonly ThreadTimelineItem[],
): ThreadBranchChoiceControl[] {
  const messageItems = timeline
    .map((item, timelineIndex) => ({ item, timelineIndex }))
    .filter((entry): entry is { item: ThreadTimelineMessageItem; timelineIndex: number } =>
      entry.item.type === 'message')
    .map((entry, messageIndex) => ({ ...entry, messageIndex }));
  const timelinePositions = new Map<string, ThreadTimelineMessagePosition>(
    messageItems.map((entry) => [entry.item.message.id, entry]),
  );

  return snapshot.activePathChoices
    .map((choice) => createThreadBranchChoiceControl(choice, timelinePositions))
    .filter((control): control is ThreadBranchChoiceControl => control !== null)
    .sort((left, right) =>
      (left.renderTimelineIndex ?? Number.MAX_SAFE_INTEGER) -
        (right.renderTimelineIndex ?? Number.MAX_SAFE_INTEGER) ||
      (left.boundaryMessageIndex ?? Number.MAX_SAFE_INTEGER) -
        (right.boundaryMessageIndex ?? Number.MAX_SAFE_INTEGER) ||
      left.groupId.localeCompare(right.groupId),
    );
}

export function getThreadBranchChoiceControlsByTimelineItem(
  snapshot: ThreadBranchNavigationSnapshot,
  timeline: readonly ThreadTimelineItem[],
): Map<string, ThreadBranchChoiceControl[]> {
  const controlsByItem = new Map<string, ThreadBranchChoiceControl[]>();

  for (const control of getThreadBranchChoiceControlsForTimeline(snapshot, timeline)) {
    const controls = controlsByItem.get(control.renderTimelineItemId);
    if (controls) {
      controls.push(control);
    } else {
      controlsByItem.set(control.renderTimelineItemId, [control]);
    }
  }

  return controlsByItem;
}

export function getThreadBranchChoiceControlLabel(control: ThreadBranchChoiceControl | null | undefined): string {
  if (!control || control.position.total <= 1) return '';
  return control.selectedMember.isSource
    ? `Source (${control.position.current} / ${control.position.total})`
    : `Fork ${control.position.current} / ${control.position.total}`;
}

export function isSubAgentThread(thread: Thread | ThreadRuntimeChild | null | undefined): boolean {
  return thread?.kind === 'SubAgent';
}

export function isMainAgentThread(thread: Thread | ThreadRuntimeChild | null | undefined): boolean {
  return thread?.kind === 'MainAgent';
}

export function isHiddenThread(thread: Thread | ThreadRuntimeChild | null | undefined): boolean {
  return thread?.visibility === 'Hidden';
}

export function isVisibleThread(thread: Thread | ThreadRuntimeChild | null | undefined): boolean {
  return thread?.visibility === 'Visible';
}

export function getParentThreadId(thread: Thread | ThreadRuntimeChild | null | undefined): string | null {
  return thread?.parentThreadId ?? null;
}

export function getThreadDisplayName(thread: Thread | ThreadRuntimeChild | null | undefined): string {
  if (!thread) return '';
  return thread.subAgentTaskName ?? thread.name ?? thread.subAgentName ?? ('id' in thread ? thread.id : thread.threadId);
}

export function getThreadKindLabel(thread: Thread | ThreadRuntimeChild | null | undefined): string {
  if (!thread) return '';
  return isSubAgentThread(thread) ? 'Subagent' : 'Thread';
}

export function getSubAgentRuntimeChildren(snapshot: ThreadBranchNavigationSnapshot): ThreadRuntimeChild[] {
  return snapshot.runtimeChildren.filter(isSubAgentThread);
}

export function getVisibleRuntimeChildren(snapshot: ThreadBranchNavigationSnapshot): ThreadRuntimeChild[] {
  return snapshot.runtimeChildren.filter(isVisibleThread);
}

export function getInspectableRuntimeChildren(snapshot: ThreadBranchNavigationSnapshot): ThreadRuntimeChild[] {
  return [...snapshot.runtimeChildren];
}

export function hasSubAgentRuntimeChildren(snapshot: ThreadBranchNavigationSnapshot): boolean {
  return snapshot.runtimeChildren.some(isSubAgentThread);
}

export function getSubAgentRuntimeChildCount(snapshot: ThreadBranchNavigationSnapshot): number {
  return getSubAgentRuntimeChildren(snapshot).length;
}

export function getRuntimeChildGroups(snapshot: ThreadBranchNavigationSnapshot): RuntimeChildGroups {
  return {
    subAgents: getSubAgentRuntimeChildren(snapshot),
    visible: getVisibleRuntimeChildren(snapshot),
    hidden: snapshot.runtimeChildren.filter(isHiddenThread),
  };
}

function createThreadBranchChoiceControl(
  choice: ActivePathChoice,
  messagePositions: Map<string, ThreadTimelineMessagePosition> | null,
): ThreadBranchChoiceControl | null {
  const boundaryMessageId = choice.group.forkedAtMessageId ?? null;
  const renderTarget = resolveBranchChoiceRenderTarget(
    choice,
    boundaryMessageId,
    messagePositions,
  );
  if (!renderTarget.position) return null;

  return {
    groupId: choice.group.id,
    sourceThreadId: choice.group.sourceThreadId,
    boundaryMessageId,
    boundaryMessageIndex: choice.group.forkedAtMessageIndex ?? null,
    choiceMessageIndex: choice.selectedMember.choiceMessageIndex ?? choice.group.choiceMessageIndex,
    renderTimelineItemId: renderTarget.position.item.id,
    renderTimelineIndex: renderTarget.position.timelineIndex,
    renderPlacement: renderTarget.placement,
    selectedMember: { ...choice.selectedMember },
    selectedThreadId: choice.selectedThreadId,
    relationship: choice.relationship,
    members: choice.group.members.map((member) => ({ ...member })),
    position: { ...choice.position },
    previous: choice.previous ? { ...choice.previous } : null,
    next: choice.next ? { ...choice.next } : null,
  };
}

function resolveBranchChoiceRenderTarget(
  choice: ActivePathChoice,
  boundaryMessageId: string | null,
  messagePositions: Map<string, ThreadTimelineMessagePosition> | null,
): { position: ThreadTimelineMessagePosition | null; placement: ThreadBranchChoiceControlPlacement } {
  if (!messagePositions) return { position: null, placement: 'unplaced' };
  const position = choice.selectedMember.choiceMessageId
    ? messagePositions.get(choice.selectedMember.choiceMessageId) ?? null
    : null;
  if (!position) return { position: null, placement: 'unplaced' };
  return {
    position,
    placement: boundaryMessageId === null ? 'root' : 'choice-message',
  };
}

function findLastMessageByRole(snapshot: ThreadProjectionSnapshot, role: string): Message | null {
  for (let index = snapshot.transcriptMessages.length - 1; index >= 0; index -= 1) {
    const message = snapshot.transcriptMessages[index];
    if (message.role === role) return message;
  }
  return null;
}

function addToolError(
  tool: ToolCall,
  add: (error: ThreadErrorInfo) => void,
): void {
  if (tool.status !== 'error' || !tool.error) return;

  add({
    id: `tool:${tool.callId}`,
    kind: 'tool',
    message: tool.error,
    source: tool.toolharnessName ?? tool.name,
    executionId: tool.executionId,
    turnId: tool.turnId,
    conversationId: tool.conversationId,
    toolCallId: tool.callId,
    recoverable: true,
  });
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
    parts: work.parts.map((part) => {
      if (part.type === 'assistant-draft') {
        return { ...part, message: cloneMessage(part.message) };
      }
      if (part.type === 'tool') {
        return { ...part, tool: { ...part.tool } };
      }
      if (part.type === 'tool-group') {
        return {
          ...part,
          group: {
            ...part.group,
            tools: part.group.tools.map((tool) => ({ ...tool })),
          },
        };
      }
      return { ...part };
    }),
  };
}

function cloneMessage(message: Message): Message {
  return {
    ...message,
    toolCalls: message.toolCalls.map((tool) => ({ ...tool })),
  };
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
