import type {
  ClientToolAugmentation,
  ClientToolInvokeOutcomeKind,
  ToolResultContent,
} from './client-tools.js';
import type { AIContent } from './session.js';
import type {
  SubAgentContextPolicy,
  ThreadKind,
  ThreadVisibility,
} from './session.js';
import type { ThreadExecutionError } from './thread-execution.js';
import type {
  AgentOperationCapabilities,
  AgentOperationKind,
  AgentOperationSnapshot,
} from './operations.js';
import type { CompactionContinuation, ThreadCompactionRequest } from './run-config.js';

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

export type AgentMessageSource =
  | 'Unspecified'
  | 'UserInput'
  | 'AssistantOutput'
  | 'SystemInstruction'
  | 'RuntimeContext'
  | 'BackgroundNotification'
  | 'ToolResult'
  | 'PermissionResponse'
  | 'Steering'
  | 'Internal';

export type AgentMessageVisibility =
  | 'Transcript'
  | 'Hidden'
  | 'Diagnostic';

export type AgentMessagePersistence =
  | 'ThreadHistory'
  | 'ModelContextOnly'
  | 'None';

export const AgentMessagePolicyProperties = {
  SOURCE: 'hpd.message.source',
  VISIBILITY: 'hpd.message.visibility',
  PERSISTENCE: 'hpd.message.persistence',
} as const;

/**
 * Event type constants matching C# EventTypes.cs
 * Uses SCREAMING_SNAKE_CASE for JSON discriminators
 */
