import type { ClientToolAugmentation, ToolResultContent } from './client-tools.js';
import type { AIContent } from './session.js';
import type { ThreadKind, ThreadVisibility } from './session.js';
import type { BackgroundOperationStatus } from './thread-run.js';

export interface UsageDetails {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  inputAudioTokenCount?: number | null;
  inputTextTokenCount?: number | null;
  outputAudioTokenCount?: number | null;
  outputTextTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

/**
 * Event type constants matching C# EventTypes.cs
 * Uses SCREAMING_SNAKE_CASE for JSON discriminators
 */
export const EventTypes = {
  // Input Events
  USER_MESSAGES_INPUT: 'USER_MESSAGES_INPUT',

  // Durable Thread Events
  THREAD_CREATED: 'THREAD_CREATED',
  THREAD_FORKED: 'THREAD_FORKED',
  THREAD_METADATA_UPDATED: 'THREAD_METADATA_UPDATED',
  THREAD_TREE_UPDATED: 'THREAD_TREE_UPDATED',
  MESSAGE_STARTED: 'MESSAGE_STARTED',
  MESSAGE_COMPLETED: 'MESSAGE_COMPLETED',
  CONTENT_ADDED: 'CONTENT_ADDED',
  THREAD_MIDDLEWARE_STATE_COMMITTED: 'THREAD_MIDDLEWARE_STATE_COMMITTED',
  THREAD_HISTORY_COMPACTED: 'THREAD_HISTORY_COMPACTED',

  // Message Turn Lifecycle
  MESSAGE_TURN_STARTED: 'MESSAGE_TURN_STARTED',
  MESSAGE_TURN_FINISHED: 'MESSAGE_TURN_FINISHED',
  MESSAGE_TURN_ERROR: 'MESSAGE_TURN_ERROR',

  // Agent Turn (iteration within a message turn)
  AGENT_TURN_STARTED: 'AGENT_TURN_STARTED',
  AGENT_TURN_FINISHED: 'AGENT_TURN_FINISHED',
  STATE_SNAPSHOT: 'STATE_SNAPSHOT',
  THREAD_RUN_STARTED: 'THREAD_RUN_STARTED',
  THREAD_RUN_COMPLETED: 'THREAD_RUN_COMPLETED',

  // Content Streaming
  TEXT_MESSAGE_START: 'TEXT_MESSAGE_START',
  TEXT_DELTA: 'TEXT_DELTA',
  TEXT_MESSAGE_END: 'TEXT_MESSAGE_END',

  // Reasoning (extended thinking)
  REASONING_MESSAGE_START: 'REASONING_MESSAGE_START',
  REASONING_DELTA: 'REASONING_DELTA',
  REASONING_MESSAGE_END: 'REASONING_MESSAGE_END',

  // Tool Execution
  TOOL_CALL_START: 'TOOL_CALL_START',
  TOOL_CALL_ARGS: 'TOOL_CALL_ARGS',
  TOOL_CALL_END: 'TOOL_CALL_END',
  TOOL_CALL_RESULT: 'TOOL_CALL_RESULT',

  // Request Lifecycle
  AGENT_REQUEST_STARTED: 'AGENT_REQUEST_STARTED',
  AGENT_REQUEST_RESOLVED: 'AGENT_REQUEST_RESOLVED',
  AGENT_REQUEST_EXPIRED: 'AGENT_REQUEST_EXPIRED',
  AGENT_REQUEST_CANCELLED: 'AGENT_REQUEST_CANCELLED',
  AGENT_RESPONSE_REJECTED: 'AGENT_RESPONSE_REJECTED',

  // Permissions
  PERMISSION_REQUEST: 'PERMISSION_REQUEST',
  PERMISSION_RESPONSE: 'PERMISSION_RESPONSE',
  PERMISSION_APPROVED: 'PERMISSION_APPROVED',
  PERMISSION_DENIED: 'PERMISSION_DENIED',

  // Continuation (for long-running tasks)
  CONTINUATION_REQUEST: 'CONTINUATION_REQUEST',
  CONTINUATION_RESPONSE: 'CONTINUATION_RESPONSE',

  // Clarification
  CLARIFICATION_REQUEST: 'CLARIFICATION_REQUEST',
  CLARIFICATION_RESPONSE: 'CLARIFICATION_RESPONSE',

  // Middleware
  MIDDLEWARE_PROGRESS: 'MIDDLEWARE_PROGRESS',
  MIDDLEWARE_ERROR: 'MIDDLEWARE_ERROR',

  // Client Tools
  CLIENT_TOOL_INVOKE_REQUEST: 'CLIENT_TOOL_INVOKE_REQUEST',
  CLIENT_TOOL_INVOKE_RESPONSE: 'CLIENT_TOOL_INVOKE_RESPONSE',
  CLIENT_TOOL_GROUPS_REGISTERED: 'CLIENT_TOOL_GROUPS_REGISTERED',

  // Observability (optional, for debugging)
  COLLAPSED_TOOLS_VISIBLE: 'COLLAPSED_TOOLS_VISIBLE',
  CONTAINER_EXPANDED: 'CONTAINER_EXPANDED',
  MIDDLEWARE_PIPELINE_START: 'MIDDLEWARE_PIPELINE_START',
  MIDDLEWARE_PIPELINE_END: 'MIDDLEWARE_PIPELINE_END',
  PERMISSION_CHECK: 'PERMISSION_CHECK',
  ITERATION_START: 'ITERATION_START',
  CIRCUIT_BREAKER_TRIGGERED: 'CIRCUIT_BREAKER_TRIGGERED',
  HISTORY_REDUCTION_CACHE: 'HISTORY_REDUCTION_CACHE',
  INTERNAL_PARALLEL_TOOL_EXECUTION: 'INTERNAL_PARALLEL_TOOL_EXECUTION',
  INTERNAL_RETRY: 'INTERNAL_RETRY',
  FUNCTION_RETRY: 'FUNCTION_RETRY',
  DELTA_SENDING_ACTIVATED: 'DELTA_SENDING_ACTIVATED',
  PLAN_MODE_ACTIVATED: 'PLAN_MODE_ACTIVATED',
  NESTED_AGENT_INVOKED: 'NESTED_AGENT_INVOKED',
  DOCUMENT_PROCESSED: 'DOCUMENT_PROCESSED',
  INTERNAL_MESSAGE_PREPARED: 'INTERNAL_MESSAGE_PREPARED',
  REQUEST_EVENT_PROCESSED: 'REQUEST_EVENT_PROCESSED',
  AGENT_DECISION: 'AGENT_DECISION',
  AGENT_COMPLETION: 'AGENT_COMPLETION',
  ITERATION_CONTEXT_SNAPSHOT: 'ITERATION_CONTEXT_SNAPSHOT',
  MIDDLEWARE_STATE_SNAPSHOT: 'MIDDLEWARE_STATE_SNAPSHOT',
  MIDDLEWARE_STATE_CHANGED: 'MIDDLEWARE_STATE_CHANGED',
  COLLAPSING_STATE: 'COLLAPSING_STATE',
  BACKGROUND_OPERATION_STARTED: 'BACKGROUND_OPERATION_STARTED',
  BACKGROUND_OPERATION_STATUS: 'BACKGROUND_OPERATION_STATUS',

  // Control
  INTERRUPTION_REQUEST: 'INTERRUPTION_REQUEST',
} as const;

export type EventType = (typeof EventTypes)[keyof typeof EventTypes];

// ============================================
// Agent Metadata
// ============================================

export interface AgentMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string;
  agentChain: string[];
  depth: number;
  isSubAgent: boolean;
}

