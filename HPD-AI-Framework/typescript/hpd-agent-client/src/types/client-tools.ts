/**
 * Client Tools Protocol Types
 *
 * Types for bidirectional tool orchestration between the agent and client applications.
 * Enables clients to register tools that execute in the browser/client context.
 */

// ============================================
// Tool Definition
// ============================================

/**
 * Definition of a tool that executes on the client.
 */
export interface ClientToolDefinition {
  /** Unique name of the tool */
  name: string;

  /** Description shown to the LLM */
  description: string;

  /** JSON Schema for the tool's parameters */
  parametersSchema: Record<string, unknown>;

  /** Whether this tool requires user permission before execution */
  requiresPermission?: boolean;
}

// ============================================
// Tool Group Definition (Container for Tools)
// ============================================

/**
 * A tool group is a container for related tools and skills.
 * All client tools must be registered inside a tool group.
 */
export interface clientToolKitDefinition {
  /** Unique name of the tool group */
  name: string;

  /**
   * Description of the tool group.
   * REQUIRED if startCollapsed is true (tells LLM when to expand).
   */
  description?: string;

  /** Tools contained in this tool group */
  tools: ClientToolDefinition[];

  /** Optional skills (entry points with instructions) */
  skills?: ClientSkillDefinition[];

  /**
   * Ephemeral instructions returned in function result when container is expanded (one-time).
   * Use for initial guidance that doesn't need to persist.
   */
  functionResult?: string;

  /**
   * Persistent instructions injected into system prompt after expansion (every iteration).
   * Use for workflow guidance, best practices, etc.
   */
  systemPrompt?: string;

  /**
   * Start with tool group collapsed (tools hidden behind container).
   * If true, description is required.
   */
  startCollapsed?: boolean;
}

// ============================================
// Skill Definition
// ============================================

/**
 * A skill is an entry point with instructions that references tools.
 * When invoked, it provides context to the agent about how to use the tools.
 */
export interface ClientSkillDefinition {
  /** Unique name of the skill */
  name: string;

  /** Description shown to the LLM */
  description: string;

  /**
   * Ephemeral instructions returned in function result when skill is activated (one-time).
   * Use for initial guidance that doesn't need to persist across iterations.
   */
  functionResult?: string;

  /**
   * Persistent instructions injected into system prompt after activation (every iteration).
   * Use for workflow guidance, best practices, etc.
   */
  systemPrompt?: string;

  /** Tools this skill references */
  references?: ClientSkillReference[];

  /** Documents available for this skill */
  documents?: ClientSkillDocument[];
}

/**
 * Reference to a tool from a skill.
 */
export interface ClientSkillReference {
  /** Name of the tool */
  toolName: string;

  /** Tool group containing the tool (optional, defaults to same tool group) */
  ToolKitName?: string;
}

/**
 * Document available for a skill.
 */
export interface ClientSkillDocument {
  /** Unique identifier for the document */
  documentId: string;

  /** Description of the document content */
  description: string;

  /** Inline content (for small documents) */
  content?: string;

  /** URL to fetch content (for large documents) */
  url?: string;
}

// ============================================
// Context Items
// ============================================

/**
 * Context item passed from client to agent.
 * Used to provide application state, user preferences, etc.
 */
export interface ContextItem {
  /** Description of this context (shown to LLM) */
  description: string;

  /** The context value (any JSON-serializable value) */
  value: unknown;

  /** Optional key for referencing this context */
  key?: string;
}

// ============================================
// Tool Result Content Types
// ============================================

/**
 * Text content in a tool result.
 */
export interface TextContent {
  type: 'text';
  text: string;
}

/**
 * Binary content in a tool result (images, files).
 */
export interface BinaryContent {
  type: 'binary';

  /** MIME type of the content */
  mimeType: string;

  /** Base64-encoded data (for inline content) */
  data?: string;

  /** URL to fetch content (for large files) */
  url?: string;

  /** Identifier for referencing this content */
  id?: string;

  /** Original filename */
  filename?: string;
}

/**
 * Structured JSON content in a tool result.
 */
export interface JsonContent {
  type: 'json';

  /** The JSON value */
  value: unknown;
}

/**
 * Union of all tool result content types.
 */
export type ToolResultContent = TextContent | BinaryContent | JsonContent;

// ============================================
// Augmentation (State Changes After Tool Execution)
// ============================================

/**
 * State changes to apply after a client tool executes.
 * Enables dynamic tool injection, visibility changes, etc.
 */
export interface ClientToolAugmentation {
  /** New tool groups to inject */
  injectToolKits?: clientToolKitDefinition[];

