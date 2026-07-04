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
  /** Force compaction on this turn */
  triggerCompaction?: boolean;
  /** Skip compaction on this turn */
  skipCompaction?: boolean;
  /** Per-turn compaction behavior override */
  compactionBehaviorOverride?: CompactionBehavior;
  /** Structured output options */
  structuredOutput?: Record<string, unknown>;
  /** Client tools, context, state, and metadata available to this run */
  clientToolInput?: AgentClientInput;
  /** Live client app providers to bind for this run */
  clientAppProviders?: ClientAppProviderReference[];
}