// ============================================
// Base Event
// ============================================

export interface BaseEvent {
  version?: string;
  type: string;
  isError?: boolean;
  errorMessage?: string | null;
  metadata?: AgentMetadata;
  eventId?: string;
  sessionId?: string;
  threadId?: string;
  sequenceNumber?: number;
  timestamp?: string;
  eventFlowId?: string;
  streamId?: string;
}

export type ResponsePolicy = 'firstValidResponseWins' | 'targetedResponder';

export type RequestVisibility = 'allObservers' | 'eligibleRespondersOnly';

export interface ResponderTarget {
  responderId?: string | null;
  responderGroup?: string | null;
  requiredCapabilities?: string[];
}

export type RespondStatus =
  | 'accepted'
  | 'notFound'
  | 'alreadyResolved'
  | 'timedOut'
  | 'cancelled'
  | 'responseTypeMismatch'
  | 'targetMismatch'
  | 'ambiguousRequest';

export interface RespondResult {
  status: RespondStatus;
  requestId: string;
  message?: string | null;
  accepted: boolean;
}

export interface ResponseMetadata {
  responderId?: string | null;
  responderGroup?: string | null;
  capabilities?: string[];
}

export interface AgentErrorEvent extends BaseEvent {
  isError: true;
  errorMessage: string;
  errorType?: string | null;
}

