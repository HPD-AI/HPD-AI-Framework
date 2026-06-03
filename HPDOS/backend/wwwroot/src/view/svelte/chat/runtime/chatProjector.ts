import { EventTypes, type AgentEvent, type ToolResultPayload } from "@hpd/hpd-agent-client";
import type {
  ChatRuntimeEvent,
  ChatTimelineItem,
  ClarificationItem,
  CommandProjection,
  FileMutationDiffStat,
  FileMutationHunk,
  FileMutationProjection,
  PermissionItem,
  ReasoningItem,
  ToolCallItem
} from "./chatTypes";

type MutableTextItem = ChatTimelineItem & {
  kind: "assistant-text";
};

type MutableUserItem = ChatTimelineItem & {
  kind: "user-message";
};

type MutableReasoningItem = ReasoningItem;

type ProjectorState = {
  items: ChatTimelineItem[];
  textByMessageId: Map<string, MutableTextItem>;
  userByMessageId: Map<string, MutableUserItem>;
  roleByMessageId: Map<string, string>;
  reasoningByMessageId: Map<string, MutableReasoningItem>;
  toolsByCallId: Map<string, ToolCallItem>;
  permissionsById: Map<string, PermissionItem>;
  clarificationsById: Map<string, ClarificationItem>;
};

export function projectChatEvents(events: readonly ChatRuntimeEvent[]): ChatTimelineItem[] {
  const state: ProjectorState = {
    items: [],
    textByMessageId: new Map(),
    userByMessageId: new Map(),
    roleByMessageId: new Map(),
    reasoningByMessageId: new Map(),
    toolsByCallId: new Map(),
    permissionsById: new Map(),
    clarificationsById: new Map()
  };

  events.forEach((event, index) => projectEvent(state, event, index));

  return state.items.filter(isVisibleTimelineItem);
}

function isVisibleTimelineItem(item: ChatTimelineItem): boolean {
  if (item.kind === "assistant-text") {
    return item.text.trim().length > 0;
  }

  if (item.kind === "user-message") {
    return item.text.trim().length > 0;
  }

  return true;
}

