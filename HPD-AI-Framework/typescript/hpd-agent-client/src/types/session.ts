/**
 * Session & Thread types for HPD-Agent V3 architecture.
 *
 * Architecture:
 * - Session: Top-level container with metadata (shared across all threads)
 * - Thread: Conversation path with messages (multiple threads per session)
 */

import type { AgentEvent } from './events.js';

// ============================================
// SESSION
// ============================================

/**
 * Session represents a chat conversation container.
 * Contains metadata and session-scoped state shared across all threads.
 */
export interface Session {
  /** Unique identifier for this session */
  id: string;

  /** When this session was created */
  createdAt: string; // ISO 8601

  /** Last time any thread in this session was updated */
  lastActivity: string; // ISO 8601

  /** Session-level metadata (not thread-specific) */
  metadata: Record<string, unknown>;
}

/**
 * Request to create a new session.
 */
export interface CreateSessionRequest {
  /** Optional session ID (generated if not provided) */
  sessionId?: string;

  /** Optional initial metadata */
  metadata?: Record<string, unknown>;
}

/**
 * Request to update session metadata.
 */
export interface UpdateSessionRequest {
  /** Metadata to merge with existing metadata */
  metadata: Record<string, unknown>;
}

/**
 * Options for listing sessions.
 */
export interface ListSessionsOptions {
  /** Maximum number of sessions to return */
  limit?: number;

  /** Skip this many sessions (for pagination) */
  offset?: number;

  /** Sort order ('createdAt' | 'lastActivity') */
  sortBy?: 'createdAt' | 'lastActivity';

  /** Sort direction ('asc' | 'desc') */
  sortDirection?: 'asc' | 'desc';
}

/**
 * Request to search sessions.
 */
export interface SearchSessionsRequest {
  /** Metadata values that must match on the session */
  metadata?: Record<string, unknown>;

  /** Skip this many sessions (for pagination) */
  offset?: number;

  /** Maximum number of sessions to return */
  limit?: number;
}

// ============================================
// THREAD
// ============================================

/**
 * Thread represents a conversation path within a session.
 * Contains messages and thread-specific state.
 */
export interface Thread {
  // ==========================================
  // Identity
  // ==========================================

  /** Unique identifier for this thread */
  id: string;

  /** Parent session ID */
  sessionId: string;

  /** Optional display name for this thread */
  name?: string;

  /** Optional user-friendly description */
  description?: string;

  // ==========================================
  // Fork ancestry
  // ==========================================

  /** Source thread ID if this was forked (null for original threads) */
  forkedFrom?: string;

  /** Message id where fork occurred (null for original threads) */
  forkedAtMessageId?: string;

  /** Resolved message index where fork occurred (diagnostic; null for original threads) */
  forkedAtMessageIndex?: number;

  /**
   * Full ancestry chain for multi-level fork tracking.
   * Key: depth (0 = root), Value: thread ID at that depth.
   * Example: { "0": "main", "1": "experimental", "2": "formal" }
   */
  ancestors?: Record<string, string>;

  // ==========================================
  // Timestamps & stats
  // ==========================================

  /** When this thread was created */
  createdAt: string; // ISO 8601

  /** Last time this thread was updated */
  lastActivity: string; // ISO 8601

  /** Number of messages in this thread */
  messageCount: number;

  /** Optional tags for categorizing threads */
  tags?: string[];

  /** Thread-level application metadata */
  metadata?: Record<string, unknown>;

  // ==========================================
  // Sibling metadata (V3 - for ordering)
  // ==========================================

  /**
   * Position among siblings at this fork point (0-based).
   * Siblings are threads that forked from the same parent at the same message id.
   * Stable ordering: original thread = 0, subsequent forks ordered chronologically.
   */
  siblingIndex: number;

  /**
   * Total number of sibling threads at this fork point (including this thread).
   * Updated atomically when siblings are added or removed.
   */
  totalSiblings: number;

  /**
   * True if this is the original thread (not forked from another).
   * Equivalent to: forkedFrom == null
   */
  isOriginal: boolean;

  /**
   * ID of the original thread in this sibling group.
   * For original threads: null
   * For forked threads: ID of the thread they forked from
   */
  originalThreadId?: string;

  // ==========================================
  // Navigation pointers (V3 - precomputed by backend)
  // ==========================================

  /**
   * ID of the previous sibling (sibling at index - 1).
   * Null if this is the first sibling (siblingIndex == 0).
   * Enables O(1) previous sibling navigation without scanning.
   */
  previousSiblingId?: string;

  /**
   * ID of the next sibling (sibling at index + 1).
   * Null if this is the last sibling (siblingIndex == totalSiblings - 1).
   * Enables O(1) next sibling navigation without scanning.
   */
  nextSiblingId?: string;

  // ==========================================
  // Fork tree metadata (V3)
  // ==========================================

  /**
   * IDs of threads that forked directly from this thread.
   * Updated when a thread forks from this one or a child is deleted.
   */
  childThreads: string[];

  /**
   * Count of direct child threads (forks from this thread).
   * Computed property: childThreads.length
   */
  totalForks: number;
}

/**
 * Lightweight sibling thread metadata for navigation UI.
 * Includes only fields needed for sibling selection and display.
 */