export interface AgentRequestEvent extends BaseEvent {
  requestId: string;
  sourceName: string;
  responsePolicy?: ResponsePolicy;
  target?: ResponderTarget | null;
  visibility?: RequestVisibility;
}

export interface AgentResponseEvent extends BaseEvent, ResponseMetadata {
  requestId: string;
  sourceName?: string;
}

/**
 * Event emitted by a toolharness, middleware, or host extension that this client
 * version does not model explicitly. The raw payload is preserved so
 * applications can opt into local custom-event handling via onAny().
 */
export interface UnknownAgentEvent extends BaseEvent {
  [key: string]: unknown;
}

// ============================================
// Input Events
// ============================================

export interface AgentInputEvent extends BaseEvent {
  clientInputId?: string | null;
  sessionId?: string;
  threadId?: string;
  agentId?: string;
  runConfig?: import('./run-config.js').RunConfig;
}

export interface UserMessageInput {
  role?: string;
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
}

export interface UserMessagesInputEvent extends AgentInputEvent {
  type: typeof EventTypes.USER_MESSAGES_INPUT;
  messages: UserMessageInput[];
}

// ============================================
// Durable Thread Events
// ============================================

export interface ThreadCreatedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_CREATED;
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
  threadMetadata?: Record<string, unknown> | null;
  createdAt: string;
  threadKind?: ThreadKind;
  visibility?: ThreadVisibility;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  subAgentRunId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  sessionPolicy?: string | null;
  threadPolicy?: string | null;
}

export interface ThreadForkedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_FORKED;
  sourceThreadId: string;
  fromMessageId?: string | null;
  resolvedMessageIndex?: number | null;
  ancestors?: Record<string, string> | null;
}

export interface ThreadMetadataUpdatedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_METADATA_UPDATED;
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
  threadMetadata?: Record<string, unknown> | null;
  threadKind?: ThreadKind;
  visibility?: ThreadVisibility;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  subAgentRunId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  sessionPolicy?: string | null;
  threadPolicy?: string | null;
}

export interface ThreadTreeUpdatedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_TREE_UPDATED;
  forkedFrom?: string | null;
  forkedAtMessageId?: string | null;
  forkedAtMessageIndex?: number | null;
  childThreads: string[];
}

export interface MessageStartedEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_STARTED;
  messageId: string;
  role: string;
  authorName?: string | null;
  createdAt?: string | null;
  clientInputId?: string | null;
  additionalProperties?: Record<string, unknown> | null;
}

export interface MessageCompletedEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_COMPLETED;
  messageId: string;
}

export interface ContentAddedEvent extends BaseEvent {
  type: typeof EventTypes.CONTENT_ADDED;
  messageId: string;
  content: unknown;
}

export interface ThreadMiddlewareStateCommittedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_MIDDLEWARE_STATE_COMMITTED;
  state: Record<string, string>;
}

