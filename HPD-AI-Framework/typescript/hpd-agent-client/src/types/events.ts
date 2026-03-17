/**
 * Event type constants matching C# EventTypes.cs
 * Uses SCREAMING_SNAKE_CASE for JSON discriminators
 */
export const EventTypes = {
  // Message Turn Lifecycle
  MESSAGE_TURN_STARTED: 'MESSAGE_TURN_STARTED',
  MESSAGE_TURN_FINISHED: 'MESSAGE_TURN_FINISHED',
  MESSAGE_TURN_ERROR: 'MESSAGE_TURN_ERROR',

  // Agent Turn (iteration within a message turn)
  AGENT_TURN_STARTED: 'AGENT_TURN_STARTED',
  AGENT_TURN_FINISHED: 'AGENT_TURN_FINISHED',
  STATE_SNAPSHOT: 'STATE_SNAPSHOT',

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

  // Permissions (bidirectional)
  PERMISSION_REQUEST: 'PERMISSION_REQUEST',
  PERMISSION_RESPONSE: 'PERMISSION_RESPONSE',
  PERMISSION_APPROVED: 'PERMISSION_APPROVED',
  PERMISSION_DENIED: 'PERMISSION_DENIED',

  // Continuation (for long-running tasks)
  CONTINUATION_REQUEST: 'CONTINUATION_REQUEST',
  CONTINUATION_RESPONSE: 'CONTINUATION_RESPONSE',

  // Clarification (bidirectional)
  CLARIFICATION_REQUEST: 'CLARIFICATION_REQUEST',
  CLARIFICATION_RESPONSE: 'CLARIFICATION_RESPONSE',

  // Middleware
  MIDDLEWARE_PROGRESS: 'MIDDLEWARE_PROGRESS',
  MIDDLEWARE_ERROR: 'MIDDLEWARE_ERROR',

  // Client Tools (bidirectional)
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
  CHECKPOINT: 'CHECKPOINT',
  INTERNAL_PARALLEL_TOOL_EXECUTION: 'INTERNAL_PARALLEL_TOOL_EXECUTION',
  INTERNAL_RETRY: 'INTERNAL_RETRY',
  FUNCTION_RETRY: 'FUNCTION_RETRY',
  DELTA_SENDING_ACTIVATED: 'DELTA_SENDING_ACTIVATED',
  PLAN_MODE_ACTIVATED: 'PLAN_MODE_ACTIVATED',
  NESTED_AGENT_INVOKED: 'NESTED_AGENT_INVOKED',
  DOCUMENT_PROCESSED: 'DOCUMENT_PROCESSED',
  INTERNAL_MESSAGE_PREPARED: 'INTERNAL_MESSAGE_PREPARED',
  BIDIRECTIONAL_EVENT_PROCESSED: 'BIDIRECTIONAL_EVENT_PROCESSED',
  AGENT_DECISION: 'AGENT_DECISION',
  AGENT_COMPLETION: 'AGENT_COMPLETION',
  ITERATION_MESSAGES: 'ITERATION_MESSAGES',
  SCHEMA_CHANGED: 'SCHEMA_CHANGED',
  COLLAPSING_STATE: 'COLLAPSING_STATE',

  // Audio Events (TTS)
  SYNTHESIS_STARTED: 'SYNTHESIS_STARTED',
  AUDIO_CHUNK: 'AUDIO_CHUNK',
  SYNTHESIS_COMPLETED: 'SYNTHESIS_COMPLETED',

  // Audio Events (STT)
  TRANSCRIPTION_DELTA: 'TRANSCRIPTION_DELTA',
  TRANSCRIPTION_COMPLETED: 'TRANSCRIPTION_COMPLETED',

  // Audio Events (Interruption)
  USER_INTERRUPTED: 'USER_INTERRUPTED',
  SPEECH_PAUSED: 'SPEECH_PAUSED',
  SPEECH_RESUMED: 'SPEECH_RESUMED',

  // Audio Events (Preemptive Generation)
  PREEMPTIVE_GENERATION_STARTED: 'PREEMPTIVE_GENERATION_STARTED',
  PREEMPTIVE_GENERATION_DISCARDED: 'PREEMPTIVE_GENERATION_DISCARDED',

  // Audio Events (VAD)
  VAD_START_OF_SPEECH: 'VAD_START_OF_SPEECH',
  VAD_END_OF_SPEECH: 'VAD_END_OF_SPEECH',

  // Audio Events (Metrics)
  AUDIO_PIPELINE_METRICS: 'AUDIO_PIPELINE_METRICS',

  // Audio Events (Turn Detection)
  TURN_DETECTED: 'TURN_DETECTED',

  // Audio Events (Filler)
  FILLER_AUDIO_PLAYED: 'FILLER_AUDIO_PLAYED',
} as const;

