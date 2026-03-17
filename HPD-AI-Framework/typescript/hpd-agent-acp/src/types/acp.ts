// ACP (Agent Client Protocol) wire types
// JSON-RPC 2.0 — all messages are newline-delimited on stdio

export type RequestId = string | number | null;

// ── JSON-RPC base frames ────────────────────────────────────────────────────

export interface JsonRpcRequest<TMethod extends string, TParams> {
  jsonrpc: '2.0';
  id: RequestId;
  method: TMethod;
  params: TParams;
}

export interface JsonRpcResponse<TResult> {
  jsonrpc: '2.0';
  id: RequestId;
  result: TResult;
}

export interface JsonRpcError {
  jsonrpc: '2.0';
  id: RequestId;
  error: {
    code: number;
    message: string;
    data?: unknown;
  };
}

export interface JsonRpcNotification<TMethod extends string, TParams> {
  jsonrpc: '2.0';
  method: TMethod;
  params: TParams;
}

export type AnyJsonRpcMessage =
  | JsonRpcRequest<string, unknown>
  | JsonRpcResponse<unknown>
  | JsonRpcError
  | JsonRpcNotification<string, unknown>;

// ── Shared content types ────────────────────────────────────────────────────

export interface AcpTextContent {
  type: 'text';
  text: string;
  annotations?: Record<string, unknown>;
  _meta?: Record<string, unknown>;
}

export interface AcpImageContent {
  type: 'image';
  data: string; // base64
  mimeType: string;
  annotations?: Record<string, unknown>;
  _meta?: Record<string, unknown>;
}

export interface AcpResourceContent {
  type: 'resource';
  resource: { uri: string; mimeType?: string; text: string };
  annotations?: Record<string, unknown>;
  _meta?: Record<string, unknown>;
}

export type AcpContentBlock = AcpTextContent | AcpImageContent | AcpResourceContent;

// ── Capability types ────────────────────────────────────────────────────────

export interface AcpClientCapabilities {
  fs?: {
    readTextFile?: boolean;
    writeTextFile?: boolean;
  };
  terminal?: boolean;
  _meta?: Record<string, unknown>;
}

export interface AcpAgentCapabilities {
  loadSession?: boolean;
  promptCapabilities?: {
    image?: boolean;
    audio?: boolean;
    embeddedContext?: boolean;
  };
  mcpCapabilities?: {
    http?: boolean;
    sse?: boolean;
  };
  sessionCapabilities?: Record<string, unknown>;
  _meta?: Record<string, unknown>;
}

// ── initialize ──────────────────────────────────────────────────────────────

export type InitializeRequest = JsonRpcRequest<'initialize', {
  protocolVersion: number;
  clientCapabilities?: AcpClientCapabilities;
  clientInfo?: { name: string; title?: string; version?: string };
  _meta?: Record<string, unknown>;
}>;

export interface InitializeResult {
  protocolVersion: number;
  agentCapabilities: AcpAgentCapabilities;
  authMethods?: Array<{ id: string; name: string; description?: string }>;
  agentInfo?: { name: string; title?: string; version?: string };
  _meta?: Record<string, unknown>;
}

// ── authenticate ────────────────────────────────────────────────────────────

export type AuthenticateRequest = JsonRpcRequest<'authenticate', {
  methodId: string;
  _meta?: Record<string, unknown>;
}>;

// ── MCP server config ───────────────────────────────────────────────────────

export interface AcpMcpServerStdio {
  name: string;
  command: string;
  args?: string[];
  env?: Array<{ name: string; value: string }>;
}

export interface AcpMcpServerHttp {
  type: 'http';
  name: string;
  url: string;
  headers?: Array<{ name: string; value: string }>;
}

export type AcpMcpServer = AcpMcpServerStdio | AcpMcpServerHttp;

// ── session/new ─────────────────────────────────────────────────────────────

export type SessionNewRequest = JsonRpcRequest<'session/new', {
  cwd: string;
  mcpServers?: AcpMcpServer[];
  _meta?: Record<string, unknown>;
}>;