export interface ThreadHistoryCompactedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_HISTORY_COMPACTED;
  compactionId: string;
  modelReducedMessageIds: string[];
  durableRemovedMessageIds: string[];
  replacementMessages: unknown[];
  strategyKind: string;
  retentionKind: string;
  boundaryKind: string;
  summaryContent?: string | null;
  compactedAt: string;
}

// ============================================
// Message Turn Events
// ============================================

export interface MessageTurnStartedEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_TURN_STARTED;
  messageTurnId: string;
  conversationId: string;
  agentName: string;
  timestamp: string;
}

export interface MessageTurnFinishedEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_TURN_FINISHED;
  messageTurnId: string;
  conversationId: string;
  agentName: string;
  duration: string;
  usage?: UsageDetails | null;
  timestamp: string;
}

export interface MessageTurnErrorEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_TURN_ERROR;
  isError: true;
  errorMessage: string;
  errorType?: string | null;
  messageTurnId?: string | null;
  conversationId?: string | null;
  agentId?: string | null;
  agentName?: string | null;
}

// ============================================
// Agent Turn Events
// ============================================

export interface AgentTurnStartedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_TURN_STARTED;
  iteration: number;
}

export interface AgentTurnFinishedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_TURN_FINISHED;
  iteration: number;
}

export interface StateSnapshotEvent extends BaseEvent {
  type: typeof EventTypes.STATE_SNAPSHOT;
  currentIteration: number;
  maxIterations: number;
  isTerminated: boolean;
  terminationReason?: string;
  consecutiveErrorCount: number;
  completedFunctions: string[];
  agentName: string;
  timestamp: string;
}

export interface ThreadRunStartedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_RUN_STARTED;
  runtimeRunId: string;
  agentId: string;
  startedAt: string;
}

export interface ThreadRunCompletedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_RUN_COMPLETED;
  runtimeRunId: string;
  agentId: string;
  cancelled: boolean;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface BackgroundOperationStartedEvent extends BaseEvent {
  type: typeof EventTypes.BACKGROUND_OPERATION_STARTED;
  continuationToken: unknown;
  status: BackgroundOperationStatus;
  operationId?: string | null;
}

export interface BackgroundOperationStatusEvent extends BaseEvent {
  type: typeof EventTypes.BACKGROUND_OPERATION_STATUS;
  continuationToken: unknown;
  status: BackgroundOperationStatus;
  statusMessage?: string | null;
}

export interface ContextMessageSnapshot {
  role: string;
  text: string;
}

export interface ToolContextSnapshot {
  name: string;
  description: string;
  toolharnessName?: string;
  callType?: ToolCallType;
  isContainer: boolean;
  inputSchemaJson?: string;
}

export interface IterationContextSnapshotEvent extends BaseEvent {
  type: typeof EventTypes.ITERATION_CONTEXT_SNAPSHOT;
  agentName: string;
  iteration: number;
  totalMessageCount: number;
  contextMessageCount: number;
  contextMessages: ContextMessageSnapshot[];
  instructions?: string;
  toolCount: number;
  tools: ToolContextSnapshot[];
  timestamp: string;
}

export type StateScope = 'Session' | 'Thread';

export interface MiddlewareStateEntrySnapshot {
  key: string;
  type: string;
  propertyName: string;
  scope: StateScope;
  persistent: boolean;
  version: number;
  json?: unknown;
  error?: string;
  redacted: boolean;
}

export interface MiddlewareStateSnapshotEvent extends BaseEvent {
  type: typeof EventTypes.MIDDLEWARE_STATE_SNAPSHOT;
  agentName: string;
  sessionId?: string;
  threadId?: string;
  iteration: number;
  phase: string;
  batchId?: string;
  functionCallId?: string;
  toolCallIndex?: number;
  stateCount: number;
  states: MiddlewareStateEntrySnapshot[];
  timestamp: string;
}

