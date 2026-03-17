/**
 * Session & Branch types for HPD-Agent V3 architecture.
 *
 * Architecture:
 * - Session: Top-level container with metadata (shared across all branches)
 * - Branch: Conversation path with messages (multiple branches per session)
 */

// ============================================
// SESSION
// ============================================

/**
 * Session represents a chat conversation container.
 * Contains metadata and session-scoped state shared across all branches.
 */
export interface Session {
  /** Unique identifier for this session */
  id: string;

  /** When this session was created */
  createdAt: string; // ISO 8601

  /** Last time any branch in this session was updated */
  lastActivity: string; // ISO 8601

  /** Session-level metadata (not branch-specific) */
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

// ============================================
// BRANCH
// ============================================

/**
 * Branch represents a conversation path within a session.
 * Contains messages and branch-specific state.
 */
export interface Branch {
  // ==========================================
  // Identity
  // ==========================================

  /** Unique identifier for this branch */
  id: string;

  /** Parent session ID */
  sessionId: string;

  /** Optional display name for this branch */
  name?: string;

  /** Optional user-friendly description */
  description?: string;

  // ==========================================
  // Fork ancestry
  // ==========================================

  /** Source branch ID if this was forked (null for original branches) */
  forkedFrom?: string;

  /** Message index where fork occurred (null for original branches) */
  forkedAtMessageIndex?: number;

  /**
   * Full ancestry chain for multi-level fork tracking.
   * Key: depth (0 = root), Value: branch ID at that depth.
   * Example: { "0": "main", "1": "experimental", "2": "formal" }
   */
  ancestors?: Record<string, string>;

  // ==========================================
  // Timestamps & stats
  // ==========================================

  /** When this branch was created */
  createdAt: string; // ISO 8601

  /** Last time this branch was updated */
  lastActivity: string; // ISO 8601

  /** Number of messages in this branch */
  messageCount: number;

  /** Optional tags for categorizing branches */
  tags?: string[];

  // ==========================================
  // Sibling metadata (V3 - for ordering)
  // ==========================================

  /**
   * Position among siblings at this fork point (0-based).
   * Siblings are branches that forked from the same parent at the same message index.
   * Stable ordering: original branch = 0, subsequent forks ordered chronologically.
   */
  siblingIndex: number;

  /**
   * Total number of sibling branches at this fork point (including this branch).
   * Updated atomically when siblings are added or removed.
   */
  totalSiblings: number;

  /**
   * True if this is the original branch (not forked from another).
   * Equivalent to: forkedFrom == null
   */
  isOriginal: boolean;

  /**
   * ID of the original branch in this sibling group.
   * For original branches: null
   * For forked branches: ID of the branch they forked from
   */
  originalBranchId?: string;

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
   * IDs of branches that forked directly from this branch.
   * Updated when a branch forks from this one or a child is deleted.
   */
  childBranches: string[];

  /**
   * Count of direct child branches (forks from this branch).
   * Computed property: childBranches.length
   */
  totalForks: number;
}

/**
 * Lightweight sibling branch metadata for navigation UI.
 * Includes only fields needed for sibling selection and display.
 */
export interface SiblingBranch {
  /** Unique identifier for this branch */
  id: string;

  /** Display name for this branch */
  name: string;

  /** Position among siblings (0-based) */
  siblingIndex: number;

  /** Total number of siblings at this fork point */
  totalSiblings: number;

  /** True if this is the original branch */
  isOriginal: boolean;

  /** Number of messages in this branch */
  messageCount: number;

  /** When this branch was created */
  createdAt: string; // ISO 8601

  /** Last time this branch was updated */
  lastActivity: string; // ISO 8601
}

/**
 * Request to create a new branch.
 */
export interface CreateBranchRequest {
  /** Optional branch ID (generated if not provided) */
  branchId?: string;

  /** Optional display name */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Agent definition ID to associate with this branch (defaults to "default") */
  agentId?: string;
}

/**
 * Request to fork a branch at a specific message index.
 */
export interface ForkBranchRequest {
  /** Optional new branch ID (generated if not provided) */
  newBranchId?: string;

  /** Message index where fork occurs (0-based, copies messages 0..index) */
  fromMessageIndex: number;

  /** Optional display name for the forked branch */
  name?: string;

  /** Optional description */
  description?: string;

  /** Optional tags */
  tags?: string[];

  /** Agent definition ID to associate with the forked branch (defaults to "default") */
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
 * Union of all possible AIContent types from branch message history.
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

// ============================================
// BRANCH MESSAGE
// ============================================

/**
 * Full-fidelity branch message carrying the complete AIContent list.
 * Mirrors the server-side ChatMessage / MessageDto structure.
 */
export interface BranchMessage {
  /** Stable message ID (GUID) */
  id: string;

  /** Message role ('user' | 'assistant' | 'system' | 'tool') */
  role: string;

  /**
   * All content items in this message.
   * Use the $type discriminator to handle each content type.
   * UsageContent is excluded server-side (billing metadata, not conversation content).
   */
  contents: AIContent[];

  /** Optional author name (multi-agent scenarios) */
  authorName?: string;

  /** Timestamp (ISO 8601) */
  timestamp: string;
}

/**
 * Reference to an uploaded asset (returned by POST /sessions/{sid}/assets).
 * Passed as attachments in SendOptions; the workspace converts these to
 * UriContent references in the outgoing message.
 */
export interface AssetReference {
  assetId: string;
  contentType: string;
  name?: string;
  sizeBytes?: number;
}
