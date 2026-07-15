import type {
  AgentClient,
  AgentEvent,
  AgentMessagePersistence,
  AgentMessageSource,
  AgentMessageVisibility,
  AgentRequestEvent,
  AgentRunInputEvent,
  AIContent,
  ClientToolAugmentation,
  ClientToolInvokeOutcome,
  ForkThreadRequest,
  InterruptionResult,
  Thread,
  ThreadForkGroup,
  ThreadForkGroupMember,
  ThreadGraph,
  ThreadRuntimeChild,
  ThreadRun,
  PermissionChoice,
  RequestVisibility,
  ResponderTarget,
  ResponseMetadata,
  ResponsePolicy,
  RunConfig,
  SubmitInputResult,
  ToolCallType,
  ToolResultContent,
  ToolResultPayload,
  UsageDetails,
} from '@hpd-research/hpd-agent-client';

export interface ThreadScope {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface ThreadSnapshot {
  thread?: Thread | null;
  events: AgentEvent[];
  latestSequenceNumber: number;
  runs?: ThreadRun[];
  activeRun?: ThreadRun | null;
}

export type MessageRole = 'system' | 'user' | 'assistant' | 'tool' | string;
export type MessagePlacement = 'transcript' | 'work' | 'final' | 'optimistic';

export interface Message {
  id: string;
  role: MessageRole;
  content: string;
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
  source?: AgentMessageSource;
  visibility?: AgentMessageVisibility;
  persistence?: AgentMessagePersistence;
  streaming: boolean;
  thinking: boolean;
  timestamp: Date;
  toolCalls: ToolCall[];
  reasoning?: string;
  authorName?: string;
  clientInputId?: string | null;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
  placement: MessagePlacement;
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
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
  groupKey?: string;
}

export type ThreadWorkStatus = 'working' | 'worked' | 'failed' | 'cancelled';

export type ThreadWorkPart =
  | ThreadWorkReasoningPart
  | ThreadWorkAssistantDraftPart
  | ThreadWorkToolPart
  | ThreadWorkToolGroupPart
  | ThreadWorkProgressPart
  | ThreadWorkHookPart
  | ThreadWorkWarningPart;

export interface ThreadWorkReasoningPart {
  type: 'reasoning';
  id: string;
  messageId: string;
  text: string;
  status: 'streaming' | 'complete';
  eventFlowId?: string;
  sequenceNumber?: number;
}

export interface ThreadWorkAssistantDraftPart {
  type: 'assistant-draft';
  id: string;
  message: Message;
}

export interface ThreadWorkToolPart {
  type: 'tool';
  id: string;
  tool: ToolCall;
}

export interface ThreadWorkToolGroupPart {
  type: 'tool-group';
  id: string;
  group: ThreadToolGroup;
}

export interface ThreadWorkProgressPart {
  type: 'progress';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadWorkHookPart {
  type: 'hook';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadWorkWarningPart {
  type: 'warning';
  id: string;
  message: string;
  event?: AgentEvent;
}

export interface ThreadToolGroup {
  id: string;
  label: string;
  summary: string;
  status: 'active' | 'complete' | 'error';
  tools: ToolCall[];
  openByDefault: boolean;
}

export interface ThreadWorkGroup {
  id: string;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  status: ThreadWorkStatus;
  label: string;
  openByDefault: boolean;
  parts: ThreadWorkPart[];
  finalMessageId?: string;
  startedAt?: string;
  completedAt?: string | null;
  error?: string | null;
  usage?: UsageDetails | null;
}

export interface ThreadContextUsage {
  usage: UsageDetails;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  updatedAt?: string;
}

export type ThreadTimelineItem =
  | ThreadTimelineMessageItem
  | ThreadTimelineWorkItem
  | ThreadTimelineRuntimeRequestItem
  | ThreadTimelineProgressItem
  | ThreadTimelineWarningItem;

export interface ThreadTimelineMessageItem {
  type: 'message';
  id: string;
  message: Message;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
  eventFlowId?: string;
  sequenceNumber?: number;
}

export interface ThreadTimelineWorkItem {
  type: 'work';
  id: string;
  work: ThreadWorkGroup;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
}

export interface ThreadTimelineRuntimeRequestItem {
  type: 'runtime-request';
  id: string;
  request: RuntimeRequest;
  turnId: string | null;
  conversationId: string | null;
  runId: string | null;
}

export interface ThreadTimelineProgressItem {
  type: 'progress';
  id: string;
  label: string;
  event?: AgentEvent;
}

export interface ThreadTimelineWarningItem {
  type: 'warning';
  id: string;
  message: string;
  event?: AgentEvent;
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
  sourceName?: string;
  toolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  description?: string;
  responsePolicy?: ResponsePolicy;
  target?: ResponderTarget | null;
  visibility?: RequestVisibility;
}

export type ClientToolOutcomeInput =
  | ClientToolInvokeOutcome
  | ToolResultContent[]
  | string;

export interface AnswerClientToolRequestOptions extends ResponseMetadata {
  augmentation?: ClientToolAugmentation;
}

export type RuntimeRequestKind = 'permission' | 'clarification' | 'client-tool' | 'custom';

export interface RuntimeRequestBase {
  id: string;
  kind: RuntimeRequestKind;
  sourceName: string;
  requestEventType: string;
  expectedResponseEventType?: string;
  responsePolicy?: ResponsePolicy;
  target?: ResponderTarget | null;
  visibility?: RequestVisibility;
  startedAt?: string;
}

export interface PermissionRuntimeRequest extends RuntimeRequestBase {
  kind: 'permission';
  request: PermissionRequest;
  event?: AgentEvent;
}

export interface ClarificationRuntimeRequest extends RuntimeRequestBase {
  kind: 'clarification';
  request: ClarificationRequest;
  event?: AgentEvent;
}

export interface ClientToolRuntimeRequest extends RuntimeRequestBase {
  kind: 'client-tool';
  request: ClientToolRequest;
  event?: AgentEvent;
}

export interface CustomRuntimeRequest extends RuntimeRequestBase {
  kind: 'custom';
  event?: AgentRequestEvent | AgentEvent;
}

export type RuntimeRequest =
  | PermissionRuntimeRequest
  | ClarificationRuntimeRequest
  | ClientToolRuntimeRequest
  | CustomRuntimeRequest;

export type ThreadRunViewStatus = 'idle' | 'active' | 'completed' | 'cancelled' | 'failed' | 'interrupted';

export interface ThreadRunView {
  runtimeRunId: string;
  agentId: string;
  status: ThreadRunViewStatus;
  startedAt?: string;
  completedAt?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
  modelBackgroundOperation?: ThreadRun['modelBackgroundOperation'];
  backgroundTasks?: ThreadRun['backgroundTasks'];
  backgroundHandles?: ThreadRun['backgroundHandles'];
}

export interface ThreadActivity {
  status: 'idle' | 'working' | 'requesting' | 'failed' | 'cancelled';
  streaming: boolean;
  reasoning: boolean;
  activeToolCount: number;
  pendingRequestCount: number;
}

export type ThreadErrorKind = 'controller' | 'run' | 'work' | 'tool' | 'thread';

export interface ThreadErrorInfo {
  id: string;
  kind: ThreadErrorKind;
  message: string;
  type?: string | null;
  source?: string | null;
  runId?: string | null;
  turnId?: string | null;
  conversationId?: string | null;
  toolCallId?: string | null;
  recoverable: boolean;
}

export interface ThreadProjectionSnapshot {
  thread: Thread | null;
  timeline: ThreadTimelineItem[];
  workGroups: ThreadWorkGroup[];
  transcriptMessages: Message[];
  activeTools: ToolCall[];
  pendingRuntimeRequests: RuntimeRequest[];
  contextUsage: ThreadContextUsage | null;
  threadRun: ThreadRunView | null;
  activity: ThreadActivity;
  currentTurnId: string | null;
  currentConversationId: string | null;
  currentRunId: string | null;
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
  includeRuns?: boolean;
}

export interface ConnectOptions {
  signal?: AbortSignal;
}

export interface SendMessageInput {
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
}

export interface SendMessageOptions {
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
  clearError(): void;

  start(options?: RehydrateOptions & ConnectOptions): Promise<void>;
  rehydrate(options?: RehydrateOptions): Promise<void>;
  connect(options?: ConnectOptions): Promise<void>;
  disconnect(): Promise<void>;
  dispose(): Promise<void>;

  sendMessage(input: SendMessageInput, options?: SendMessageOptions): Promise<void>;
  run(input: AgentRunInputEvent): Promise<SubmitInputResult>;
  respond(input: AgentRunInputEvent): Promise<SubmitInputResult>;
  interrupt(options?: InterruptOptions): Promise<InterruptionResult>;

  approve(permissionId: string, choice?: PermissionChoice): Promise<SubmitInputResult>;
  deny(permissionId: string, reason?: string): Promise<SubmitInputResult>;
  clarify(requestId: string, answer: string): Promise<SubmitInputResult>;
  answerClientToolRequest(
    requestId: string,
    outcome: ClientToolOutcomeInput,
    options?: AnswerClientToolRequestOptions,
  ): Promise<SubmitInputResult>;
}

export interface LoadThreadSnapshotOptions extends ThreadScope {
  client: AgentClient;
}

export interface ThreadBranchNavigatorOptions {
  client: AgentClient;
  sessionId: string;
  threadId: string;
}

export type BranchChoiceRelationship = 'exact-member' | 'descendant-of-member';

export interface ActivePathChoice {
  group: ThreadForkGroup;
  selectedMember: ThreadForkGroupMember;
  selectedThreadId: string;
  relationship: BranchChoiceRelationship;
  previous: ThreadForkGroupMember | null;
  next: ThreadForkGroupMember | null;
  position: {
    current: number;
    total: number;
  };
}

export type ThreadBranchChoiceControlPlacement =
  | 'choice-message'
  | 'root'
  | 'unplaced';

export interface ThreadBranchChoiceControl {
  groupId: string;
  sourceThreadId: string;
  boundaryMessageId: string | null;
  boundaryMessageIndex: number | null;
  choiceMessageIndex: number;
  renderTimelineItemId: string;
  renderTimelineIndex: number;
  renderPlacement: ThreadBranchChoiceControlPlacement;
  selectedMember: ThreadForkGroupMember;
  selectedThreadId: string;
  relationship: BranchChoiceRelationship;
  members: ThreadForkGroupMember[];
  position: {
    current: number;
    total: number;
  };
  previous: ThreadForkGroupMember | null;
  next: ThreadForkGroupMember | null;
}

export interface ThreadBranchNavigationSnapshot {
  sessionId: string;
  threadId: string;
  graph: ThreadGraph;
  current: Thread | null;
  threads: Thread[];
  forkGroups: ThreadForkGroup[];
  activePathChoices: ActivePathChoice[];
  runtimeChildren: ThreadRuntimeChild[];
  hasRuntimeChildren: boolean;
}

export interface ThreadBranchNavigator {
  readonly sessionId: string;
  readonly threadId: string;

  getSnapshot(): ThreadBranchNavigationSnapshot;
  load(threadId?: string): Promise<ThreadBranchNavigationSnapshot>;
  selectThread(threadId: string): Promise<ThreadBranchNavigationSnapshot>;
  selectForkGroupMember(groupId: string, threadId: string): Promise<ThreadBranchNavigationSnapshot>;
  previousInGroup(groupId: string): Promise<ThreadBranchNavigationSnapshot>;
  nextInGroup(groupId: string): Promise<ThreadBranchNavigationSnapshot>;
}

export type ThreadRevisionKind = 'retry' | 'edit';

export type ThreadRevisionErrorCode =
  | 'message-not-found'
  | 'no-user-message'
  | 'empty-message'
  | 'unsupported-message-role';

export interface ThreadRevisionForkOptions
  extends Omit<ForkThreadRequest, 'agentId' | 'fromMessageId'> {}

export interface ThreadRevisionForkDetails {
  kind: ThreadRevisionKind;
  clickedMessageId: string;
  inputMessageId: string;
  forkBoundaryMessageId: string | null;
  sentText: string;
}

export interface ThreadRevisionOptions {
  runConfig?: RunConfig;
  fork?: ThreadRevisionForkOptions | ((details: ThreadRevisionForkDetails) => ThreadRevisionForkOptions | undefined);
}

export interface ThreadRevisionResult {
  kind: ThreadRevisionKind;
  thread: Thread;
  threadId: string;
  clickedMessageId: string;
  inputMessageId: string;
  forkBoundaryMessageId: string | null;
  sentText: string;
}

export interface ThreadRevisionControllerOptions extends ThreadScope {
  client: AgentClient;
}

export interface ThreadRevisionController {
  readonly scope: ThreadScope;

  forkAndRetryMessage(messageId: string, options?: ThreadRevisionOptions): Promise<ThreadRevisionResult>;
  forkAndEditMessage(messageId: string, text: string, options?: ThreadRevisionOptions): Promise<ThreadRevisionResult>;
}