export const EventTypes = {
  // Input Events
  USER_MESSAGES_INPUT: 'USER_MESSAGES_INPUT',
  COMPACT_THREAD_INPUT: 'COMPACT_THREAD_INPUT',
  AGENT_OPERATION_NOTIFICATION_INPUT: 'AGENT_OPERATION_NOTIFICATION_INPUT',

  // Durable Thread Events
  THREAD_CREATED: 'THREAD_CREATED',
  THREAD_UPDATED: 'THREAD_UPDATED',
  CONTENT_ADDED: 'CONTENT_ADDED',
  THREAD_MIDDLEWARE_STATE_COMMITTED: 'THREAD_MIDDLEWARE_STATE_COMMITTED',
  THREAD_HISTORY_COMPACTION_CHECKPOINT: 'THREAD_HISTORY_COMPACTION_CHECKPOINT',

  // Message Turn Lifecycle
  MESSAGE_TURN_STARTED: 'MESSAGE_TURN_STARTED',
  MESSAGE_TURN_FINISHED: 'MESSAGE_TURN_FINISHED',
  MESSAGE_TURN_ERROR: 'MESSAGE_TURN_ERROR',

  // Agent Turn (iteration within a message turn)
  AGENT_TURN_STARTED: 'AGENT_TURN_STARTED',
  AGENT_TURN_FINISHED: 'AGENT_TURN_FINISHED',
  PROVIDER_OPERATION_USAGE: 'PROVIDER_OPERATION_USAGE',
  PROVIDER_VALUATION_OBSERVATION: 'PROVIDER_VALUATION_OBSERVATION',
  STATE_SNAPSHOT: 'STATE_SNAPSHOT',
  THREAD_EXECUTION_STARTED: 'THREAD_EXECUTION_STARTED',
  THREAD_EXECUTION_FINISHED: 'THREAD_EXECUTION_FINISHED',
  AGENT_REQUEST_TERMINATED: 'AGENT_REQUEST_TERMINATED',
  SUBAGENT_INVOCATION_STARTED: 'SUBAGENT_INVOCATION_STARTED',
  SUBAGENT_INVOCATION_COMPLETED: 'SUBAGENT_INVOCATION_COMPLETED',
  SUBAGENT_INVOCATION_FAILED: 'SUBAGENT_INVOCATION_FAILED',
  SUBAGENT_INVOCATION_CANCELLED: 'SUBAGENT_INVOCATION_CANCELLED',

  // Content Streaming
  TEXT_MESSAGE_START: 'TEXT_MESSAGE_START',
  TEXT_DELTA: 'TEXT_DELTA',
  TEXT_MESSAGE_END: 'TEXT_MESSAGE_END',
  USER_MESSAGE: 'USER_MESSAGE',

  // Reasoning (extended thinking)
  REASONING_MESSAGE_START: 'REASONING_MESSAGE_START',
  REASONING_DELTA: 'REASONING_DELTA',
  REASONING_MESSAGE_END: 'REASONING_MESSAGE_END',

  // Tool Execution
  TOOL_CALL_START: 'TOOL_CALL_START',
  TOOL_CALL_ARGS: 'TOOL_CALL_ARGS',
  TOOL_CALL_END: 'TOOL_CALL_END',
  TOOL_CALL_RESULT: 'TOOL_CALL_RESULT',

  // Permissions
  PERMISSION_REQUEST: 'PERMISSION_REQUEST',
  PERMISSION_RESPONSE: 'PERMISSION_RESPONSE',

  // Continuation (for long-running tasks)
  CONTINUATION_REQUEST: 'CONTINUATION_REQUEST',
  CONTINUATION_RESPONSE: 'CONTINUATION_RESPONSE',

  // Clarification
  CLARIFICATION_REQUEST: 'CLARIFICATION_REQUEST',
  CLARIFICATION_RESPONSE: 'CLARIFICATION_RESPONSE',

  // Middleware
  MIDDLEWARE_ERROR: 'MIDDLEWARE_ERROR',
  COMPACTION: 'COMPACTION',

  // Client Tools
  CLIENT_TOOL_INVOKE_REQUEST: 'CLIENT_TOOL_INVOKE_REQUEST',
  CLIENT_TOOL_INVOKE_OUTCOME: 'CLIENT_TOOL_INVOKE_OUTCOME',
  CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME: 'CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME',

  // Observability (optional, for debugging)
  COLLAPSED_TOOLS_VISIBLE: 'COLLAPSED_TOOLS_VISIBLE',
  CONTAINER_EXPANDED: 'CONTAINER_EXPANDED',
  PERMISSION_CHECK: 'PERMISSION_CHECK',
  ITERATION_START: 'ITERATION_START',
  CIRCUIT_BREAKER_TRIGGERED: 'CIRCUIT_BREAKER_TRIGGERED',
  INTERNAL_PARALLEL_TOOL_EXECUTION: 'INTERNAL_PARALLEL_TOOL_EXECUTION',
  FUNCTION_RETRY: 'FUNCTION_RETRY',
  MODEL_CALL_RETRY: 'MODEL_CALL_RETRY',
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
  AGENT_OPERATION_REGISTERED: 'AGENT_OPERATION_REGISTERED',
  AGENT_OPERATION_TRANSITIONED: 'AGENT_OPERATION_TRANSITIONED',
  AGENT_OPERATION_NOTIFICATION_QUEUED: 'AGENT_OPERATION_NOTIFICATION_QUEUED',
  AGENT_OPERATION_NOTIFICATION_DELIVERED: 'AGENT_OPERATION_NOTIFICATION_DELIVERED',
  AGENT_OPERATION_NOTIFICATION_SUPPRESSED: 'AGENT_OPERATION_NOTIFICATION_SUPPRESSED',
  AGENT_OPERATION_TOMBSTONED: 'AGENT_OPERATION_TOMBSTONED',
  AGENT_OPERATION_TOMBSTONE_EVICTED: 'AGENT_OPERATION_TOMBSTONE_EVICTED',

  // Control
  INTERRUPTION_REQUEST: 'INTERRUPTION_REQUEST',
  STEERING_INPUT: 'STEERING_INPUT',
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
  metadata?: AgentMetadata | Record<string, string> | null;
  eventId?: string;
  sessionId?: string;
  threadId?: string;
  /** Durable identity of the thread execution that produced or owns this event. */
  threadExecutionId?: string | null;
  threadSequenceNumber?: number;
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
  | 'ambiguousRequest'
  | 'executionEnded'
  | 'runtimeUnavailable';

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

export type AgentRequestTerminalKind = 'Expired' | 'Cancelled' | 'Abandoned';

/** Durable terminal fact for an Agent request that received no response. */
export interface AgentRequestTerminatedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_REQUEST_TERMINATED;
  requestId: string;
  sourceName: string;
  terminalKind: AgentRequestTerminalKind;
  reason?: string | null;
  terminatedAt: string;
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
  threadExecutionId?: string | null;
}