export interface MiddlewareStateChange {
  key: string;
  type: string;
  propertyName: string;
  scope: StateScope;
  persistent: boolean;
  version: number;
  changeType: 'added' | 'updated' | 'removed' | string;
  before?: unknown;
  after?: unknown;
  error?: string;
  redacted: boolean;
}

export interface MiddlewareStateChangedEvent extends BaseEvent {
  type: typeof EventTypes.MIDDLEWARE_STATE_CHANGED;
  agentName: string;
  sessionId?: string;
  threadId?: string;
  iteration: number;
  phase: string;
  batchId?: string;
  functionCallId?: string;
  toolCallIndex?: number;
  changeCount: number;
  changes: MiddlewareStateChange[];
  timestamp: string;
}

// ============================================
// Content Events
// ============================================

export interface TextMessageStartEvent extends BaseEvent {
  type: typeof EventTypes.TEXT_MESSAGE_START;
  messageId: string;
  role: string;
  clientInputId?: string | null;
  optimistic?: boolean;
}

export interface TextDeltaEvent extends BaseEvent {
  type: typeof EventTypes.TEXT_DELTA;
  text: string;
  messageId: string;
}

export interface TextMessageEndEvent extends BaseEvent {
  type: typeof EventTypes.TEXT_MESSAGE_END;
  messageId: string;
}

// ============================================
// Reasoning Events
// ============================================

export interface ReasoningMessageStartEvent extends BaseEvent {
  type: typeof EventTypes.REASONING_MESSAGE_START;
  messageId: string;
  role: string;
}

export interface ReasoningDeltaEvent extends BaseEvent {
  type: typeof EventTypes.REASONING_DELTA;
  text: string;
  messageId: string;
}

export interface ReasoningMessageEndEvent extends BaseEvent {
  type: typeof EventTypes.REASONING_MESSAGE_END;
  messageId: string;
}

// ============================================
// Tool Events
// ============================================

/** Indicates the kind of capability behind a tool call. Serialised as a string on the wire. */
export type ToolCallType = 'Function' | 'Skill' | 'SubAgent' | 'MultiAgent' | 'MCPServer' | 'OpenApi';

export interface ToolCallStartEvent extends BaseEvent {
  type: typeof EventTypes.TOOL_CALL_START;
  callId: string;
  name: string;
  messageId: string;
  /** The toolharness that owns this tool, if any. */
  toolharnessName?: string;
  /** The kind of capability (AIFunction, Skill, SubAgent, etc.). */
  callType?: ToolCallType;
}

export interface ToolCallArgsEvent extends BaseEvent {
  type: typeof EventTypes.TOOL_CALL_ARGS;
  callId: string;
  argsJson: string;
}

export interface ToolCallEndEvent extends BaseEvent {
  type: typeof EventTypes.TOOL_CALL_END;
  callId: string;
}

export interface ToolResultPayload {
  text?: string;
  json?: unknown;
  content?: ToolResultContent[];
  resultType?: string;
}

export interface ToolCallResultEvent extends BaseEvent {
  type: typeof EventTypes.TOOL_CALL_RESULT;
  callId: string;
  /** The tool/function name, when provided by the runtime. */
  name?: string;
  result: ToolResultPayload;
  /** The toolharness that owns this tool, if any. */
  toolharnessName?: string;
  /** The kind of capability (AIFunction, Skill, SubAgent, etc.). */
  callType?: ToolCallType;
}

// ============================================
// Request Lifecycle Events
// ============================================

export interface AgentRequestStartedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_REQUEST_STARTED;
  requestId: string;
  sourceName: string;
  requestEventType: string;
  expectedResponseEventType: string;
  responsePolicy: ResponsePolicy;
  target?: ResponderTarget | null;
  visibility: RequestVisibility;
  startedAt: string;
}

export interface AgentRequestResolvedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_REQUEST_RESOLVED;
  requestId: string;
  sourceName: string;
  requestEventType: string;
  responseEventType: string;
  responderId?: string | null;
  responderGroup?: string | null;
  resolvedAt: string;
}