export interface SiblingThread {
  /** Unique identifier for this thread */
  id: string;

  /** Display name for this thread */
  name: string;

  /** Position among siblings (0-based) */
  siblingIndex: number;

  /** Total number of siblings at this fork point */
  totalSiblings: number;

  /** True if this is the original thread */
  isOriginal: boolean;

  /** Number of messages in this thread */
  messageCount: number;

  /** When this thread was created */
  createdAt: string; // ISO 8601

  /** Last time this thread was updated */
  lastActivity: string; // ISO 8601
}

/**
 * Request to create a new thread.
 */
export interface CreateThreadRequest {
  /** Optional thread ID (generated if not provided) */
  threadId?: string;

  /** Optional display name */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Optional thread-level metadata */
  metadata?: Record<string, unknown>;

  /** Agent definition ID used in the route for agent-scoped thread creation */
  agentId?: string;
}

/**
 * Request to update thread metadata.
 */
export interface UpdateThreadRequest {
  /** Optional display name */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Metadata fields to merge; null removes a key */
  metadata?: Record<string, unknown | null>;
}

/**
 * Request to fork a thread at a specific message id.
 */
export interface ForkThreadRequest {
  /** Optional new thread ID (generated if not provided) */
  newThreadId?: string;

  /** Message id where fork occurs (copies messages through this message) */
  fromMessageId: string;

  /** Optional display name for the forked thread */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Optional thread-level metadata */
  metadata?: Record<string, unknown>;

  /** Agent definition ID used in the route for agent-scoped thread forking */
  agentId?: string;
}

// ============================================
// AI CONTENT TYPES
// Mirror the M.E.AI $type polymorphic wire format.
// Prefixed with "Ai" to avoid collision with client-tools TextContent.
// ============================================

export interface AiTextContent {
  $type: 'text';
  text: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiTextReasoningContent {
  $type: 'reasoning';
  text: string;
  /** Encrypted blob from provider (Anthropic / OpenAI o-series). Must be round-tripped verbatim. */
  protectedData?: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiFunctionCallContent {
  $type: 'functionCall';
  callId: string;
  name: string;
  arguments?: Record<string, unknown>;
  informationalOnly?: boolean;
  additionalProperties?: Record<string, unknown>;
}

export interface AiFunctionResultContent {
  $type: 'functionResult';
  callId: string;
  result?: unknown;
  additionalProperties?: Record<string, unknown>;
}

export interface AiDataContent {
  $type: 'data';
  mediaType: string;
  uri?: string;
  data?: string; // base64
  additionalProperties?: Record<string, unknown>;
}

export interface AiErrorContent {
  $type: 'error';
  message: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiUriContent {
  $type: 'uri';
  uri: string;
  mimeType?: string;
  additionalProperties?: Record<string, unknown>;
}

// HPD custom content types (registered server-side via AddAIContentType)
export interface AiHpdImageContent {
  $type: 'hpd:image';
  mediaType: string;
  uri?: string;
  data?: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiHpdAudioContent {
  $type: 'hpd:audio';
  mediaType: string;
  uri?: string;
  data?: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiHpdVideoContent {
  $type: 'hpd:video';
  mediaType: string;
  uri?: string;
  data?: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiHpdDocumentContent {
  $type: 'hpd:document';
  mediaType: string;
  uri?: string;
  data?: string;
  additionalProperties?: Record<string, unknown>;
}

export interface AiUnknownContent {
  $type: string;
  [key: string]: unknown;
}

/**
 * Union of all possible AIContent types from thread message history.
 * Discriminated by the $type field matching the M.E.AI wire format.
 */
export type AIContent =
  | AiTextContent
  | AiTextReasoningContent
  | AiFunctionCallContent
  | AiFunctionResultContent
  | AiDataContent
  | AiErrorContent
  | AiUriContent
  | AiHpdImageContent
  | AiHpdAudioContent
  | AiHpdVideoContent
  | AiHpdDocumentContent
  | AiUnknownContent;

/**
 * Materialized thread transcript message returned by thread-history APIs.
 */
export interface ThreadMessage {
  /** Stable message id. */
  id: string;

  /** Chat role for this message. */
  role: 'system' | 'user' | 'assistant' | 'tool' | string;

  /** Full structured contents for this message. */
  contents: AIContent[];

  /** Message timestamp as ISO 8601. */
  timestamp: string;

  /** Optional author name. */
  authorName?: string;
}

// ============================================
// THREAD EVENT LOG
// ============================================

/**
 * Durable thread event envelope returned by GET /sessions/{sid}/threads/{bid}/events.
 * The type value intentionally matches the live runtime event name when the event
 * represents transcript activity; thread-only events use thread-specific names.
 */
export type ThreadEvent = AgentEvent & {
  eventId?: string;
  sessionId?: string;
  threadId?: string;
  sequenceNumber?: number;
  timestamp?: string;
  eventFlowId?: string;
};

/**
 * Reference to an uploaded content (returned by POST /sessions/{sid}/threads/{bid}/content).
 * Passed as attachments in SendOptions; the workspace converts these to
 * UriContent references in the outgoing message.
 */
export interface ContentReference {
  contentId: string;
  version: string;
  contentType: string;
  name?: string;
  sizeBytes?: number;
}
