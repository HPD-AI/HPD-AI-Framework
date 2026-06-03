import type {
  AgentEvent,
  BranchEvent,
  ToolResultPayload
} from "@hpd/hpd-agent-client";

export type ChatRuntimeEvent = AgentEvent | BranchEvent;

export type ChatTimelineItem =
  | UserMessageItem
  | AssistantTextItem
  | ReasoningItem
  | ErrorItem
  | ToolCallItem
  | PermissionItem
  | ClarificationItem
  | BranchEventItem
  | UnknownEventItem;

export type ChatTimelineItemBase = {
  id: string;
  sourceEvents: string[];
};

export type UserMessageItem = ChatTimelineItemBase & {
  kind: "user-message";
  text: string;
  messageId?: string;
};

export type AssistantTextItem = ChatTimelineItemBase & {
  kind: "assistant-text";
  text: string;
  messageId: string;
  role?: string;
  complete: boolean;
};

export type ReasoningItem = ChatTimelineItemBase & {
  kind: "reasoning";
  text: string;
  messageId: string;
  complete: boolean;
};

export type ErrorItem = ChatTimelineItemBase & {
  kind: "error";
  message: string;
  source?: string;
};

export type ToolCallStatus = "pending" | "running" | "completed" | "failed";

export type ToolCallItem = ChatTimelineItemBase & {
  kind: "tool-call";
  callId: string;
  name: string;
  messageId?: string;
  args?: unknown;
  result?: ToolResultPayload;
  status: ToolCallStatus;
  startedAt?: string;
  completedAt?: string;
  toolharnessName?: string;
  callType?: string;
  command?: CommandProjection;
  fileMutation?: FileMutationProjection;
  rawEvents: ChatRuntimeEvent[];
};

export type CommandProjection = {
  commandId?: string;
  command?: string;
  baseCommand?: string;
  category?: string;
  workingDirectory?: string;
  shell?: string;
  processId?: number;
  timeoutMilliseconds?: number;
  background?: boolean;
  autoBackgroundEligible?: boolean;
  liveOutput?: string;
  exitCode?: number | null;
  completionKind?: string;
  durationMilliseconds?: number;
  stdoutBytes?: number;
  stderrBytes?: number;
  combinedOutputBytes?: number;
  combinedBytesDiscarded?: number;
  outputTruncated?: boolean;
  outputDrainTimedOut?: boolean;
  outputEventsSuppressed?: boolean;
  artifacts?: CommandOutputArtifacts;
};

export type CommandOutputArtifacts = {
  stdoutArtifactPath?: string | null;
  stderrArtifactPath?: string | null;
  combinedOutputArtifactPath?: string | null;
  stdoutContentId?: string | null;
  stderrContentId?: string | null;
  combinedOutputContentId?: string | null;
  stdoutLocalPath?: string | null;
  stderrLocalPath?: string | null;
  combinedOutputLocalPath?: string | null;
};

export type FileMutationProjection = {
  type: "edit" | "write";
  path: string;
  displayPath: string;
  mutationKind?: string;
  created: boolean;
  changed: boolean;
  mode?: string;
  editCount?: number;
  replacementCount?: number;
  before?: unknown;
  after?: unknown;
  textEdits?: unknown[];
  hunks?: FileMutationHunk[];
  hunksTruncated?: boolean;
  diffStat?: FileMutationDiffStat;
  notes?: unknown[];
  replacements?: unknown[];
  normalizations?: unknown[];
};

export type FileMutationHunk = {
  oldStart: number;
  oldLines: number;
  newStart: number;
  newLines: number;
  lines: string[];
};

export type FileMutationDiffStat = {
  addedLines: number;
  removedLines: number;
  addedChars?: number;
  removedChars?: number;
};

export type PermissionItem = ChatTimelineItemBase & {
  kind: "permission";
  permissionId: string;
  sourceName: string;
  functionName: string;
  callId: string;
  description?: string;
  pending: boolean;
};

export type ClarificationItem = ChatTimelineItemBase & {
  kind: "clarification";
  requestId: string;
  sourceName: string;
  question: string;
  answer?: string;
  pending: boolean;
};

export type BranchEventItem = ChatTimelineItemBase & {
  kind: "branch-event";
  type: string;
  label: string;
  event: ChatRuntimeEvent;
};

export type UnknownEventItem = ChatTimelineItemBase & {
  kind: "unknown-event";
  type: string;
  event: ChatRuntimeEvent;
};