function projectEvent(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  switch (event.type) {
    case EventTypes.USER_TEXT_INPUT:
      state.items.push({
        kind: "user-message",
        id: itemId(event, index, "user"),
        sourceEvents: [sourceEventId(event, index)],
        text: stringProp(event, "text") ?? "",
        messageId: stringProp(event, "messageId")
      });
      return;

    case EventTypes.TEXT_MESSAGE_START:
      ensureTextItemForRole(state, event, index);
      return;

    case EventTypes.TEXT_DELTA: {
      const item = ensureTextItemForRole(state, event, index);
      item.text += stringProp(event, "text") ?? "";
      item.sourceEvents.push(sourceEventId(event, index));
      return;
    }

    case EventTypes.TEXT_MESSAGE_END: {
      const item = ensureTextItemForRole(state, event, index);
      if (item.kind === "assistant-text") {
        item.complete = true;
      }
      item.sourceEvents.push(sourceEventId(event, index));
      return;
    }

    case EventTypes.REASONING_MESSAGE_START:
      ensureReasoningItem(state, event, index);
      return;

    case EventTypes.REASONING_DELTA: {
      const item = ensureReasoningItem(state, event, index);
      item.text += stringProp(event, "text") ?? "";
      item.sourceEvents.push(sourceEventId(event, index));
      return;
    }

    case EventTypes.REASONING_MESSAGE_END: {
      const item = ensureReasoningItem(state, event, index);
      item.complete = true;
      item.sourceEvents.push(sourceEventId(event, index));
      return;
    }

    case EventTypes.MESSAGE_TURN_ERROR:
      state.items.push({
        kind: "error",
        id: itemId(event, index, "error"),
        sourceEvents: [sourceEventId(event, index)],
        message: stringProp(event, "message") ?? "Message turn failed.",
        source: "Message turn"
      });
      return;

    case EventTypes.TOOL_CALL_START: {
      const callId = requiredString(event, "callId", `tool-${index}`);
      const tool = ensureToolItem(state, callId, event, index);
      tool.name = requiredString(event, "name", tool.name);
      tool.messageId = stringProp(event, "messageId") ?? tool.messageId;
      tool.status = "running";
      tool.startedAt = timestamp(event) ?? tool.startedAt;
      tool.toolharnessName = stringProp(event, "toolharnessName") ?? tool.toolharnessName;
      tool.callType = stringProp(event, "callType") ?? tool.callType;
      pushToolEvent(tool, event, index);
      return;
    }

    case EventTypes.TOOL_CALL_ARGS: {
      const callId = requiredString(event, "callId", `tool-${index}`);
      const tool = ensureToolItem(state, callId, event, index);
      tool.args = parseArgsJson(stringProp(event, "argsJson"));
      pushToolEvent(tool, event, index);
      return;
    }

    case EventTypes.TOOL_CALL_END: {
      const callId = requiredString(event, "callId", `tool-${index}`);
      const tool = ensureToolItem(state, callId, event, index);
      if (tool.status !== "completed" && tool.status !== "failed") {
        tool.status = "completed";
      }
      tool.completedAt = timestamp(event) ?? tool.completedAt;
      pushToolEvent(tool, event, index);
      return;
    }

    case EventTypes.TOOL_CALL_RESULT: {
      const callId = requiredString(event, "callId", `tool-${index}`);
      const tool = ensureToolItem(state, callId, event, index);
      tool.result = objectProp<ToolResultPayload>(event, "result");
      tool.status = "completed";
      tool.completedAt = timestamp(event) ?? tool.completedAt;
      tool.toolharnessName = stringProp(event, "toolharnessName") ?? tool.toolharnessName;
      tool.callType = stringProp(event, "callType") ?? tool.callType;
      pushToolEvent(tool, event, index);
      return;
    }

    case EventTypes.PERMISSION_REQUEST:
      projectPermissionRequest(state, event, index);
      return;

    case EventTypes.PERMISSION_RESPONSE:
    case EventTypes.PERMISSION_APPROVED:
    case EventTypes.PERMISSION_DENIED:
      closePermission(state, event, index);
      return;

    case EventTypes.CLARIFICATION_REQUEST:
      projectClarificationRequest(state, event, index);
      return;

    case EventTypes.CLARIFICATION_RESPONSE:
      closeClarification(state, event, index);
      return;
  }

  if (isExecuteCommandEvent(event)) {
    projectCommandEvent(state, event, index);
    return;
  }

  if (isFileMutationEvent(event)) {
    projectFileMutationEvent(state, event, index);
    return;
  }

  // Runtime, branch metadata, and unknown events belong in a debug/event inspector,
  // not in the primary conversation transcript.
}

function ensureTextItemForRole(
  state: ProjectorState,
  event: ChatRuntimeEvent,
  index: number
): MutableTextItem | MutableUserItem {
  const messageId = requiredString(event, "messageId", `message-${index}`);
  const role = stringProp(event, "role");
  if (role) {
    state.roleByMessageId.set(messageId, role);
  }

  const effectiveRole = role ?? state.roleByMessageId.get(messageId);
  if (effectiveRole === "user") {
    return ensureUserTextItem(state, event, index);
  }

  return ensureTextItem(state, event, index);
}

function ensureUserTextItem(state: ProjectorState, event: ChatRuntimeEvent, index: number): MutableUserItem {
  const messageId = requiredString(event, "messageId", `message-${index}`);
  let item = state.userByMessageId.get(messageId);
  if (item) return item;

  item = {
    kind: "user-message",
    id: `user:${messageId}`,
    sourceEvents: [sourceEventId(event, index)],
    messageId,
    text: ""
  };
  state.userByMessageId.set(messageId, item);
  state.items.push(item);
  return item;
}