export interface SessionNewResult {
  sessionId: string;
  _meta?: Record<string, unknown>;
}

// ── session/load ────────────────────────────────────────────────────────────

export type SessionLoadRequest = JsonRpcRequest<'session/load', {
  sessionId: string;
  cwd: string;
  mcpServers?: AcpMcpServer[];
  _meta?: Record<string, unknown>;
}>;

// ── session/prompt ──────────────────────────────────────────────────────────

export type SessionPromptRequest = JsonRpcRequest<'session/prompt', {
  sessionId: string;
  prompt: AcpContentBlock[];
  _meta?: Record<string, unknown>;
}>;

export type StopReason = 'end_turn' | 'max_tokens' | 'max_turn_requests' | 'refusal' | 'cancelled';

export interface SessionPromptResult {
  stopReason: StopReason;
  _meta?: Record<string, unknown>;
}

// ── session/set_mode ────────────────────────────────────────────────────────

export type SessionSetModeRequest = JsonRpcRequest<'session/set_mode', {
  sessionId: string;
  modeId: string;
  _meta?: Record<string, unknown>;
}>;

// ── session/cancel (notification, client → agent) ──────────────────────────

export type SessionCancelNotification = JsonRpcNotification<'session/cancel', {
  sessionId: string;
  _meta?: Record<string, unknown>;
}>;

// ── session/update (notification, agent → client) ──────────────────────────

export type ToolCallKind = 'read' | 'edit' | 'delete' | 'move' | 'search' | 'execute' | 'think' | 'fetch' | 'switch_mode' | 'other';
export type ToolCallStatus = 'pending' | 'in_progress' | 'completed' | 'failed';

export interface AcpToolCallContentItem {
  type: 'content';
  content: AcpContentBlock;
  _meta?: Record<string, unknown>;
}

export interface AcpToolCallDiffItem {
  type: 'diff';
  path: string;
  oldText: string | null;
  newText: string;
  _meta?: Record<string, unknown>;
}

export interface AcpToolCallTerminalItem {
  type: 'terminal';
  terminalId: string;
  _meta?: Record<string, unknown>;
}

export type AcpToolCallContentEntry = AcpToolCallContentItem | AcpToolCallDiffItem | AcpToolCallTerminalItem;

export interface AcpToolCall {
  toolCallId: string;
  title?: string;
  kind?: ToolCallKind;
  status?: ToolCallStatus;
  content?: AcpToolCallContentEntry[];
  locations?: Array<{ path: string; line?: number }>;
  rawInput?: Record<string, unknown>;
  rawOutput?: Record<string, unknown>;
  _meta?: Record<string, unknown>;
}

export type SessionUpdate =
  | { sessionUpdate: 'agent_message_chunk'; content: AcpContentBlock; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'user_message_chunk'; content: AcpContentBlock; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'agent_thought_chunk'; content: AcpContentBlock; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'tool_call'; } & AcpToolCall
  | { sessionUpdate: 'tool_call_update'; toolCallId: string; title?: string; kind?: ToolCallKind; status?: ToolCallStatus; content?: AcpToolCallContentEntry[]; locations?: Array<{ path: string; line?: number }>; rawInput?: Record<string, unknown>; rawOutput?: Record<string, unknown>; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'plan'; entries: Array<{ content: string; priority?: 'high' | 'medium' | 'low'; status?: 'pending' | 'in_progress' | 'completed'; _meta?: Record<string, unknown> }>; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'available_commands_update'; availableCommands: Array<{ name: string; description?: string; input: { type: 'unstructured' } }>; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'current_mode_update'; currentModeId: string; _meta?: Record<string, unknown> }
  | { sessionUpdate: 'config_option_update'; configOptions: Array<{ optionId: string; title: string; description?: string; type: 'boolean' | 'number' | 'string'; value: unknown; disabled?: boolean }>; _meta?: Record<string, unknown> };

export type SessionUpdateNotification = JsonRpcNotification<'session/update', {
  sessionId: string;
  update: SessionUpdate;
  _meta?: Record<string, unknown>;
}>;

