/**
 * Agent definition types for the HPD-Agent runtime agent registry.
 */

// ============================================
// AGENT DEFINITIONS
// ============================================

/**
 * Lightweight summary of a stored agent — returned by listAgents().
 * Does not include the config body; use getAgent(id) for the full definition.
 */
export interface AgentSummaryDto {
  id: string;
  name: string;
  createdAt: string; // ISO 8601
  updatedAt: string; // ISO 8601
  metadata?: Record<string, unknown>;
}

/**
 * Full stored agent definition including config — returned by getAgent(), createAgent(), updateAgent().
 */
export interface StoredAgentDto {
  id: string;
  name: string;
  config: AgentConfig;
  createdAt: string; // ISO 8601
  updatedAt: string; // ISO 8601
  metadata?: Record<string, unknown>;
}

/**
 * Serializable agent configuration.
 * Matches the server-side AgentConfig shape (serializable fields only).
 */
export interface AgentConfig {
  name?: string;
  systemInstructions?: string;
  maxAgenticIterations?: number;
  provider?: ProviderConfig;
  toolkits?: ToolkitReference[];
  [key: string]: unknown;
}

export interface ProviderConfig {
  providerKey: string;
  modelName?: string;
  [key: string]: unknown;
}

export interface ToolkitReference {
  name: string;
  [key: string]: unknown;
}

// ============================================
// REQUEST TYPES
// ============================================

/** Request body for POST /agents */
export interface CreateAgentRequest {
  name: string;
  config: AgentConfig;
  metadata?: Record<string, unknown>;
}

/** Request body for PUT /agents/{agentId} */
export interface UpdateAgentRequest {
  config: AgentConfig;
}