export interface AgentRequestExpiredEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_REQUEST_EXPIRED;
  requestId: string;
  sourceName: string;
  requestEventType: string;
  timeout: string | number;
  expiredAt: string;
}

export interface AgentRequestCancelledEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_REQUEST_CANCELLED;
  requestId: string;
  sourceName: string;
  requestEventType: string;
  reason?: string | null;
  cancelledAt: string;
}

export interface AgentResponseRejectedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_RESPONSE_REJECTED;
  requestId: string;
  responseEventType: string;
  status: RespondStatus;
  reason?: string | null;
  responderId?: string | null;
  responderGroup?: string | null;
  rejectedAt: string;
}

// ============================================
// Permission Events
// ============================================

export type PermissionChoice = 'ask' | 'allow_always' | 'deny_always';

export interface PermissionRequestEvent extends BaseEvent {
  type: typeof EventTypes.PERMISSION_REQUEST;
  permissionId: string;
  sourceName: string;
  functionName: string;
  description?: string;
  callId: string;
  arguments?: Record<string, unknown>;
}

export interface PermissionResponseEvent extends BaseEvent, ResponseMetadata {
  type: typeof EventTypes.PERMISSION_RESPONSE;
  permissionId: string;
  sourceName: string;
  approved: boolean;
  reason?: string;
  choice?: PermissionChoice;
}

export interface PermissionApprovedEvent extends BaseEvent {
  type: typeof EventTypes.PERMISSION_APPROVED;
  permissionId: string;
  sourceName: string;
}

export interface PermissionDeniedEvent extends BaseEvent {
  type: typeof EventTypes.PERMISSION_DENIED;
  permissionId: string;
  sourceName: string;
  reason: string;
}

// ============================================
// Continuation Events
// ============================================

export interface ContinuationRequestEvent extends BaseEvent {
  type: typeof EventTypes.CONTINUATION_REQUEST;
  continuationId: string;
  sourceName: string;
  currentIteration: number;
  maxIterations: number;
}

export interface ContinuationResponseEvent extends BaseEvent, ResponseMetadata {
  type: typeof EventTypes.CONTINUATION_RESPONSE;
  continuationId: string;
  sourceName: string;
  approved: boolean;
  extensionAmount?: number;
}

// ============================================
// Clarification Events
// ============================================

export interface ClarificationRequestEvent extends BaseEvent {
  type: typeof EventTypes.CLARIFICATION_REQUEST;
  requestId: string;
  sourceName: string;
  question: string;
  agentName?: string;
  options?: string[];
}

export interface ClarificationResponseEvent extends BaseEvent, ResponseMetadata {
  type: typeof EventTypes.CLARIFICATION_RESPONSE;
  requestId: string;
  sourceName: string;
  question: string;
  answer: string;
}

// ============================================
// Middleware Events
// ============================================

export interface MiddlewareProgressEvent extends BaseEvent {
  type: typeof EventTypes.MIDDLEWARE_PROGRESS;
  sourceName: string;
  message: string;
  percentComplete?: number;
}

export interface MiddlewareErrorEvent extends BaseEvent {
  type: typeof EventTypes.MIDDLEWARE_ERROR;
  sourceName: string;
  errorMessage: string;
}

// ============================================
// Client Tool Events
// ============================================

export interface ClientToolInvokeRequestEvent extends BaseEvent {
  type: typeof EventTypes.CLIENT_TOOL_INVOKE_REQUEST;
  requestId: string;
  sourceName?: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  description?: string;
  responsePolicy?: ResponsePolicy;
  target?: ResponderTarget | null;
  visibility?: RequestVisibility;
}

export interface ClientToolInvokeResponseEvent extends BaseEvent, ResponseMetadata {
  type: typeof EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
  requestId: string;
  content: ToolResultContent[];
  success: boolean;
  errorMessage?: string;
  augmentation?: ClientToolAugmentation;
}

// ============================================
// Control Events
// ============================================

