/**
 * Session & Thread types for HPD-Agent V3 architecture.
 *
 * Architecture:
 * - Session: Top-level container with metadata (shared across all threads)
 * - Thread: Conversation path with messages (multiple threads per session)
 */

import type {
  AgentEvent,
  AgentMessagePersistence,
  AgentMessageSource,
  AgentMessageVisibility,
} from './events.js';
import type { ThreadForkCompaction } from './run-config.js';

/** Resolved context used to create a subagent thread. */
export type SubAgentContextPolicy = 'Handoff' | 'Fresh' | 'Isolated';

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
  /** Optional stable agent identity that owns the default main thread */
  agentId?: string;

  /** Optional session ID (generated if not provided) */
  sessionId?: string;

  /** Optional initial metadata */
  metadata?: Record<string, unknown>;
}

/**
 * Request to update session metadata.
 */
export interface UpdateSessionRequest {
  /** Metadata to merge with existing metadata; null removes a key */
  metadata: Record<string, unknown | null>;
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

export type ThreadKind = 'MainAgent' | 'SubAgent';
export type ThreadVisibility = 'Visible' | 'Hidden';

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

  /** Agent identity selected when continuation does not choose another agent */
  defaultAgentId: string;

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
  // Runtime child metadata
  // ==========================================

  /** Runtime classification for this thread */
  kind: ThreadKind;

  /** Default list visibility for this thread */
  visibility: ThreadVisibility;

  /** Parent session for runtime child threads such as subagents */
  parentSessionId?: string;

  /** Parent thread for runtime child threads such as subagents */
  parentThreadId?: string;

  /** Subagent name when this is a subagent thread */
  subAgentName?: string;

  /** Caller-assigned name for this individual subagent task */
  subAgentTaskName?: string;

  /** Delegation invocation that created this subagent thread */
  invocationId?: string;

  /** Subagent definition source kind when this is a subagent thread */
  subAgentSourceKind?: string;

  /** Parent tool call id that created this runtime child thread */
  parentToolCallId?: string;

  /** Resolved context used to create this subagent thread */
  contextPolicy?: SubAgentContextPolicy;

  // ==========================================
  // Thread tree metadata
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
 * Session-level thread graph for branch navigation.
 */
export interface ThreadGraph {
  threads: Thread[];
  forkGroups: ThreadForkGroup[];
  runtimeChildren: ThreadRuntimeChild[];
}

/**
 * A set of branch choices that diverge from the same source thread at the same message.
 */
export interface ThreadForkGroup {
  id: string;
  sourceThreadId: string;
  forkedAtMessageId?: string;
  forkedAtMessageIndex?: number;
  choiceMessageIndex: number;
  members: ThreadForkGroupMember[];
}

/**
 * Lightweight member metadata for branch navigation UI.
 */
export interface ThreadForkGroupMember {
  threadId: string;
  name: string;
  index: number;
  isSource: boolean;
  choiceMessageId?: string;
  choiceMessageIndex?: number;
  messageCount: number;
  createdAt: string; // ISO 8601
  lastActivity: string; // ISO 8601
}

/**
 * Runtime child thread metadata, such as hidden subagent threads attached to a parent thread.
 */
export interface ThreadRuntimeChild {
  threadId: string;
  sessionId: string;
  /** Child agent identity selected by default for continuation */
  defaultAgentId: string;
  parentSessionId: string;
  parentThreadId: string;
  name: string;
  kind: ThreadKind;
  visibility: ThreadVisibility;
  subAgentName?: string;
  subAgentTaskName?: string;
  invocationId?: string;
  subAgentSourceKind?: string;
  parentToolCallId?: string;
  contextPolicy?: SubAgentContextPolicy;
  status?: string;
  messageCount: number;
  createdAt: string; // ISO 8601
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
 * Request to fork a thread at a message boundary.
 */
export interface ForkThreadRequest {
  /** Optional new thread ID (generated if not provided) */
  newThreadId?: string;

  /** Message id where fork occurs (copies messages through this message). Null forks from root before any messages. */
  fromMessageId?: string | null;

  /** Optional display name for the forked thread */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Optional thread-level metadata */
  metadata?: Record<string, unknown>;

  /** Explicit inherit/disabled/enabled fork-target compaction policy. */
  compaction?: ThreadForkCompaction | null;

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
  mediaType?: string;
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

  /** Message-level metadata/additional properties. */
  additionalProperties?: Record<string, unknown>;

  /** HPD-owned message source. This is separate from the provider-facing chat role. */
  source?: AgentMessageSource;

  /** HPD-owned transcript visibility. Hidden messages are model/runtime context, not transcript rows. */
  visibility?: AgentMessageVisibility;

  /** HPD-owned persistence policy for this message. */
  persistence?: AgentMessagePersistence;

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
  threadSequenceNumber?: number;
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