export interface UserMessageInput {
  role?: string;
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
}

export interface UserMessagesInputEvent extends AgentInputEvent {
  type: typeof EventTypes.USER_MESSAGES_INPUT;
  messages?: UserMessageInput[] | null;
}

export interface CompactThreadInputEvent extends AgentInputEvent {
  type: typeof EventTypes.COMPACT_THREAD_INPUT;
  request?: ThreadCompactionRequest;
}

export interface AgentOperationNotification {
  notificationId: string;
  operationId: string;
  name: string;
  providerStatus: string;
  summary?: string | null;
}

export interface AgentOperationNotificationInputEvent extends AgentInputEvent {
  type: typeof EventTypes.AGENT_OPERATION_NOTIFICATION_INPUT;
  notifications: AgentOperationNotification[];
}

// ============================================
// Durable Thread Events
// ============================================

export interface ThreadCreatedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_CREATED;
  defaultAgentId: string;
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
  subAgentTaskName?: string | null;
  invocationId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  contextPolicy?: SubAgentContextPolicy | null;
  forkedFrom?: string | null;
  forkedAtMessageId?: string | null;
  forkedAtMessageIndex?: number | null;
  childThreads?: string[] | null;
  ancestors?: Record<string, string> | null;
}

export interface ThreadUpdatedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_UPDATED;
  defaultAgentId: string;
  name?: string | null;
  description?: string | null;
  tags?: string[] | null;
  threadMetadata?: Record<string, unknown> | null;
  threadKind?: ThreadKind;
  visibility?: ThreadVisibility;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  subAgentTaskName?: string | null;
  invocationId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  contextPolicy?: SubAgentContextPolicy | null;
  forkedFrom?: string | null;
  forkedAtMessageId?: string | null;
  forkedAtMessageIndex?: number | null;
  childThreads?: string[] | null;
  ancestors?: Record<string, string> | null;
}

export interface ContentAddedEvent extends BaseEvent {
  type: typeof EventTypes.CONTENT_ADDED;
  messageId: string;
  role: string;
  content: unknown;
  authorName?: string | null;
  createdAt?: string | null;
  clientInputId?: string | null;
  source?: AgentMessageSource;
  visibility?: AgentMessageVisibility;
  persistence?: AgentMessagePersistence;
  additionalProperties?: Record<string, unknown> | null;
}

export interface ThreadMiddlewareStateCommittedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_MIDDLEWARE_STATE_COMMITTED;
  state: Record<string, string>;
}

export interface ThreadHistoryCompactionCheckpointEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_HISTORY_COMPACTION_CHECKPOINT;
  compactionId: string;
  point: { kind: string; messageId?: string | null; turnId?: string | null; expectedJournalGeneration?: number | null };
  preservation: { kind: string; count?: number | null; tokenBudget?: number | null };
  compactedMessageIds: string[];
  preservedMessageIds: string[];
  carriedUserMessageSourceIds: string[];
  afterPointMessageIds: string[];
  replacementMessages: unknown[];
  strategy: { kind: string; instructions?: string | null };
  commitMode: 0 | 1 | 'Soft' | 'Hard';
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
  usage: MessageTurnUsageSummary;
  timestamp: string;
}

export interface MessageTurnErrorEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_TURN_ERROR;
  isError: true;
  errorMessage: string;
  errorType?: string | null;
  messageTurnId: string;
  usage: MessageTurnUsageSummary;
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
  messageTurnId: string;
  iteration: number;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  family: ProviderClientFamily;
  outcome: ProviderOperationOutcome;
  usage?: UsageDetails | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
}

export type ProviderClientFamily =
  | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8
  | 'Chat' | 'TextToSpeech' | 'SpeechToText' | 'Realtime'
  | 'ImageGeneration' | 'Embeddings' | 'HostedFiles'
  | 'VoiceActivityDetection' | 'EndOfTurnDetection';

export type ProviderOperationOutcome =
  | 0 | 1 | 2 | 3
  | 'Succeeded' | 'Failed' | 'Cancelled' | 'Unknown';