export type InterruptionSource = 'User' | 'System' | 'Parent' | 'Middleware';

export interface InterruptionRequestEvent extends AgentInputEvent {
  type: typeof EventTypes.INTERRUPTION_REQUEST;
  reason: string;
  source: InterruptionSource;
}

export interface clientToolHarnessesRegisteredEvent extends BaseEvent {
  type: typeof EventTypes.CLIENT_TOOL_GROUPS_REGISTERED;
  registeredToolHarnesses: string[];
  totalTools: number;
  timestamp: string;
}

// ============================================
// Union Type (Core Events)
// ============================================

/**
 * Union of all core agent events that clients typically handle.
 * Does not include observability events (which are for debugging).
 */
export type KnownAgentEvent =
  // Input Events
  | UserMessagesInputEvent
  // Durable Thread Events
  | ThreadCreatedEvent
  | ThreadForkedEvent
  | ThreadMetadataUpdatedEvent
  | ThreadTreeUpdatedEvent
  | MessageStartedEvent
  | MessageCompletedEvent
  | ContentAddedEvent
  | ThreadMiddlewareStateCommittedEvent
  | ThreadHistoryCompactedEvent
  // Message Turn Events
  | MessageTurnStartedEvent
  | MessageTurnFinishedEvent
  | MessageTurnErrorEvent
  // Agent Turn Events
  | AgentTurnStartedEvent
  | AgentTurnFinishedEvent
  | StateSnapshotEvent
  | ThreadRunStartedEvent
  | ThreadRunCompletedEvent
  | BackgroundOperationStartedEvent
  | BackgroundOperationStatusEvent
  | IterationContextSnapshotEvent
  | MiddlewareStateSnapshotEvent
  | MiddlewareStateChangedEvent
  // Content Events
  | TextMessageStartEvent
  | TextDeltaEvent
  | TextMessageEndEvent
  // Reasoning Events
  | ReasoningMessageStartEvent
  | ReasoningDeltaEvent
  | ReasoningMessageEndEvent
  // Tool Events
  | ToolCallStartEvent
  | ToolCallArgsEvent
  | ToolCallEndEvent
  | ToolCallResultEvent
  // Request Lifecycle Events
  | AgentRequestStartedEvent
  | AgentRequestResolvedEvent
  | AgentRequestExpiredEvent
  | AgentRequestCancelledEvent
  | AgentResponseRejectedEvent
  // Permission Events
  | PermissionRequestEvent
  | PermissionResponseEvent
  | PermissionApprovedEvent
  | PermissionDeniedEvent
  // Continuation Events
  | ContinuationRequestEvent
  | ContinuationResponseEvent
  // Clarification Events
  | ClarificationRequestEvent
  | ClarificationResponseEvent
  // Middleware Events
  | MiddlewareProgressEvent
  | MiddlewareErrorEvent
  // Client Tool Events
  | ClientToolInvokeRequestEvent
  | ClientToolInvokeResponseEvent
  | clientToolHarnessesRegisteredEvent
  // Control Events
  | InterruptionRequestEvent;

export type AgentEvent = KnownAgentEvent | UnknownAgentEvent;

export type AgentRunInputEvent =
  | UserMessagesInputEvent
  | PermissionResponseEvent
  | ContinuationResponseEvent
  | ClarificationResponseEvent
  | ClientToolInvokeResponseEvent
  | InterruptionRequestEvent;

export type AgentEventOfType<TType extends KnownAgentEvent['type']> =
  Extract<KnownAgentEvent, { type: TType }>;

// ============================================
// Type Guards
// ============================================

export function isTextDeltaEvent(event: BaseEvent): event is TextDeltaEvent {
  return event.type === EventTypes.TEXT_DELTA;
}

export function isToolCallStartEvent(event: BaseEvent): event is ToolCallStartEvent {
  return event.type === EventTypes.TOOL_CALL_START;
}