export type EventType = (typeof EventTypes)[keyof typeof EventTypes];

// ============================================
// Execution Context
// ============================================

export interface AgentExecutionContext {
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
  version: string;
  type: string;
  executionContext?: AgentExecutionContext;
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
  timestamp: string;
}

export interface MessageTurnErrorEvent extends BaseEvent {
  type: typeof EventTypes.MESSAGE_TURN_ERROR;
  message: string;
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

// ============================================
// Content Events
// ============================================

export interface TextMessageStartEvent extends BaseEvent {
  type: typeof EventTypes.TEXT_MESSAGE_START;
  messageId: string;
  role: string;
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
  /** The toolkit that owns this tool, if any. */
  toolkitName?: string;
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

export interface ToolCallResultEvent extends BaseEvent {
  type: typeof EventTypes.TOOL_CALL_RESULT;
  callId: string;
  result: string;
  /** The toolkit that owns this tool, if any. */
  toolkitName?: string;
  /** The kind of capability (AIFunction, Skill, SubAgent, etc.). */
  callType?: ToolCallType;
}

// ============================================
// Permission Events (Bidirectional)
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

export interface PermissionResponseEvent extends BaseEvent {
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
// Continuation Events (Bidirectional)
// ============================================

export interface ContinuationRequestEvent extends BaseEvent {
  type: typeof EventTypes.CONTINUATION_REQUEST;
  continuationId: string;
  sourceName: string;
  currentIteration: number;
  maxIterations: number;
}

export interface ContinuationResponseEvent extends BaseEvent {
  type: typeof EventTypes.CONTINUATION_RESPONSE;
  continuationId: string;
  sourceName: string;
  approved: boolean;
  extensionAmount?: number;
}

// ============================================
// Clarification Events (Bidirectional)
// ============================================

export interface ClarificationRequestEvent extends BaseEvent {
  type: typeof EventTypes.CLARIFICATION_REQUEST;
  requestId: string;
  sourceName: string;
  question: string;
  agentName?: string;
  options?: string[];
}

export interface ClarificationResponseEvent extends BaseEvent {
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
// Client Tool Events (Bidirectional)
// ============================================

export interface ClientToolInvokeRequestEvent extends BaseEvent {
  type: typeof EventTypes.CLIENT_TOOL_INVOKE_REQUEST;
  requestId: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  description?: string;
}

export interface ClientToolInvokeResponseEvent extends BaseEvent {
  type: typeof EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
  requestId: string;
  content: Array<{ type: string; [key: string]: unknown }>;
  success: boolean;
  errorMessage?: string;
  augmentation?: Record<string, unknown>;
}

export interface clientToolKitsRegisteredEvent extends BaseEvent {
  type: typeof EventTypes.CLIENT_TOOL_GROUPS_REGISTERED;
  registeredToolKits: string[];
  totalTools: number;
  timestamp: string;
}

// ============================================
// Audio Events (TTS - Synthesis)
// ============================================

export interface SynthesisStartedEvent extends BaseEvent {
  type: typeof EventTypes.SYNTHESIS_STARTED;
  synthesisId: string;
  modelId?: string;
  voice?: string;
  streamId?: string;
}

export interface AudioChunkEvent extends BaseEvent {
  type: typeof EventTypes.AUDIO_CHUNK;
  synthesisId: string;
  base64Audio: string;
  mimeType: string;
  chunkIndex: number;
  duration: string; // TimeSpan as ISO 8601 duration
  isLast: boolean;
  streamId?: string;
}

export interface SynthesisCompletedEvent extends BaseEvent {
  type: typeof EventTypes.SYNTHESIS_COMPLETED;
  synthesisId: string;
  wasInterrupted: boolean;
  totalChunks: number;
  deliveredChunks: number;
  streamId?: string;
}

// ============================================
// Audio Events (STT - Transcription)
// ============================================

export interface TranscriptionDeltaEvent extends BaseEvent {
  type: typeof EventTypes.TRANSCRIPTION_DELTA;
  transcriptionId: string;
  text: string;
  isFinal: boolean;
  confidence?: number;
}

export interface TranscriptionCompletedEvent extends BaseEvent {
  type: typeof EventTypes.TRANSCRIPTION_COMPLETED;
  transcriptionId: string;
  finalText: string;
  processingDuration: string;
}

// ============================================
// Audio Events (Interruption)
// ============================================

export interface UserInterruptedEvent extends BaseEvent {
  type: typeof EventTypes.USER_INTERRUPTED;
  transcribedText?: string;
}

export interface SpeechPausedEvent extends BaseEvent {
  type: typeof EventTypes.SPEECH_PAUSED;
  synthesisId: string;
  reason: 'user_speaking' | 'potential_interruption';
}

export interface SpeechResumedEvent extends BaseEvent {
  type: typeof EventTypes.SPEECH_RESUMED;
  synthesisId: string;
  pauseDuration: string;
}

// ============================================
// Audio Events (Preemptive Generation)
// ============================================

export interface PreemptiveGenerationStartedEvent extends BaseEvent {
  type: typeof EventTypes.PREEMPTIVE_GENERATION_STARTED;
  generationId: string;
  turnCompletionProbability: number;
}

export interface PreemptiveGenerationDiscardedEvent extends BaseEvent {
  type: typeof EventTypes.PREEMPTIVE_GENERATION_DISCARDED;
  generationId: string;
  reason: 'user_continued' | 'low_confidence';
}

// ============================================
// Audio Events (VAD)
// ============================================

export interface VadStartOfSpeechEvent extends BaseEvent {
  type: typeof EventTypes.VAD_START_OF_SPEECH;
  /** ISO 8601 duration from audio start (e.g., "PT5.2S" for 5.2 seconds) */
  timestamp: string;
  /** Speech probability (0.0 - 1.0) */
  speechProbability: number;
}

export interface VadEndOfSpeechEvent extends BaseEvent {
  type: typeof EventTypes.VAD_END_OF_SPEECH;
  /** ISO 8601 duration from audio start (e.g., "PT5.2S" for 5.2 seconds) */
  timestamp: string;
  /** ISO 8601 duration of speech (e.g., "PT2.5S" for 2.5 seconds of speech) */
  speechDuration: string;
  /** Speech probability (0.0 - 1.0) */
  speechProbability: number;
}

// ============================================
// Audio Events (Metrics)
// ============================================

export interface AudioPipelineMetricsEvent extends BaseEvent {
  type: typeof EventTypes.AUDIO_PIPELINE_METRICS;
  metricType: 'latency' | 'quality' | 'throughput' | 'error';
  metricName: string;
  value: number;
  unit?: string;
}

// ============================================
// Audio Events (Turn Detection)
// ============================================

export interface TurnDetectedEvent extends BaseEvent {
  type: typeof EventTypes.TURN_DETECTED;
  transcribedText: string;
  completionProbability: number;
  silenceDuration: string;
  detectionMethod: 'heuristic' | 'ml' | 'manual' | 'timeout';
}

// ============================================
// Audio Events (Filler)
// ============================================

export interface FillerAudioPlayedEvent extends BaseEvent {
  type: typeof EventTypes.FILLER_AUDIO_PLAYED;
  phrase: string;
  duration: string;
}

// ============================================
// Union Type (Core Events)
// ============================================

/**
 * Union of all core agent events that clients typically handle.
 * Does not include observability events (which are for debugging).
 */
export type AgentEvent =
  // Message Turn Events
  | MessageTurnStartedEvent
  | MessageTurnFinishedEvent
  | MessageTurnErrorEvent
  // Agent Turn Events
  | AgentTurnStartedEvent
  | AgentTurnFinishedEvent
  | StateSnapshotEvent
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
  | clientToolKitsRegisteredEvent
  // Audio Events (TTS)
  | SynthesisStartedEvent
  | AudioChunkEvent
  | SynthesisCompletedEvent
  // Audio Events (STT)
  | TranscriptionDeltaEvent
  | TranscriptionCompletedEvent
  // Audio Events (Interruption)
  | UserInterruptedEvent
  | SpeechPausedEvent
  | SpeechResumedEvent
  // Audio Events (Preemptive Generation)
  | PreemptiveGenerationStartedEvent
  | PreemptiveGenerationDiscardedEvent
  // Audio Events (VAD)
  | VadStartOfSpeechEvent
  | VadEndOfSpeechEvent
  // Audio Events (Metrics/Turn/Filler)
  | AudioPipelineMetricsEvent
  | TurnDetectedEvent
  | FillerAudioPlayedEvent;

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

export function isclientToolKitsRegisteredEvent(
  event: BaseEvent
): event is clientToolKitsRegisteredEvent {
  return event.type === EventTypes.CLIENT_TOOL_GROUPS_REGISTERED;
}

// Audio Type Guards

export function isSynthesisStartedEvent(event: BaseEvent): event is SynthesisStartedEvent {
  return event.type === EventTypes.SYNTHESIS_STARTED;
}

export function isAudioChunkEvent(event: BaseEvent): event is AudioChunkEvent {
  return event.type === EventTypes.AUDIO_CHUNK;
}

export function isSynthesisCompletedEvent(event: BaseEvent): event is SynthesisCompletedEvent {
  return event.type === EventTypes.SYNTHESIS_COMPLETED;
}

export function isTranscriptionDeltaEvent(event: BaseEvent): event is TranscriptionDeltaEvent {
  return event.type === EventTypes.TRANSCRIPTION_DELTA;
}

export function isTranscriptionCompletedEvent(
  event: BaseEvent
): event is TranscriptionCompletedEvent {
  return event.type === EventTypes.TRANSCRIPTION_COMPLETED;
}

export function isUserInterruptedEvent(event: BaseEvent): event is UserInterruptedEvent {
  return event.type === EventTypes.USER_INTERRUPTED;
}

export function isSpeechPausedEvent(event: BaseEvent): event is SpeechPausedEvent {
  return event.type === EventTypes.SPEECH_PAUSED;
}

export function isSpeechResumedEvent(event: BaseEvent): event is SpeechResumedEvent {
  return event.type === EventTypes.SPEECH_RESUMED;
}

export function isVadStartOfSpeechEvent(event: BaseEvent): event is VadStartOfSpeechEvent {
  return event.type === EventTypes.VAD_START_OF_SPEECH;
}

export function isVadEndOfSpeechEvent(event: BaseEvent): event is VadEndOfSpeechEvent {
  return event.type === EventTypes.VAD_END_OF_SPEECH;
}

export function isAudioPipelineMetricsEvent(event: BaseEvent): event is AudioPipelineMetricsEvent {
  return event.type === EventTypes.AUDIO_PIPELINE_METRICS;
}

export function isTurnDetectedEvent(event: BaseEvent): event is TurnDetectedEvent {
  return event.type === EventTypes.TURN_DETECTED;
}

export function isFillerAudioPlayedEvent(event: BaseEvent): event is FillerAudioPlayedEvent {
  return event.type === EventTypes.FILLER_AUDIO_PLAYED;
}