  /** Tool groups to remove */
  removeToolKits?: string[];

  /** Tool groups to expand (show their tools) */
  expandToolKits?: string[];

  /** Tool groups to collapse (hide their tools) */
  collapseToolKits?: string[];

  /** Tools to hide */
  hideTools?: string[];

  /** Tools to show */
  showTools?: string[];

  /** Context items to add */
  addContext?: ContextItem[];

  /** Context keys to remove */
  removeContext?: string[];

  /** Full state replacement */
  updateState?: unknown;

  /** Partial state patch (merged with existing) */
  patchState?: unknown;
}

// ============================================
// Client Tool Events
// ============================================

/**
 * Request from agent to invoke a client tool.
 */
export interface ClientToolInvokeRequest {
  /** Unique request ID (for correlating response) */
  requestId: string;

  /** Name of the tool to invoke */
  toolName: string;

  /** Function call ID from the LLM */
  callId: string;

  /** Arguments to pass to the tool */
  arguments: Record<string, unknown>;

  /** Tool description (for debugging) */
  description?: string;
}

/**
 * Response from client after executing a tool.
 */
export interface ClientToolInvokeResponse {
  /** Must match requestId from the request */
  requestId: string;

  /** Tool result content */
  content: ToolResultContent[];

  /** Whether execution succeeded */
  success: boolean;

  /** Error message if success is false */
  errorMessage?: string;

  /** State changes to apply before next iteration */
  augmentation?: ClientToolAugmentation;
}

// ============================================
// Stream Options Extension
// ============================================

/**
 * Client-specific options for streaming.
 */
export interface ClientStreamOptions {
  /** Tool groups to register for this stream */
  clientToolKits?: clientToolKitDefinition[];

  /** Context items to pass to the agent */
  context?: ContextItem[];

  /** Application state (opaque to agent) */
  state?: unknown;

  /** Tool groups to start expanded */
  expandedContainers?: string[];

  /** Tools to start hidden */
  hiddenTools?: string[];

  /** Reset client state (clear all registered tool groups) */
  resetClientState?: boolean;
}

// ============================================
// Helper Functions
// ============================================

/**
 * Creates a collapsed tool group definition.
 * Collapsed tool groups hide their tools behind an expandable container.
 */
export function createCollapsedToolKit(
  name: string,
  description: string,
  tools: ClientToolDefinition[],
  options?: {
    skills?: ClientSkillDefinition[];
    /** Ephemeral instructions returned when container is expanded (one-time) */
    functionResult?: string;
    /** Persistent instructions injected into system prompt after expansion (every iteration) */
    systemPrompt?: string;
  }
): clientToolKitDefinition {
  return {
    name,
    description,
    tools,
    skills: options?.skills,
    functionResult: options?.functionResult,
    systemPrompt: options?.systemPrompt,
    startCollapsed: true,
  };
}

/**
 * Creates an expanded tool group definition.
 * Expanded tool groups show all their tools immediately.
 */
export function createExpandedToolKit(
  name: string,
  tools: ClientToolDefinition[],
  options?: {
    description?: string;
    skills?: ClientSkillDefinition[];
    /** Ephemeral instructions returned when container is expanded (one-time) */
    functionResult?: string;
    /** Persistent instructions injected into system prompt after expansion (every iteration) */
    systemPrompt?: string;
  }
): clientToolKitDefinition {
  return {
    name,
    description: options?.description,
    tools,
    skills: options?.skills,
    functionResult: options?.functionResult,
    systemPrompt: options?.systemPrompt,
    startCollapsed: false,
  };
}

/**
 * Creates a simple text result for a client tool response.
 */
export function createTextResult(text: string): ToolResultContent[] {
  return [{ type: 'text', text }];
}

/**
 * Creates a JSON result for a client tool response.
 */
export function createJsonResult(value: unknown): ToolResultContent[] {
  return [{ type: 'json', value }];
}

/**
 * Creates a successful tool response.
 */
export function createSuccessResponse(
  requestId: string,
  content: ToolResultContent[] | string,
  augmentation?: ClientToolAugmentation
): ClientToolInvokeResponse {
  return {
    requestId,
    content: typeof content === 'string' ? createTextResult(content) : content,
    success: true,
    augmentation,
  };
}

/**
 * Creates a failed tool response.
 */
export function createErrorResponse(
  requestId: string,
  errorMessage: string
): ClientToolInvokeResponse {
  return {
    requestId,
    content: [],
    success: false,
    errorMessage,
  };
}
