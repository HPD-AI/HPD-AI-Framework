import type { BackgroundTaskNotificationRule } from './events.js';

/**
 * Client Tools Protocol Types
 *
 * Types for request-session tool orchestration between the agent and client applications.
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

  /** How this tool may be invoked by the model. Defaults to synchronous-only. */
  invocationModePolicy?: AgentInvocationModePolicy;

  /** Notification rule used when this client tool accepts background work. */
  backgroundNotification?: BackgroundTaskNotificationRule;
}

export type AgentInvocationModePolicy =
  | 'SynchronousOnly'
  | 'BackgroundOnly'
  | 'ModelChoice'
  | string;

export type BackgroundHandleKind =
  | 'Process'
  | 'Workflow'
  | 'Agent'
  | 'McpOperation'
  | 'ClientToolOperation'
  | 'BrowserSession'
  | 'FileWatcher'
  | 'Export'
  | 'IndexingJob'
  | 'Runtime'
  | 'Other'
  | string;

export type BackgroundHandleOperation =
  | 'None'
  | 'Status'
  | 'Read'
  | 'Stop'
  | 'Cancel'
  | 'Artifacts'
  | 'Events'
  | string;

// ============================================
// Tool Group Definition (Container for Tools)
// ============================================

/**
 * A tool group is a container for related tools and skills.
 * All client tools must be registered inside a tool group.
 */
export interface ClientToolHarnessDefinition {
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

/**
 * Client tool surface provided for a single agent run.
 */
export interface AgentClientInput {
  /** Tool groups available to this run */
  clientToolHarnesses?: ClientToolHarnessDefinition[];

  /** Tool groups that should start expanded */
  expandedContainers?: string[];

  /** Tools that should be hidden for this run */
  hiddenTools?: string[];

  /** Context items to expose to the agent */
  context?: ContextItem[];

  /** Client-owned state for the run */
  state?: unknown;

  /** Execution context visible to client-side tools and UI state. */
  executionContext?: AgentExecutionContext;

  /** Host metadata for diagnostics or routing */
  metadata?: unknown;

  /** Reset backend client-tool state before applying this input */
  resetClientState?: boolean;
}

/**
 * Client-visible execution context associated with a message or tool call.
 */
export interface AgentExecutionContext {
  sessionId?: string;
  threadId?: string;
  agentId?: string;
  threadExecutionId?: string;
  messageId?: string;
  metadata?: Record<string, unknown>;
  state?: unknown;
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
  ToolHarnessName?: string;
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
  injectToolHarnesses?: ClientToolHarnessDefinition[];

  /** Tool groups to remove */
  removeToolHarnesses?: string[];

  /** Tool groups to expand (show their tools) */
  expandToolHarnesses?: string[];

  /** Tool groups to collapse (hide their tools) */
  collapseToolHarnesses?: string[];

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
 * Immediate outcome from the client after accepting or executing a tool request.
 */
export interface ClientToolInvokeOutcome {
  /** Must match requestId from the request */
  requestId: string;

  /** Immediate outcome kind. */
  outcome: ClientToolInvokeOutcomeKind;

  /** Tool result content for completed outcomes, or launch content for background outcomes. */
  content?: ToolResultContent[];

  /** Error message for rejected or failed outcomes. */
  errorMessage?: string;

  /** Client-owned operation id for accepted background work. */
  clientOperationId?: string;

  /** Optional handle kind when the accepted background operation is controllable. */
  handleKind?: BackgroundHandleKind;

  /** Operations supported by the background handle. */
  supportedOperations?: BackgroundHandleOperation[];

