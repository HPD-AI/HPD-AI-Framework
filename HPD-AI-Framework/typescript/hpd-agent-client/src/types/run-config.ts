/**
 * Chat-level sampling parameters.
 * Maps to ChatRunConfigDto on the server.
 */
export interface ChatRunConfig {
  /** Sampling temperature — 0.0 to 1.0 */
  temperature?: number;
  maxOutputTokens?: number;
  topP?: number;
  frequencyPenalty?: number;
  presencePenalty?: number;
}

/**
 * Per-stream run configuration.
 * All fields are optional — only set fields are sent to the server.
 * Maps to StreamRunConfigDto on the server.
 */
export interface RunConfig {
  /** Provider key (e.g. "anthropic", "openai") */
  providerKey?: string;
  /** Model ID (e.g. "claude-sonnet-4-6") */
  modelId?: string;
  /** Additional system instructions appended to the agent's system prompt */
  additionalSystemInstructions?: string;
  /** Chat-level sampling parameters */
  chat?: ChatRunConfig;
  /** Per-tool permission overrides — key is tool name, value is allow/deny */
  permissionOverrides?: Record<string, boolean>;
  /** Whether to coalesce streamed text deltas before sending to the client */
  coalesceDeltas?: boolean;
  /** Skip tool execution for this run */
  skipTools?: boolean;
  /** Run timeout as ISO 8601 duration (e.g. "PT5M") */
  runTimeout?: string;
}