// ── session/request_permission (agent → client, request) ───────────────────

export type PermissionOptionKind = 'allow_once' | 'allow_always' | 'reject_once' | 'reject_always';

export interface AcpPermissionOption {
  optionId: string;
  name: string;
  kind: PermissionOptionKind;
}

export type SessionRequestPermissionRequest = JsonRpcRequest<'session/request_permission', {
  sessionId: string;
  toolCall: AcpToolCall;
  options?: AcpPermissionOption[];
  _meta?: Record<string, unknown>;
}>;

export type PermissionOutcome =
  | { outcome: 'selected'; optionId: string }
  | { outcome: 'cancelled' };

export interface SessionRequestPermissionResult {
  outcome: PermissionOutcome;
  _meta?: Record<string, unknown>;
}

// ── fs/read_text_file (agent → client, request) ────────────────────────────

export type FsReadTextFileRequest = JsonRpcRequest<'fs/read_text_file', {
  sessionId: string;
  path: string;
  line?: number;
  limit?: number;
  _meta?: Record<string, unknown>;
}>;

export interface FsReadTextFileResult {
  content: string;
  _meta?: Record<string, unknown>;
}

// ── fs/write_text_file (agent → client, request) ───────────────────────────

export type FsWriteTextFileRequest = JsonRpcRequest<'fs/write_text_file', {
  sessionId: string;
  path: string;
  content: string;
  _meta?: Record<string, unknown>;
}>;

// ── terminal/create (agent → client, request) ──────────────────────────────

export type TerminalCreateRequest = JsonRpcRequest<'terminal/create', {
  sessionId: string;
  command: string;
  args?: string[];
  env?: Array<{ name: string; value: string }>;
  cwd?: string;
  outputByteLimit?: number;
  _meta?: Record<string, unknown>;
}>;

export interface TerminalCreateResult {
  terminalId: string;
  _meta?: Record<string, unknown>;
}

// ── terminal/output (agent → client, request) ──────────────────────────────

export type TerminalOutputRequest = JsonRpcRequest<'terminal/output', {
  sessionId: string;
  terminalId: string;
  _meta?: Record<string, unknown>;
}>;

export interface TerminalOutputResult {
  output: string;
  truncated: boolean;
  exitStatus?: { exitCode: number; signal?: string };
  _meta?: Record<string, unknown>;
}

// ── terminal/wait_for_exit (agent → client, request) ───────────────────────

export type TerminalWaitForExitRequest = JsonRpcRequest<'terminal/wait_for_exit', {
  sessionId: string;
  terminalId: string;
  _meta?: Record<string, unknown>;
}>;

export interface TerminalWaitForExitResult {
  exitCode: number;
  signal?: string;
  _meta?: Record<string, unknown>;
}

// ── terminal/kill (agent → client, request) ────────────────────────────────

export type TerminalKillRequest = JsonRpcRequest<'terminal/kill', {
  sessionId: string;
  terminalId: string;
  _meta?: Record<string, unknown>;
}>;

// ── terminal/release (agent → client, request) ─────────────────────────────

export type TerminalReleaseRequest = JsonRpcRequest<'terminal/release', {
  sessionId: string;
  terminalId: string;
  _meta?: Record<string, unknown>;
}>;

// ── Inbound message union (editor → bridge) ─────────────────────────────────

export type InboundMessage =
  | InitializeRequest
  | AuthenticateRequest
  | SessionNewRequest
  | SessionLoadRequest
  | SessionPromptRequest
  | SessionSetModeRequest
  | SessionCancelNotification
  | JsonRpcResponse<unknown>     // responses to agent→client requests
  | JsonRpcError;                // error responses to agent→client requests

// ── JSON-RPC error codes ────────────────────────────────────────────────────

export const JsonRpcErrorCode = {
  ParseError: -32700,
  InvalidRequest: -32600,
  MethodNotFound: -32601,
  InvalidParams: -32602,
  InternalError: -32603,
  RequestCancelled: -32800,
  AuthRequired: -32000,
  ResourceNotFound: -32002,
} as const;