  /** State changes to apply before next iteration */
  augmentation?: ClientToolAugmentation;
}

export type ClientToolInvokeOutcomeKind =
  | 'Completed'
  | 'AcceptedBackground'
  | 'Rejected'
  | 'Failed';

// ============================================
// Helper Functions
// ============================================

/**
 * Creates a collapsed tool group definition.
 * Collapsed tool groups hide their tools behind an expandable container.
 */
export function createCollapsedToolHarness(
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
): ClientToolHarnessDefinition {
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
export function createExpandedToolHarness(
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
): ClientToolHarnessDefinition {
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
 * Creates a completed tool outcome.
 */
export function completeClientTool(
  requestId: string,
  content: ToolResultContent[] | string,
  augmentation?: ClientToolAugmentation
): ClientToolInvokeOutcome {
  return {
    requestId,
    outcome: 'Completed',
    content: typeof content === 'string' ? createTextResult(content) : content,
    augmentation,
  };
}

/**
 * Creates a completed text tool outcome.
 */
export function completeClientToolWithText(
  requestId: string,
  text: string,
  augmentation?: ClientToolAugmentation
): ClientToolInvokeOutcome {
  return completeClientTool(requestId, createTextResult(text), augmentation);
}

/**
 * Creates a completed JSON tool outcome.
 */
export function completeClientToolWithJson(
  requestId: string,
  value: unknown,
  augmentation?: ClientToolAugmentation
): ClientToolInvokeOutcome {
  return completeClientTool(requestId, createJsonResult(value), augmentation);
}

/**
 * Creates a failed tool outcome.
 */
export function failClientTool(
  requestId: string,
  errorMessage: string
): ClientToolInvokeOutcome {
  return {
    requestId,
    outcome: 'Failed',
    errorMessage,
  };
}

/**
 * Creates a rejected tool outcome.
 */
export function rejectClientTool(
  requestId: string,
  errorMessage: string
): ClientToolInvokeOutcome {
  return {
    requestId,
    outcome: 'Rejected',
    errorMessage,
  };
}

/**
 * Creates an accepted background tool outcome.
 */
export function acceptClientToolBackground(
  requestId: string,
  clientOperationId: string,
  options: {
    content?: ToolResultContent[] | string;
    handleKind?: BackgroundHandleKind;
    supportedOperations?: BackgroundHandleOperation[];
    augmentation?: ClientToolAugmentation;
  } = {},
): ClientToolInvokeOutcome {
  return {
    requestId,
    outcome: 'AcceptedBackground',
    clientOperationId,
    content: typeof options.content === 'string' ? createTextResult(options.content) : options.content,
    handleKind: options.handleKind,
    supportedOperations: options.supportedOperations,
    augmentation: options.augmentation,
  };
}

export type ClientToolBackgroundOperationState =
  | 'Completed'
  | 'Faulted'
  | 'Cancelled';

export interface ClientToolBackgroundOperationOutcomeInput {
  type: 'CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME';
  clientOperationId: string;
  state: ClientToolBackgroundOperationState;
  content?: ToolResultContent[];
  augmentation?: ClientToolAugmentation;
  errorMessage?: string | null;
  errorType?: string | null;
  cancellationReason?: string | null;
  metadata?: Record<string, string> | null;
}

/**
 * Creates a terminal background operation outcome for accepted client-tool work.
 */
export function finishClientToolBackgroundOperation(
  clientOperationId: string,
  state: ClientToolBackgroundOperationState,
  options: {
    content?: ToolResultContent[] | string;
    augmentation?: ClientToolAugmentation;
    errorMessage?: string | null;
    errorType?: string | null;
    cancellationReason?: string | null;
    metadata?: Record<string, string> | null;
  } = {},
): ClientToolBackgroundOperationOutcomeInput {
  return {
    type: 'CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME',
    clientOperationId,
    state,
    content: typeof options.content === 'string' ? createTextResult(options.content) : options.content,
    augmentation: options.augmentation,
    errorMessage: options.errorMessage,
    errorType: options.errorType,
    cancellationReason: options.cancellationReason,
    metadata: options.metadata,
  };
}

/**
 * Creates a completed background operation outcome.
 */
export function completeClientToolBackgroundOperation(
  clientOperationId: string,
  content?: ToolResultContent[] | string,
  options: {
    augmentation?: ClientToolAugmentation;
    metadata?: Record<string, string> | null;
  } = {},
): ClientToolBackgroundOperationOutcomeInput {
  return finishClientToolBackgroundOperation(clientOperationId, 'Completed', {
    content,
    augmentation: options.augmentation,
    metadata: options.metadata,
  });
}

/**
 * Creates a faulted background operation outcome.
 */
export function failClientToolBackgroundOperation(
  clientOperationId: string,
  errorMessage: string,
  options: {
    errorType?: string | null;
    metadata?: Record<string, string> | null;
  } = {},
): ClientToolBackgroundOperationOutcomeInput {
  return finishClientToolBackgroundOperation(clientOperationId, 'Faulted', {
    errorMessage,
    errorType: options.errorType,
    metadata: options.metadata,
  });
}

/**
 * Creates a cancelled background operation outcome.
 */
export function cancelClientToolBackgroundOperation(
  clientOperationId: string,
  cancellationReason?: string | null,
  metadata?: Record<string, string> | null,
): ClientToolBackgroundOperationOutcomeInput {
  return finishClientToolBackgroundOperation(clientOperationId, 'Cancelled', {
    cancellationReason,
    metadata,
  });
}
