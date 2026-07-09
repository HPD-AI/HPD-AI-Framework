import type { AgentClientInput } from './client-tools.js';
import type { ClientAppProviderReference } from './client-tool-providers.js';

/**
 * Chat-level sampling parameters.
 * Maps to ChatRunConfigDto on the server.
 */
export interface ChatRunConfig {
  /** Sampling temperature — 0.0 to 1.0 */
  temperature?: number;
  /** Top-P nucleus sampling — 0.0 to 1.0 */
  topP?: number;
  /** Top-K sampling candidate count */
  topK?: number;
  maxOutputTokens?: number;
  frequencyPenalty?: number;
  presencePenalty?: number;
  /** Provider-local model override inside ChatOptions */
  modelId?: string;
  /** Stop sequences that end generation */
  stopSequences?: string[];
  /** Provider-specific chat additional properties */
  additionalProperties?: Record<string, unknown>;
  /** Reasoning configuration */
  reasoning?: Record<string, unknown>;
}

export type AgentModelTransportMode = 0 | 1 | 2;
export type UploadStrategy = 0 | 1 | 2;
export type CompactionBehavior = 0 | 1;
export type CompactionRunMode = 0 | 1 | 2;
export type HistoryCountingUnit = 0 | 1;
export type SummaryStyle = 0 | 1;

export const CompactionRunModes = {
  Auto: 0,
  Force: 1,
  Disabled: 2,
} as const satisfies Record<string, CompactionRunMode>;

export const CompactionBehaviors = {
  Continue: 0,
  StopAfterCompaction: 1,
} as const satisfies Record<string, CompactionBehavior>;

export interface ClientProviderConfig {
  providerKey?: string;
  modelName?: string;
  apiKey?: string;
  endpoint?: string;
  customHeaders?: Record<string, string>;
  additionalProperties?: Record<string, unknown>;
  providerOptions?: Record<string, unknown>;
  httpReferer?: string;
  appName?: string;
}

export interface AgentRunClientConfig {
  providers?: Record<string, ClientProviderConfig>;
  chat?: ClientProviderConfig;
  textToSpeech?: ClientProviderConfig;
  speechToText?: ClientProviderConfig;
  realtime?: ClientProviderConfig;
  imageGeneration?: ClientProviderConfig;
  embeddings?: ClientProviderConfig;
  hostedFiles?: ClientProviderConfig;
  voiceActivityDetection?: ClientProviderConfig;
  endOfTurnDetection?: ClientProviderConfig;
}

export interface AudioRunConfig {
  enabled?: boolean;
  inputMode?: string;
  outputMode?: string;
  assistantOutputMode?: string;
  pacing?: Record<string, unknown>;
  progressiveRouteMode?: string;
  pushTextAggregationMode?: string;
  artifactCapturePolicy?: string;
  voiceId?: string;
  language?: string;
  outputFormat?: string;
  contentType?: string;
  speed?: number;
  enablePlayback?: boolean;
}

export interface ModelContextWindowOptions {
  providerKey?: string;
  modelId?: string;
  contextWindow?: number | null;
  inputTokenLimit?: number | null;
  outputTokenLimit?: number | null;
}

export interface MessageCountingCompactionOptions {
  $type: 'messageCounting';
  preserveFromMessageId?: string | null;
  preserveFromMessageTurnId?: string | null;
  preserveRecentUserTurnCount?: number;
}

export interface SummaryMemoryOptions {
  recentUserMessageTokenBudget?: number;
  preserveRecentUserMessagesSeparately?: boolean;
  reinjectCurrentContextAfterCompaction?: boolean;
  filterGeneratedContextWrappers?: boolean;
}

export interface SummarizingCompactionOptions {
  $type: 'summarizing';
  preserveFromMessageId?: string | null;
  preserveFromMessageTurnId?: string | null;
  preserveRecentUserTurnCount?: number;
  resummarizeAfterNewMessages?: number;
  customPrompt?: string | null;
  summarizerProvider?: ClientProviderConfig | null;
  useSingleSummary?: boolean;
  summaryStyle?: SummaryStyle;
  memory?: SummaryMemoryOptions;
}

export type CompactionStrategyOptions =
  | MessageCountingCompactionOptions
  | SummarizingCompactionOptions;

export interface CountCompactionTriggerOptions {
  $type: 'count';
  countingUnit?: HistoryCountingUnit;
  targetCount?: number;
  threshold?: number;
}

export type ContextWindowCompactionThresholdMode = 0 | 1;

export const ContextWindowCompactionThresholdModes = {
  Percentage: 0,
  TokenCount: 1,
} as const;

