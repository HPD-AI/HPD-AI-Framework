import type { RunConfig } from './run-config.js';

export interface ContextUsageRequest {
  runConfig?: RunConfig | null;
}

export interface ThreadContextUsage {
  sessionId: string;
  threadId: string;
  providerKey?: string | null;
  modelId?: string | null;
  contextWindow?: number | null;
  lastObservedInputTokens?: number | null;
  estimatedInputTokens?: number | null;
  effectiveInputTokens?: number | null;
  usageRatio?: number | null;
  isEstimate: boolean;
  source?: string | null;
}
