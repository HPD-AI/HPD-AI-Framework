import type {
  AgentClient,
  AgentEvent,
  AgentRunInputEvent,
  Thread,
  ThreadEvent,
  ThreadMessage,
  ThreadRun,
  PermissionChoice,
  RespondResult,
  RunConfig,
  ToolCallType,
  ToolResultPayload,
} from '@hpd-research/hpd-agent-client';

export interface ThreadScope {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface ThreadSnapshot {
  thread?: Thread | null;
  messages?: ThreadMessage[];
  events?: ThreadEvent[];
  runs?: ThreadRun[];
  activeRun?: ThreadRun | null;
}

export type MessageRole = 'system' | 'user' | 'assistant' | 'tool' | string;

export interface Message {
  id: string;
  role: MessageRole;
  content: string;
  streaming: boolean;
  thinking: boolean;
  timestamp: Date;
  toolCalls: ToolCall[];
  reasoning?: string;
  authorName?: string;
}

export type ToolCallStatus = 'pending' | 'executing' | 'complete' | 'error';

export interface ToolCall {
  callId: string;
  name: string;
  messageId: string;
  status: ToolCallStatus;
  startTime: Date;
  endTime?: Date;
  args?: unknown;
  result?: ToolResultPayload;
  resultText?: string;
  error?: string;
  toolharnessName?: string;
  callType?: ToolCallType;
}

export interface PermissionRequest {
  permissionId: string;
  sourceName: string;
  functionName: string;
  description?: string;
  callId: string;
  arguments?: Record<string, unknown>;
}

export interface ClarificationRequest {
  requestId: string;
  sourceName: string;
  question: string;
  agentName?: string;
  options?: string[];
}

export interface ClientToolRequest {
  requestId: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  description?: string;
}

export type ThreadRunViewStatus = 'idle' | 'active' | 'completed' | 'cancelled' | 'failed' | 'interrupted';

export interface ThreadRunView {
  runtimeRunId: string;
  agentId: string;
  status: ThreadRunViewStatus;
  startedAt?: string;
  completedAt?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
  backgroundOperation?: ThreadRun['backgroundOperation'];
  backgroundTasks?: ThreadRun['backgroundTasks'];
}

export interface ThreadProjectionSnapshot {
  thread: Thread | null;
  messages: Message[];
  streaming: boolean;
  reasoning: boolean;
  activeTools: ToolCall[];
  pendingPermissions: PermissionRequest[];
  pendingClarifications: ClarificationRequest[];
  pendingClientToolRequests: ClientToolRequest[];
  threadRun: ThreadRunView | null;
  currentTurnId: string | null;
  currentConversationId: string | null;
  error: string | null;
  canSend: boolean;
}

export type ThreadProjectionListener = (snapshot: ThreadProjectionSnapshot) => void;
export type Unsubscribe = () => void;

export interface ThreadProjection {
  getSnapshot(): ThreadProjectionSnapshot;
  subscribe(listener: ThreadProjectionListener): Unsubscribe;
  rehydrate(snapshot: ThreadSnapshot): void;
  project(event: AgentEvent): void;
  clearError(): void;
  reset(): void;
}

export interface RehydrateOptions {
  includeEvents?: boolean;
  includeRuns?: boolean;
}

export interface ConnectOptions {
  signal?: AbortSignal;
}

export interface SendTextOptions {
  runConfig?: RunConfig;
  signal?: AbortSignal;
}

export interface InterruptOptions {
  reason?: string;
  eventFlowId?: string | null;
  signal?: AbortSignal;
}

export interface ThreadControllerOptions extends ThreadScope {
  client: AgentClient;
  projection?: ThreadProjection;
  autoConnectOnSend?: boolean;
  stopClientOnDisconnect?: boolean;
  allowScopeLessEvents?: boolean;
}

export interface ThreadController {
  readonly scope: ThreadScope;
  readonly projection: ThreadProjection;
  readonly connected: boolean;
  readonly loading: boolean;
  readonly error: string | null;

  start(options?: RehydrateOptions & ConnectOptions): Promise<void>;
  rehydrate(options?: RehydrateOptions): Promise<void>;
  connect(options?: ConnectOptions): Promise<void>;
  disconnect(): Promise<void>;
  dispose(): Promise<void>;

  sendText(text: string, options?: SendTextOptions): Promise<void>;
  run(input: AgentRunInputEvent): Promise<RespondResult | undefined>;
  interrupt(options?: InterruptOptions): Promise<void>;

  approve(permissionId: string, choice?: PermissionChoice): Promise<RespondResult | undefined>;
  deny(permissionId: string, reason?: string): Promise<RespondResult | undefined>;
  clarify(requestId: string, answer: string): Promise<RespondResult | undefined>;
}

export interface LoadThreadSnapshotOptions extends ThreadScope {
  client: AgentClient;
}