export function isPermissionRequestEvent(event: BaseEvent): event is PermissionRequestEvent {
  return event.type === EventTypes.PERMISSION_REQUEST;
}

export function isReasoningMessageStartEvent(event: BaseEvent): event is ReasoningMessageStartEvent {
  return event.type === EventTypes.REASONING_MESSAGE_START;
}

export function isReasoningDeltaEvent(event: BaseEvent): event is ReasoningDeltaEvent {
  return event.type === EventTypes.REASONING_DELTA;
}

export function isReasoningMessageEndEvent(event: BaseEvent): event is ReasoningMessageEndEvent {
  return event.type === EventTypes.REASONING_MESSAGE_END;
}

export function isMessageTurnFinishedEvent(event: BaseEvent): event is MessageTurnFinishedEvent {
  return event.type === EventTypes.MESSAGE_TURN_FINISHED;
}

export function isMessageTurnErrorEvent(event: BaseEvent): event is MessageTurnErrorEvent {
  return event.type === EventTypes.MESSAGE_TURN_ERROR;
}

export function isErrorEvent(event: BaseEvent): event is AgentErrorEvent {
  return event.isError === true && typeof event.errorMessage === 'string';
}

export function isClarificationRequestEvent(event: BaseEvent): event is ClarificationRequestEvent {
  return event.type === EventTypes.CLARIFICATION_REQUEST;
}

export function isContinuationRequestEvent(event: BaseEvent): event is ContinuationRequestEvent {
  return event.type === EventTypes.CONTINUATION_REQUEST;
}

export function isClientToolInvokeRequestEvent(
  event: BaseEvent
): event is ClientToolInvokeRequestEvent {
  return event.type === EventTypes.CLIENT_TOOL_INVOKE_REQUEST;
}

export function isAgentRequestResolvedEvent(event: BaseEvent): event is AgentRequestResolvedEvent {
  return event.type === EventTypes.AGENT_REQUEST_RESOLVED;
}

export function isAgentRequestExpiredEvent(event: BaseEvent): event is AgentRequestExpiredEvent {
  return event.type === EventTypes.AGENT_REQUEST_EXPIRED;
}

export function isAgentRequestCancelledEvent(event: BaseEvent): event is AgentRequestCancelledEvent {
  return event.type === EventTypes.AGENT_REQUEST_CANCELLED;
}

export function isclientToolHarnessesRegisteredEvent(
  event: BaseEvent
): event is clientToolHarnessesRegisteredEvent {
  return event.type === EventTypes.CLIENT_TOOL_GROUPS_REGISTERED;
}

export function isAgentRequestEvent(event: BaseEvent): event is AgentRequestEvent {
  return hasStringProperty(event, 'requestId') &&
    hasStringProperty(event, 'sourceName') &&
    !isRequestLifecycleEvent(event) &&
    !isKnownResponseEvent(event);
}

export function isAgentResponseEvent(event: BaseEvent): event is AgentResponseEvent {
  return hasStringProperty(event, 'requestId') &&
    !isRequestLifecycleEvent(event) &&
    isKnownResponseEvent(event);
}

function isRequestLifecycleEvent(event: BaseEvent): boolean {
  return event.type === EventTypes.AGENT_REQUEST_STARTED ||
    event.type === EventTypes.AGENT_REQUEST_RESOLVED ||
    event.type === EventTypes.AGENT_REQUEST_EXPIRED ||
    event.type === EventTypes.AGENT_REQUEST_CANCELLED ||
    event.type === EventTypes.AGENT_RESPONSE_REJECTED;
}

function isKnownResponseEvent(event: BaseEvent): boolean {
  return event.type === EventTypes.PERMISSION_RESPONSE ||
    event.type === EventTypes.CONTINUATION_RESPONSE ||
    event.type === EventTypes.CLARIFICATION_RESPONSE ||
    event.type === EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
}

function hasStringProperty(event: BaseEvent, property: string): boolean {
  return typeof (event as unknown as Record<string, unknown>)[property] === 'string';
}

