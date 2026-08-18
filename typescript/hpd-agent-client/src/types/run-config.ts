import type { AgentClientInput } from './client-tools.js';
import type { ClientAppProviderReference } from './client-tool-providers.js';

/**
 * Chat-level sampling parameters.
 * Maps to ChatClientConfig on the server.
 */
export interface ChatClientConfig {
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
export type AgentApprovalPolicy = 0 | 1;
export type AgentSandboxPolicy = 0 | 1;
export type AgentSandboxEscapePolicy = 0 | 1;
export type AgentSandboxPathAccess = 0 | 1;
export type UploadStrategy = 0 | 1 | 2;
export type CompactionContinuation = 0 | 1;

export const CompactionContinuations = {
  Continue: 0,
  StopAfterCompaction: 1,
} as const satisfies Record<string, CompactionContinuation>;

export const AgentApprovalPolicies = {
  ReviewProtectedActions: 0,
  AutoApprove: 1,
} as const satisfies Record<string, AgentApprovalPolicy>;

export const AgentSandboxPolicies = {
  Enforced: 0,
  Disabled: 1,
} as const satisfies Record<string, AgentSandboxPolicy>;

export const AgentSandboxEscapePolicies = {
  Ask: 0,
  Deny: 1,
} as const satisfies Record<string, AgentSandboxEscapePolicy>;

export const AgentSandboxPathAccesses = {
  Read: 0,
  Write: 1,
} as const satisfies Record<string, AgentSandboxPathAccess>;

export interface AgentSecurityProfile {
  approval?: AgentApprovalPolicy;
  sandbox?: AgentSandboxPolicy;
  sandboxEscape?: AgentSandboxEscapePolicy;
}

export interface AgentSandboxPathGrant {
  access: AgentSandboxPathAccess;
  path: string;
}

export interface AgentSandboxConfiguration {
  filesystem?: AgentSandboxPathGrant[];
  network?: Record<string, unknown>;
  interactive?: Record<string, unknown>;
}

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

export interface TurnCountCompactionTrigger {
  $type: 'turnCount';
  turns: number;
}

export interface InputTokenCompactionTrigger {
  $type: 'inputTokens';
  inputTokens: number;
}

export interface ContextPercentageCompactionTrigger {
  $type: 'contextPercentage';
  totalInputTokens: number;
  percentage: number;
}

export type CompactionTrigger =
  | TurnCountCompactionTrigger
  | InputTokenCompactionTrigger
  | ContextPercentageCompactionTrigger;

export interface CompactAtCurrentHead {
  $type: 'currentHead';
}

export interface CompactAtMessage {
  $type: 'message';
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface CompactAtTurn {
  $type: 'turn';
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export type CompactionPoint = CompactAtCurrentHead | CompactAtMessage | CompactAtTurn;

export interface PreserveNoPreviousHistory {
  $type: 'none';
}

export interface PreservePreviousTurns {
  $type: 'previousTurns';
  count: number;
}

export interface PreviousItemCountLimit {
  $type: 'count';
  count: number;
}

export interface PreviousTokenBudgetLimit {
  $type: 'tokenBudget';
  tokens: number;
}

export type PreviousHistoryLimit = PreviousItemCountLimit | PreviousTokenBudgetLimit;

export interface PreservePreviousUserMessages {
  $type: 'previousUserMessages';
  limit: PreviousHistoryLimit;
}

export type CompactionPreservation =
  | PreserveNoPreviousHistory
  | PreservePreviousTurns
  | PreservePreviousUserMessages;

export interface RemovalCompaction {
  $type: 'removal';
}

export interface SummarizingCompaction {
  $type: 'summarizing';
  provider?: ClientProviderConfig | null;
  instructions?: string | null;
}

export type CompactionStrategy = RemovalCompaction | SummarizingCompaction;
export type CompactionCommitMode = 0 | 1;

export const CompactionCommitModes = {
  Soft: 0,
  Hard: 1,
} as const satisfies Record<string, CompactionCommitMode>;

export interface CompactionSpecification {
  point: CompactionPoint;
  preservation?: CompactionPreservation;
  strategy: CompactionStrategy;
  commitMode?: CompactionCommitMode;
}

export interface AutomaticCompactionPolicy {
  trigger: CompactionTrigger;
  compaction: CompactionSpecification;
  continuation?: CompactionContinuation;
}

export interface CompactionRunPolicy {
  automatic?: AutomaticCompactionPolicy | null;
}

export interface ThreadCompactionRequest {
  compaction: CompactionSpecification;
  continuation?: CompactionContinuation;
}

export interface InheritThreadForkCompaction {
  $type: 'inherit';
}

export interface DisableThreadForkCompaction {
  $type: 'disabled';
}

export interface ApplyThreadForkCompaction {
  $type: 'enabled';
  compaction: CompactionSpecification;
}

export type ThreadForkCompaction =
  | InheritThreadForkCompaction
  | DisableThreadForkCompaction
  | ApplyThreadForkCompaction;

/**
 * Per-invocation agent run configuration.
 * All fields are optional — only set fields are sent to the server.
 * Maps to AgentRunConfig on the server.
 */
export interface RunConfig {
  /** Independent approval, sandbox, and sandbox-escape controls. */
  security?: AgentSecurityProfile;
  /** Capabilities available while host sandbox isolation is enforced. */
  sandbox?: AgentSandboxConfiguration;
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
  chat?: ChatClientConfig;
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
  compaction?: CompactionRunPolicy;
  /** Structured output options */
  structuredOutput?: Record<string, unknown>;
  /** Client tools, context, state, and metadata available to this run */
  clientToolInput?: AgentClientInput;
  /** Live client app providers to bind for this run */
  clientAppProviders?: ClientAppProviderReference[];
}