export interface ContextWindowCompactionTriggerOptions {
  $type: 'contextWindow';
  contextWindowSize?: number | null;
  thresholdMode?: ContextWindowCompactionThresholdMode;
  triggerPercentage?: number;
  triggerTokenCount?: number | null;
}

export interface CompositeCompactionTriggerOptions {
  $type: 'composite';
  anyOf: CompactionTriggerOptions[];
}

export type CompactionTriggerOptions =
  | CountCompactionTriggerOptions
  | ContextWindowCompactionTriggerOptions
  | CompositeCompactionTriggerOptions;

export interface ExactCompactedMessagesBoundaryOptions {
  $type: 'exactCompactedMessages';
}

export interface IncludePreviousMessagesBoundaryOptions {
  $type: 'includePreviousMessages';
  count: number;
}

export interface IncludeMessageTurnBoundaryOptions {
  $type: 'includeMessageTurn';
}

export interface IncludeToolCallGroupBoundaryOptions {
  $type: 'includeToolCallGroup';
}

export interface CompositeCompactionBoundaryOptions {
  $type: 'composite';
  policies: CompactionBoundaryOptions[];
}

export type CompactionBoundaryOptions =
  | ExactCompactedMessagesBoundaryOptions
  | IncludePreviousMessagesBoundaryOptions
  | IncludeMessageTurnBoundaryOptions
  | IncludeToolCallGroupBoundaryOptions
  | CompositeCompactionBoundaryOptions;

export interface PreserveThreadHistoryOptions {
  $type: 'preserve';
}

export interface CompactThreadHistoryOptions {
  $type: 'compact';
  boundary?: CompactionBoundaryOptions;
}

export type CompactionRetentionOptions =
  | PreserveThreadHistoryOptions
  | CompactThreadHistoryOptions;

export interface CompactionRunConfig {
  mode?: CompactionRunMode;
  behavior?: CompactionBehavior | null;
  trigger?: CompactionTriggerOptions | null;
  strategy?: CompactionStrategyOptions | null;
  retention?: CompactionRetentionOptions | null;
  modelContext?: ModelContextWindowOptions | null;
}

/**
 * Per-invocation agent run configuration.
 * All fields are optional — only set fields are sent to the server.
 * Maps to AgentRunConfig on the server.
 */
export interface RunConfig {
  /** Model transport override: auto, chat, or realtime */
  modelTransport?: AgentModelTransportMode;
  /** Provider-created client-family overrides for this run */
  clients?: AgentRunClientConfig;
  /** Provider key (e.g. "anthropic", "openai") */
  providerKey?: string;
  /** Model ID (e.g. "claude-sonnet-4-6") */
  modelId?: string;
  /** API key to use when switching providers */
  apiKey?: string;
  /** Endpoint URL override for the provider */
  providerEndpoint?: string;
  /** Custom HTTP headers for provider requests */
  customHeaders?: Record<string, string>;
  /** Provider-specific options interpreted by the selected backend provider */
  providerOptions?: Record<string, unknown>;
  /** System instructions replacement for this run */
  systemInstructions?: string;
  /** Additional system instructions appended to the agent's system prompt */
  additionalSystemInstructions?: string;
  /** Chat-level sampling parameters */
  chat?: ChatRunConfig;
  /** Per-tool permission overrides — key is tool name, value is allow/deny */
  permissionOverrides?: Record<string, boolean>;
  /** Per-run context values available to agent middleware and toolharness functions */
  contextOverrides?: Record<string, unknown>;
  /** Whether to use cached responses for this run */
  useCache?: boolean;
  /** Whether to coalesce streamed text deltas before sending to the client */
  coalesceDeltas?: boolean;
  /** Skip tool execution for this run */
  skipTools?: boolean;
  /** Run timeout as ISO 8601 duration (e.g. "PT5M") */
  runTimeout?: string;
  /** Client-visible conversation ID override */
  conversationIdOverride?: string;
  /** Allow provider background responses */
  allowBackgroundResponses?: boolean;
  /** Background polling interval as ISO 8601 duration */
  backgroundPollingInterval?: string;
  /** Background timeout as ISO 8601 duration */
  backgroundTimeout?: string;
  /** User message override for run-config-driven execution */
  userMessage?: string;
  /** DataContent upload strategy */
  uploadStrategy?: UploadStrategy;
  /** HPD audio runtime options */
  audio?: AudioRunConfig;
  /** Per-run compaction policy. Null or omitted uses the agent's configured defaults. */
  compaction?: CompactionRunConfig;
  /** Structured output options */
  structuredOutput?: Record<string, unknown>;
  /** Client tools, context, state, and metadata available to this run */
  clientToolInput?: AgentClientInput;
  /** Live client app providers to bind for this run */
  clientAppProviders?: ClientAppProviderReference[];
}