function ensureTextItem(state: ProjectorState, event: ChatRuntimeEvent, index: number): MutableTextItem {
  const messageId = requiredString(event, "messageId", `message-${index}`);
  let item = state.textByMessageId.get(messageId);
  if (item) return item;

  item = {
    kind: "assistant-text",
    id: `assistant:${messageId}`,
    sourceEvents: [sourceEventId(event, index)],
    messageId,
    role: stringProp(event, "role"),
    text: "",
    complete: false
  };
  state.textByMessageId.set(messageId, item);
  state.items.push(item);
  return item;
}

function ensureReasoningItem(state: ProjectorState, event: ChatRuntimeEvent, index: number): MutableReasoningItem {
  const messageId = requiredString(event, "messageId", `reasoning-${index}`);
  let item = state.reasoningByMessageId.get(messageId);
  if (item) return item;

  item = {
    kind: "reasoning",
    id: `reasoning:${messageId}`,
    sourceEvents: [sourceEventId(event, index)],
    messageId,
    text: "",
    complete: false
  };
  state.reasoningByMessageId.set(messageId, item);
  state.items.push(item);
  return item;
}

function ensureToolItem(
  state: ProjectorState,
  callId: string,
  event: ChatRuntimeEvent,
  index: number
): ToolCallItem {
  let item = state.toolsByCallId.get(callId);
  if (item) return item;

  item = {
    kind: "tool-call",
    id: `tool:${callId}`,
    sourceEvents: [sourceEventId(event, index)],
    callId,
    name: stringProp(event, "name") ?? stringProp(event, "functionName") ?? "tool",
    status: "pending",
    rawEvents: []
  };
  state.toolsByCallId.set(callId, item);
  state.items.push(item);
  return item;
}

function projectCommandEvent(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const callId = requiredString(event, "toolCallId", requiredString(event, "callId", `command-${index}`));
  const tool = ensureToolItem(state, callId, event, index);
  tool.name = stringProp(event, "functionName") ?? tool.name;
  tool.command = mergeDefined(tool.command, commandProjection(event));

  if (event.type === "EXECUTE_COMMAND_PROCESS_STARTED") {
    tool.status = "running";
    tool.startedAt = timestamp(event) ?? tool.startedAt;
  }

  if (event.type === "EXECUTE_COMMAND_OUTPUT_CHUNK") {
    tool.status = "running";
    const text = stringProp(event, "text");
    if (text) {
      tool.command.liveOutput = `${tool.command.liveOutput ?? ""}${text}`;
    }
  }

  if (event.type === "EXECUTE_COMMAND_PROCESS_EXITED") {
    tool.status = numberProp(event, "exitCode") === 0 ? "completed" : "failed";
    tool.completedAt = timestamp(event) ?? tool.completedAt;
  }

  pushToolEvent(tool, event, index);
}