export type ProviderOperationKind =
  | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9
  | 'ChatModelResponse' | 'RealtimeModelResponse' | 'SpeechToText'
  | 'TextToSpeech' | 'RealtimeInputTranscription' | 'ImageGeneration'
  | 'Embeddings' | 'HostedFileOperation' | 'VoiceActivityDetection'
  | 'EndOfTurnDetection';

export interface ProviderUsageMeasurement {
  sourceEventId: string;
  messageTurnId: string;
  threadSequenceNumber: number;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  operationKind: ProviderOperationKind;
  family: ProviderClientFamily;
  outcome: ProviderOperationOutcome;
  usage?: UsageDetails | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
}

export interface MessageTurnUsageSummary {
  operations: ProviderUsageMeasurement[];
}

export interface ProviderOperationUsageEvent extends BaseEvent {
  type: typeof EventTypes.PROVIDER_OPERATION_USAGE;
  messageTurnId: string;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  operationKind: ProviderOperationKind;
  family: ProviderClientFamily;
  outcome: ProviderOperationOutcome;
  usage?: UsageDetails | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
}

export interface ProviderValuationObservationEvent extends BaseEvent {
  type: typeof EventTypes.PROVIDER_VALUATION_OBSERVATION;
  messageTurnId: string;
  sourceEventId: string;
  observation: Record<string, unknown>;
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

export interface ThreadExecutionStartedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_EXECUTION_STARTED;
  threadExecutionId: string;
  agentId: string;
  startedAt: string;
}

export interface ThreadExecutionFinishedEvent extends BaseEvent {
  type: typeof EventTypes.THREAD_EXECUTION_FINISHED;
  threadExecutionId: string;
  agentId: string;
  outcome: ThreadExecutionOutcome;
  finishedAt: string;
  error?: ThreadExecutionError | null;
}

export type ThreadExecutionOutcome = 'Succeeded' | 'Failed' | 'Cancelled';

export type AgentInvocationMode = 'Synchronous' | 'Background';

/** Durable parent-side record that a delegation to a child agent began. */
export interface SubAgentInvocationStartedEvent extends BaseEvent {
  type: typeof EventTypes.SUBAGENT_INVOCATION_STARTED;
  invocationId: string;
  parentToolCallId: string;
  childAgentId: string;
  childSessionId: string;
  childThreadId: string;
  roleName: string;
  taskName: string;
  mode: AgentInvocationMode;
}

/** Durable parent-side record that a child delegation completed successfully. */
export interface SubAgentInvocationCompletedEvent extends BaseEvent {
  type: typeof EventTypes.SUBAGENT_INVOCATION_COMPLETED;
  invocationId: string;
  summary?: string | null;
}

/** Durable parent-side record that a child delegation failed. */
export interface SubAgentInvocationFailedEvent extends BaseEvent {
  type: typeof EventTypes.SUBAGENT_INVOCATION_FAILED;
  invocationId: string;
  errorType: string;
  message: string;
}

/** Durable parent-side record that a child delegation was cancelled. */
export interface SubAgentInvocationCancelledEvent extends BaseEvent {
  type: typeof EventTypes.SUBAGENT_INVOCATION_CANCELLED;
  invocationId: string;
  reason?: string | null;
}

export interface ToolInvocationInfo {
  batchId: string;
  callId: string;
  functionName: string;
  toolCallIndex: number;
}

export interface FunctionInvocationSnapshot {
  agentName: string;
  functionCallId: string;
  functionName: string;
  conversationId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  traceId?: string | null;
  invocation?: ToolInvocationInfo | null;
  batchId?: string | null;
  toolCallIndex?: number | null;
}

export interface AgentOperationRegisteredEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_REGISTERED;
  operation: AgentOperationSnapshot;
}

export interface AgentOperationTransitionedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_TRANSITIONED;
  operationId: string;
  previousVersion: number;
  operation: AgentOperationSnapshot;
  providerDeduplicationKey?: string | null;
}

export interface AgentOperationNotificationQueuedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_NOTIFICATION_QUEUED;
  notification: AgentOperationNotification;
  queuedAt: string;
}

export interface AgentOperationNotificationDeliveredEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_NOTIFICATION_DELIVERED;
  notificationId: string;
  deliveredAt: string;
}

export interface AgentOperationNotificationSuppressedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_NOTIFICATION_SUPPRESSED;
  operationId: string;
  reason: string;
  suppressedAt: string;
}

export interface AgentOperationTombstone {
  operationId: string;
  address: import('./operations.js').AgentExecutionAddress;
  providerDeduplicationKeys: string[];
  providerStatus: import('./operations.js').AgentOperationProviderStatus;
  finishedAt: string;
  finalVersion: number;
}

export interface AgentOperationTombstonedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_TOMBSTONED;
  tombstone: AgentOperationTombstone;
}

export interface AgentOperationTombstoneEvictedEvent extends BaseEvent {
  type: typeof EventTypes.AGENT_OPERATION_TOMBSTONE_EVICTED;
  operationId: string;
  evictedAt: string;
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
  source?: AgentMessageSource;
  visibility?: AgentMessageVisibility;
  persistence?: AgentMessagePersistence;
  authorName?: string | null;
  createdAt?: string | null;
  clientInputId?: string | null;
  additionalProperties?: Record<string, unknown> | null;
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

export interface UserMessageEvent extends BaseEvent {
  type: typeof EventTypes.USER_MESSAGE;
  messageId: string;
  text: string;
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
export type ToolCallType = 'Function' | 'Skill' | 'SubAgent' | 'MultiAgent' | 'McpServer' | 'OpenApi';

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
  messageId: string;
  name: string;
  argsJson: string;
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

export interface MiddlewareErrorEvent extends BaseEvent {
  type: typeof EventTypes.MIDDLEWARE_ERROR;
  sourceName: string;
  errorMessage: string;
}

export type CompactionStatus = 0 | 1 | 2 | 3;
export type CompactionOrigin = 0 | 1 | 2;

export const CompactionStatuses = {
  Started: 0,
  Skipped: 1,
  Failed: 2,
  Completed: 3,
} as const satisfies Record<string, CompactionStatus>;

export interface CompactionEvent extends BaseEvent {
  type: typeof EventTypes.COMPACTION;
  agentName: string;
  iteration: number;
  status: CompactionStatus;
  startedAt: string;
  completedAt: string;
  strategy?: string | null;
  originalMessageCount?: number | null;
  compactedMessageCount?: number | null;
  messagesRemoved?: number | null;
  summaryContent?: string | null;
  reason?: string | null;
  continuation: CompactionContinuation;
  origin: CompactionOrigin;
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

export interface ClientToolInvokeOutcomeEvent extends BaseEvent, ResponseMetadata {
  type: typeof EventTypes.CLIENT_TOOL_INVOKE_OUTCOME;
  requestId: string;
  outcome: ClientToolInvokeOutcomeKind;
  content?: ToolResultContent[];
  errorMessage?: string;
  clientOperationId?: string;
  operationKind?: AgentOperationKind | null;
  operationCapabilities?: AgentOperationCapabilities;
  augmentation?: ClientToolAugmentation;
}

export type ClientToolBackgroundOperationOutcomeState =
  | 'Completed'
  | 'Faulted'
  | 'Cancelled'
  | 'Unknown';

export interface ClientToolBackgroundOperationOutcomeEvent extends AgentInputEvent {
  type: typeof EventTypes.CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME;
  clientOperationId: string;
  state: ClientToolBackgroundOperationOutcomeState;
  content?: ToolResultContent[];
  augmentation?: ClientToolAugmentation;
  errorMessage?: string | null;
  errorType?: string | null;
  cancellationReason?: string | null;
  metadata?: Record<string, string> | null;
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

export interface SteeringInputEvent extends AgentInputEvent {
  type: typeof EventTypes.STEERING_INPUT;
  messages: UserMessageInput[];
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
  | CompactThreadInputEvent
  | AgentOperationNotificationInputEvent
  // Durable Thread Events
  | ThreadCreatedEvent
  | ThreadUpdatedEvent
  | ContentAddedEvent
  | ThreadMiddlewareStateCommittedEvent
  | ThreadHistoryCompactionCheckpointEvent
  // Message Turn Events
  | MessageTurnStartedEvent
  | MessageTurnFinishedEvent
  | MessageTurnErrorEvent
  // Agent Turn Events
  | AgentTurnStartedEvent
  | AgentTurnFinishedEvent
  | ProviderOperationUsageEvent
  | ProviderValuationObservationEvent
  | StateSnapshotEvent
  | ThreadExecutionStartedEvent
  | ThreadExecutionFinishedEvent
  | AgentRequestTerminatedEvent
  | SubAgentInvocationStartedEvent
  | SubAgentInvocationCompletedEvent
  | SubAgentInvocationFailedEvent
  | SubAgentInvocationCancelledEvent
  | AgentOperationRegisteredEvent
  | AgentOperationTransitionedEvent
  | AgentOperationNotificationQueuedEvent
  | AgentOperationNotificationDeliveredEvent
  | AgentOperationNotificationSuppressedEvent
  | AgentOperationTombstonedEvent
  | AgentOperationTombstoneEvictedEvent
  | IterationContextSnapshotEvent
  | MiddlewareStateSnapshotEvent
  | MiddlewareStateChangedEvent
  // Content Events
  | TextMessageStartEvent
  | TextDeltaEvent
  | TextMessageEndEvent
  | UserMessageEvent
  // Reasoning Events
  | ReasoningMessageStartEvent
  | ReasoningDeltaEvent
  | ReasoningMessageEndEvent
  // Tool Events
  | ToolCallStartEvent
  | ToolCallArgsEvent
  | ToolCallEndEvent
  | ToolCallResultEvent
  // Permission Events
  | PermissionRequestEvent
  | PermissionResponseEvent
  // Continuation Events
  | ContinuationRequestEvent
  | ContinuationResponseEvent
  // Clarification Events
  | ClarificationRequestEvent
  | ClarificationResponseEvent
  // Middleware Events
  | MiddlewareErrorEvent
  | CompactionEvent
  // Client Tool Events
  | ClientToolInvokeRequestEvent
  | ClientToolInvokeOutcomeEvent
  | ClientToolBackgroundOperationOutcomeEvent
  // Control Events
  | InterruptionRequestEvent
  | SteeringInputEvent;

export type AgentEvent = KnownAgentEvent | UnknownAgentEvent;

export type AgentRunInputEvent =
  | UserMessagesInputEvent
  | CompactThreadInputEvent
  | ClientToolBackgroundOperationOutcomeEvent
  | InterruptionRequestEvent
  | SteeringInputEvent;

/** Response event accepted by the hosted Agent response route. */
export type AgentResponseInput =
  | PermissionResponseEvent
  | ContinuationResponseEvent
  | ClarificationResponseEvent
  | ClientToolInvokeOutcomeEvent
  | AgentResponseEvent;

export type AgentEventOfType<TType extends KnownAgentEvent['type']> =
  Extract<KnownAgentEvent, { type: TType }>;

// ============================================
// Type Guards
// ============================================

export function isTextDeltaEvent(event: BaseEvent): event is TextDeltaEvent {
  return event.type === EventTypes.TEXT_DELTA;
}

export function isUserMessageEvent(event: BaseEvent): event is UserMessageEvent {
  return event.type === EventTypes.USER_MESSAGE;
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

export function isErrorEvent(event: AgentEvent): event is AgentEvent & AgentErrorEvent {
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

export function isAgentRequestEvent(event: AgentEvent): event is AgentEvent & AgentRequestEvent {
  return hasStringProperty(event, 'requestId') &&
    hasStringProperty(event, 'sourceName') &&
    !isKnownResponseEvent(event);
}

export function isAgentResponseEvent(event: BaseEvent): event is AgentResponseEvent {
  return hasStringProperty(event, 'requestId') &&
    isKnownResponseEvent(event);
}

function isKnownResponseEvent(event: BaseEvent): boolean {
  return event.type === EventTypes.PERMISSION_RESPONSE ||
    event.type === EventTypes.CONTINUATION_RESPONSE ||
    event.type === EventTypes.CLARIFICATION_RESPONSE ||
    event.type === EventTypes.CLIENT_TOOL_INVOKE_OUTCOME;
}

function hasStringProperty(event: BaseEvent, property: string): boolean {
  return typeof (event as unknown as Record<string, unknown>)[property] === 'string';
}