function commandProjection(event: ChatRuntimeEvent): CommandProjection {
  return {
    commandId: stringProp(event, "commandId"),
    command: stringProp(event, "command"),
    baseCommand: stringProp(event, "baseCommand"),
    category: stringProp(event, "category"),
    workingDirectory: stringProp(event, "workingDirectory"),
    shell: stringProp(event, "shell"),
    processId: numberProp(event, "processId"),
    timeoutMilliseconds: numberProp(event, "timeoutMilliseconds"),
    background: booleanProp(event, "background"),
    autoBackgroundEligible: booleanProp(event, "autoBackgroundEligible"),
    exitCode: nullableNumberProp(event, "exitCode"),
    completionKind: stringProp(event, "completionKind"),
    durationMilliseconds: numberProp(event, "durationMilliseconds"),
    stdoutBytes: numberProp(event, "stdoutBytes"),
    stderrBytes: numberProp(event, "stderrBytes"),
    combinedOutputBytes: numberProp(event, "combinedOutputBytes"),
    combinedBytesDiscarded: numberProp(event, "combinedBytesDiscarded"),
    outputTruncated: booleanProp(event, "outputTruncated"),
    outputDrainTimedOut: booleanProp(event, "outputDrainTimedOut"),
    outputEventsSuppressed: booleanProp(event, "outputEventsSuppressed"),
    artifacts: {
      stdoutArtifactPath: nullableStringProp(event, "stdoutArtifactPath"),
      stderrArtifactPath: nullableStringProp(event, "stderrArtifactPath"),
      combinedOutputArtifactPath: nullableStringProp(event, "combinedOutputArtifactPath"),
      stdoutContentId: nullableStringProp(event, "stdoutContentId"),
      stderrContentId: nullableStringProp(event, "stderrContentId"),
      combinedOutputContentId: nullableStringProp(event, "combinedOutputContentId"),
      stdoutLocalPath: nullableStringProp(event, "stdoutLocalPath"),
      stderrLocalPath: nullableStringProp(event, "stderrLocalPath"),
      combinedOutputLocalPath: nullableStringProp(event, "combinedOutputLocalPath")
    }
  };
}

function projectFileMutationEvent(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const callId = requiredString(event, "toolCallId", `file-mutation-${index}`);
  const tool = ensureToolItem(state, callId, event, index);
  tool.name = stringProp(event, "functionName") ?? tool.name;
  tool.status = "completed";
  tool.fileMutation = fileMutationProjection(event);
  pushToolEvent(tool, event, index);
}

function fileMutationProjection(event: ChatRuntimeEvent): FileMutationProjection {
  return {
    type: event.type === "FILE_WRITE_APPLIED" ? "write" : "edit",
    path: requiredString(event, "path", ""),
    displayPath: requiredString(event, "displayPath", requiredString(event, "path", "")),
    mutationKind: stringProp(event, "mutationKind"),
    created: booleanProp(event, "created") ?? false,
    changed: booleanProp(event, "changed") ?? false,
    mode: stringProp(event, "mode"),
    editCount: numberProp(event, "editCount"),
    replacementCount: numberProp(event, "replacementCount"),
    before: prop(event, "before"),
    after: prop(event, "after"),
    textEdits: arrayProp(event, "textEdits"),
    hunks: arrayProp<FileMutationHunk>(event, "hunks"),
    hunksTruncated: booleanProp(event, "hunksTruncated"),
    diffStat: objectProp<FileMutationDiffStat>(event, "diffStat"),
    notes: arrayProp(event, "notes"),
    replacements: arrayProp(event, "replacements"),
    normalizations: arrayProp(event, "normalizations")
  };
}

function projectPermissionRequest(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const permissionId = requiredString(event, "permissionId", `permission-${index}`);
  const item: PermissionItem = {
    kind: "permission",
    id: `permission:${permissionId}`,
    sourceEvents: [sourceEventId(event, index)],
    permissionId,
    sourceName: requiredString(event, "sourceName", ""),
    functionName: requiredString(event, "functionName", ""),
    callId: requiredString(event, "callId", ""),
    description: stringProp(event, "description"),
    pending: true
  };
  state.permissionsById.set(permissionId, item);
  state.items.push(item);
}

function closePermission(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const permissionId = requiredString(event, "permissionId", `permission-${index}`);
  const item = state.permissionsById.get(permissionId);
  if (item) {
    item.pending = false;
    item.sourceEvents.push(sourceEventId(event, index));
  }
}

function projectClarificationRequest(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const requestId = requiredString(event, "requestId", `clarification-${index}`);
  const item: ClarificationItem = {
    kind: "clarification",
    id: `clarification:${requestId}`,
    sourceEvents: [sourceEventId(event, index)],
    requestId,
    sourceName: requiredString(event, "sourceName", ""),
    question: requiredString(event, "question", ""),
    pending: true
  };
  state.clarificationsById.set(requestId, item);
  state.items.push(item);
}

function closeClarification(state: ProjectorState, event: ChatRuntimeEvent, index: number): void {
  const requestId = requiredString(event, "requestId", `clarification-${index}`);
  const item = state.clarificationsById.get(requestId);
  if (item) {
    item.pending = false;
    item.answer = stringProp(event, "answer");
    item.sourceEvents.push(sourceEventId(event, index));
  }
}

function pushToolEvent(tool: ToolCallItem, event: ChatRuntimeEvent, index: number): void {
  tool.rawEvents.push(event);
  const id = sourceEventId(event, index);
  if (!tool.sourceEvents.includes(id)) {
    tool.sourceEvents.push(id);
  }
}

function isExecuteCommandEvent(event: AgentEvent): boolean {
  return event.type.startsWith("EXECUTE_COMMAND_");
}

function isFileMutationEvent(event: AgentEvent): boolean {
  return event.type === "FILE_EDIT_APPLIED" || event.type === "FILE_WRITE_APPLIED";
}

function parseArgsJson(argsJson: string | undefined): unknown {
  if (!argsJson) return undefined;
  try {
    return JSON.parse(argsJson);
  } catch {
    return argsJson;
  }
}

function sourceEventId(event: ChatRuntimeEvent, index: number): string {
  return stringProp(event, "eventId") ?? `${event.type}:${event.sequenceNumber ?? index}`;
}

function itemId(event: ChatRuntimeEvent, index: number, prefix: string): string {
  return `${prefix}:${sourceEventId(event, index)}`;
}

function timestamp(event: ChatRuntimeEvent): string | undefined {
  return stringProp(event, "timestamp") ?? stringProp(event, "startedAt") ?? stringProp(event, "observedAt");
}

function requiredString(event: ChatRuntimeEvent, key: string, fallback: string): string {
  return stringProp(event, key) ?? fallback;
}

function stringProp(event: ChatRuntimeEvent, key: string): string | undefined {
  const value = prop(event, key);
  return typeof value === "string" ? value : undefined;
}

function nullableStringProp(event: ChatRuntimeEvent, key: string): string | null | undefined {
  const value = prop(event, key);
  if (value === null) return null;
  return typeof value === "string" ? value : undefined;
}

function numberProp(event: ChatRuntimeEvent, key: string): number | undefined {
  const value = prop(event, key);
  return typeof value === "number" ? value : undefined;
}

function nullableNumberProp(event: ChatRuntimeEvent, key: string): number | null | undefined {
  const value = prop(event, key);
  if (value === null) return null;
  return typeof value === "number" ? value : undefined;
}

function booleanProp(event: ChatRuntimeEvent, key: string): boolean | undefined {
  const value = prop(event, key);
  return typeof value === "boolean" ? value : undefined;
}

function objectProp<T>(event: ChatRuntimeEvent, key: string): T | undefined {
  const value = prop(event, key);
  return value !== null && typeof value === "object" && !Array.isArray(value) ? value as T : undefined;
}

function arrayProp<T = unknown>(event: ChatRuntimeEvent, key: string): T[] | undefined {
  const value = prop(event, key);
  return Array.isArray(value) ? value as T[] : undefined;
}

function prop(event: ChatRuntimeEvent, key: string): unknown {
  const record = event as unknown as Record<string, unknown>;
  return record[key] ?? record[capitalize(key)];
}

function capitalize(value: string): string {
  return value.length === 0 ? value : value[0].toUpperCase() + value.slice(1);
}

function mergeDefined<T extends Record<string, unknown>>(
  previous: T | undefined,
  next: T
): T {
  const merged = { ...previous } as Record<string, unknown>;
  for (const [key, value] of Object.entries(next)) {
    if (value === undefined) continue;
    if (isPlainObject(value) && isPlainObject(merged[key])) {
      merged[key] = mergeDefined(merged[key] as Record<string, unknown>, value);
    } else {
      merged[key] = value;
    }
  }
  return merged as T;
}

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}
