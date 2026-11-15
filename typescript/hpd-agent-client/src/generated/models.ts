/* eslint-disable */
// Generated from HPD Agent SDK IR v1.

export const ClientToolProviderBindingLeaseStatus = {
  Active: "Active",
  Released: "Released",
  Expired: "Expired",
  Disconnected: "Disconnected",
  Revoked: "Revoked",
  Broken: "Broken",
} as const;
export type ClientToolProviderBindingLeaseStatus = typeof ClientToolProviderBindingLeaseStatus[keyof typeof ClientToolProviderBindingLeaseStatus] | (string & {});

export const ClientToolProviderConnectionState = {
  Connected: "Connected",
  Registered: "Registered",
  Ready: "Ready",
  Bound: "Bound",
  Disconnected: "Disconnected",
  Revoked: "Revoked",
} as const;
export type ClientToolProviderConnectionState = typeof ClientToolProviderConnectionState[keyof typeof ClientToolProviderConnectionState] | (string & {});

export const ClientToolProviderReadiness = {
  Initializing: "Initializing",
  Ready: "Ready",
  Degraded: "Degraded",
  Revoked: "Revoked",
} as const;
export type ClientToolProviderReadiness = typeof ClientToolProviderReadiness[keyof typeof ClientToolProviderReadiness] | (string & {});

export interface AIAnnotation {
}

export interface AIAnnotationBase {
  annotatedRegions?: Array<AnnotatedRegion> | null;
  additionalProperties?: unknown;
}

export interface AIAnnotationCitationAnnotation {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<AnnotatedRegion> | null;
  additionalProperties?: unknown;
}

export interface AIContent {
}

export interface AIContentBase {
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentCodeInterpreterToolCallContent {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface AIContentCodeInterpreterToolResultContent {
  $type: string;
  outputs?: Array<AIContent> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentDataContent {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentErrorContent {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentFunctionCallContent {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentFunctionResultContent {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentHostedFileContent {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentHostedVectorStoreContent {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentImageGenerationToolCallContent {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface AIContentImageGenerationToolResultContent {
  $type: string;
  outputs?: Array<AIContent> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentInputRequestContent {
  $type: string;
  requestId?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentInputResponseContent {
  $type: string;
  requestId?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentMcpServerToolCallContent {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface AIContentMcpServerToolResultContent {
  $type: string;
  outputs?: Array<AIContent> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentTextContent {
  $type: string;
  text: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentTextReasoningContent {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentToolApprovalRequestContent {
  $type: string;
  toolCall: ToolCallContent;
  requestId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentToolApprovalResponseContent {
  $type: string;
  approved: boolean;
  toolCall: ToolCallContent;
  reason?: string | null;
  requestId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentToolCallContent {
  $type: string;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentToolResultContent {
  $type: string;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentUriContent {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentUsageContent {
  $type: string;
  details: UsageDetails;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIContentWebSearchToolCallContent {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface AIContentWebSearchToolResultContent {
  $type: string;
  outputs?: Array<AIContent> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface AIFunctionPermissionDeclaration {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface ActionRetryExclusion {
  function?: string | null;
  action?: string | null;
}

export interface AgentClientsConfig {
  transport?: number | null;
  chat?: null | ChatClientConfig | null;
  textToSpeech?: null | TextToSpeechClientConfig | null;
  speechToText?: null | SpeechToTextClientConfig | null;
  realtime?: null | RealtimeClientConfig | null;
  imageGeneration?: null | ImageGenerationClientConfig | null;
  embeddings?: null | EmbeddingsClientConfig | null;
  hostedFiles?: null | HostedFilesClientConfig | null;
  voiceActivity?: null | VoiceActivityClientConfig | null;
  endOfTurn?: null | EndOfTurnClientConfig | null;
}

export interface AgentConfig {
  name?: string | null;
  systemInstructions?: string | null;
  maxAgenticIterations?: number | null;
  maxSubAgentDepth?: number | null;
  defaultSubAgentForkOptions?: SubAgentForkOptions | null;
  continuationExtensionAmount?: number | null;
  shutdown?: AgentShutdownOptions | null;
  operationRetention?: AgentOperationRetentionPolicy | null;
  clients?: AgentClientsConfig | null;
  providerProfiles?: Array<AgentProviderBackendProfile> | null;
  providerDefaults?: Array<AgentProviderFamilyDefault> | null;
  validation?: null | ValidationConfig | null;
  errorHandling?: null | ErrorHandlingConfig | null;
  compaction?: null | CompactionConfig | null;
  goals?: null | GoalConfig | null;
  enableParentCommunication?: boolean | null;
  allowUserQuestions?: boolean | null;
  agenticLoop?: null | AgenticLoopConfig | null;
  messages?: AgentMessagesConfig | null;
  toolSelection?: null | ToolSelectionConfig | null;
  collapsing?: CollapsingConfig | null;
  skills?: SkillRuntimeOptions | null;
  toolOutputs?: ToolOutputConfig | null;
  toolHarnesses?: Array<unknown> | null;
  mcp?: null | AgentMcpConfig | null;
  middlewares?: Array<unknown> | null;
  observability?: null | ObservabilityConfig | null;
  backgroundResponses?: null | BackgroundResponsesConfig | null;
  audio?: null | AudioConfig | null;
  includeReasoningInModelHistory?: boolean | null;
  coalesceDeltas?: boolean | null;
}

export interface AgentMcpConfig {
  servers?: Array<McpServerConfig> | null;
  manifests?: Array<McpManifestReference> | null;
}

export interface AgentMessagesConfig {
  maxIterationsReached?: string | null;
  circuitBreakerTriggered?: string | null;
  maxConsecutiveErrors?: string | null;
  permissionDeniedDefault?: string | null;
}

export interface AgentMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: Array<string> | null;
  depth?: number | null;
}

export interface AgentOperationNotificationPolicy {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface AgentOperationRetentionPolicy {
  terminalRetention?: string | null;
  providerDeduplicationRetention?: string | null;
  maximumTerminalOperationsPerThread?: number | null;
}

export interface AgentProviderBackendProfile {
  providerKey: string;
  backendKey: string;
  clients: AgentClientsConfig;
}

export interface AgentProviderFamilyDefault {
  family: number;
  providerKey: string;
  backendKey: string;
}

export interface AgentRunConfig {
  harnesses?: Array<InlineAgentRunConfigHarnessesItem | null> | null;
  toolOutputs?: InlineAgentRunConfigToolOutputs | null;
  allowUserQuestions?: boolean | null;
  enableParentCommunication?: boolean | null;
  security?: InlineAgentRunConfigSecurity | null;
  clients?: InlineAgentRunConfigClients | null;
  systemInstructions?: InlineAgentRunConfigSystemInstructions | null;
  tools?: InlineAgentRunConfigTools | null;
  context?: InlineAgentRunConfigContext | null;
  backgroundResponses?: InlineAgentRunConfigBackgroundResponses | null;
  streaming?: InlineAgentRunConfigStreaming | null;
  uploadStrategy?: number | null;
  audio?: InlineAgentRunConfigAudio | null;
  compaction?: InlineAgentRunConfigCompaction | null;
  goals?: InlineAgentRunConfigGoals | null;
  collapsing?: InlineAgentRunConfigCollapsing | null;
  structuredOutput?: InlineAgentRunConfigStructuredOutput | null;
}

export interface AgentShutdownOptions {
  gracefulDrainTimeout?: string | null;
  cancellationDrainTimeout?: string | null;
  remoteOperations?: number | null;
  leaseLeaks?: number | null;
}

export interface AgentSummaryDto {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
  metadata: unknown;
}

export interface AgenticLoopConfig {
  maxTurnDuration?: string | null;
  maxParallelFunctions?: number | null;
  terminateOnUnknownCalls?: boolean | null;
}

export interface AnnotatedRegion {
}

export interface AudioConfig {
  enabled?: boolean | null;
  inputMode?: number | null;
  outputMode?: number | null;
  transport?: null | AudioTransportConfig | null;
  policy?: null | AudioPolicySet | null;
  assistantOutputMode?: number | null;
  pacing?: null | TextToSpeechPacingOptions | null;
  progressiveRouteMode?: number | null;
  pushTextAggregationMode?: number | null;
  artifactCapturePolicy?: number | null;
  enablePlayback?: boolean | null;
}

export interface AudioPolicySet {
  inputMedia?: InputMediaPolicy | null;
  trace?: TraceCapturePolicy | null;
  privacy?: PrivacyPolicy | null;
  threadProjection?: ThreadProjectionPolicy | null;
}

export interface AudioTransportConfig {
  componentInstance: string;
  endpoint: string;
  schema?: string | null;
  version?: number | null;
  providerConfig?: null | unknown | null;
}

export interface AutomaticCompactionPolicy {
  trigger: CompactionTrigger;
  compaction: CompactionSpecification;
  continuation?: number | null;
}

export interface BackgroundResponsesConfig {
  defaultAllow?: boolean | null;
  defaultPollingInterval?: string | null;
  defaultTimeout?: string | null;
  autoPollToCompletion?: boolean | null;
  maxPollAttempts?: number | null;
}

export interface ChatClientConfig {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: null | ReasoningOptions | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface ChatDetails {
  turnDetails: Array<ChatTurnDetails>;
}

export interface ChatMessage {
  authorName?: string | null;
  createdAt?: string | null;
  role?: unknown;
  contents?: Array<AIContent> | null;
  messageId?: string | null;
  additionalProperties?: unknown;
}

export interface ChatResponse {
  messages?: Array<ChatMessage> | null;
  responseId?: string | null;
  conversationId?: string | null;
  modelId?: string | null;
  createdAt?: string | null;
  finishReason?: null | unknown | null;
  usage?: null | UsageDetails | null;
  continuationToken?: null | unknown | null;
  additionalProperties?: unknown;
}

export interface ChatTurnDetails {
  latency: string;
  model: string | null;
  modelProvider: string | null;
  usage?: null | UsageDetails | null;
  cacheKey?: string | null;
  cacheHit?: boolean | null;
}

export interface ClientAppProviderDescriptor {
  name: string;
  displayName?: string | null;
  description?: string | null;
  version?: string | null;
  tags?: Array<string> | null;
  metadata?: unknown;
}

export interface ClientSkillDefinition {
  name: string;
  description: string;
  instructions: string;
  reinforcement?: string | null;
  references?: Array<ClientSkillReference> | null;
}

export interface ClientSkillReference {
  toolName: string;
  toolsetName?: string | null;
}

export interface ClientToolDefinition {
  name: string;
  description: string;
  parametersSchema: unknown;
  defaultPolicy?: ClientToolPolicy | null;
  operationContract?: null | ClientToolOperationContract | null;
  metadata?: unknown;
}

export interface ClientToolOperationContract {
  discriminator: string;
  actions: Record<string, ClientToolPolicy>;
}

export interface ClientToolPolicy {
  permission?: null | AIFunctionPermissionDeclaration | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: null | unknown | null;
  operationNotification?: null | AgentOperationNotificationPolicy | null;
}

export interface ClientToolProviderBindingLease {
  bindingId: string;
  clientRuntimeId: string;
  connectionId: string;
  ownerRuntimeId?: string | null;
  agentId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  boundAt?: string | null;
  expiresAt?: string | null;
  heartbeatInterval?: string | null;
  status?: ClientToolProviderBindingLeaseStatus | null;
  releasedAt?: string | null;
  releaseReason?: string | null;
}

export interface ClientToolProviderContext {
  workspaceId?: string | null;
  documentId?: string | null;
  documentName?: string | null;
  pageId?: string | null;
  fileId?: string | null;
  projectId?: string | null;
  sceneId?: string | null;
  activeView?: string | null;
  selectionSummary?: string | null;
  appStateVersion?: string | null;
  metadata?: unknown;
}

export interface ClientToolProviderIdentity {
  providerName: string;
  appKind: string;
  instanceId?: string | null;
  installationId?: string | null;
  userHint?: string | null;
  origin?: string | null;
  version?: string | null;
}

export interface ClientToolProviderManifest {
  protocolVersion?: string | null;
  identity: ClientToolProviderIdentity;
  appProvider: ClientAppProviderDescriptor;
  context?: null | ClientToolProviderContext | null;
  readiness?: ClientToolProviderReadiness | null;
  clientToolHarnesses?: Array<ClientToolHarnessDefinition> | null;
  metadata?: unknown;
}

export interface ClientToolProviderRuntimeIdentity {
  appId: string;
  appRevision: string;
  installationId: string;
  workloadId: string;
  workloadGeneration: number;
  endpointId: string;
  publicationId: string;
  publicationGeneration: number;
  launchSurfaceId: string;
  browserLaunchSessionId: string;
  browserLaunchSessionGeneration: number;
  providerConnectionGeneration: number;
  origin: string;
}

export interface ClientToolProviderSnapshot {
  clientRuntimeId: string;
  connectionId: string;
  runtimeIdentity?: null | ClientToolProviderRuntimeIdentity | null;
  manifest?: null | ClientToolProviderManifest | null;
  state?: ClientToolProviderConnectionState | null;
  connectedAt?: string | null;
  lastHeartbeatAt?: string | null;
  disconnectedAt?: string | null;
  bindingLease?: null | ClientToolProviderBindingLease | null;
}

export interface CollapsingConfig {
  enabled?: boolean | null;
  collapseClientTools?: boolean | null;
  maxFunctionNamesInDescription?: number | null;
  persistSystemPromptInjections?: boolean | null;
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  mcpServerInstructions?: Record<string, string> | null;
  clientToolsInstructions?: string | null;
  neverCollapse?: Array<string> | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface CompactionConfig {
  automatic?: null | AutomaticCompactionPolicy | null;
  forkCompaction?: null | CompactionSpecification | null;
}

export interface CompactionEvidenceOptions {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface CompactionPoint {
}

export interface CompactionPointCompactAtCurrentHead {
  $type?: string | null;
}

export interface CompactionPointCompactAtMessage {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface CompactionPointCompactAtTurn {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface CompactionPreservation {
}

export interface CompactionPreservationPreserveNoPreviousHistory {
  $type?: string | null;
}

export interface CompactionPreservationPreservePreviousTurns {
  $type?: string | null;
  count: number;
}

export interface CompactionPreservationPreservePreviousUserMessages {
  $type?: string | null;
  limit: PreviousHistoryLimit;
}

export interface CompactionSpecification {
  point: CompactionPoint;
  preservation?: CompactionPreservation | null;
  strategy: CompactionStrategy;
  commitMode?: number | null;
}

export interface CompactionStrategy {
}

export interface CompactionStrategyRemovalCompaction {
  $type?: string | null;
}

export interface CompactionStrategySummarizingCompaction {
  $type?: string | null;
  summarizer?: null | ChatClientConfig | null;
  instructions?: string | null;
  evidence?: CompactionEvidenceOptions | null;
}

export interface CompactionTrigger {
}

export interface CompactionTriggerContextPercentageCompactionTrigger {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface CompactionTriggerInputTokenCompactionTrigger {
  $type?: string | null;
  inputTokens: number;
}

export interface CompactionTriggerTurnCountCompactionTrigger {
  $type?: string | null;
  turns: number;
}

export interface ContentDto {
  contentId: string;
  version: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
}

export interface ContentRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface CreateAgentRequest {
  agentId: string;
  name: string;
  config: AgentConfig;
  metadata?: unknown;
}

export interface CreateSessionRequest {
  agentId?: string | null;
  sessionId?: string | null;
  metadata?: unknown;
}

export interface CreateThreadRequest {
  threadId: string;
  name: string | null;
  description: string | null;
  tags: Array<string> | null;
  metadata?: unknown;
}

export interface DatasetRecord {
  datasetId: string;
  currentVersion: string;
  description: string | null;
  contentHash: string;
  registeredAt: string;
  metadata: unknown;
}

export interface DatasetVersionRecord {
  datasetId: string;
  version: string;
  description: string | null;
  contentHash: string;
  caseCount: number;
  registeredAt: string;
  metadata: unknown;
}

export interface EmbeddingsClientConfig {
  dimensions?: number | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface EndOfTurnClientConfig {
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface ErrorHandlingConfig {
  normalizeErrors?: boolean | null;
  includeProviderDetails?: boolean | null;
  includeDetailedErrorsInChat?: boolean | null;
  modelRetries?: ModelRetryConfig | null;
  functionRetries?: FunctionRetryConfig | null;
  singleFunctionTimeout?: string | null;
}

export interface EvaluationContext {
  name?: string | null;
  contents?: Array<AIContent> | null;
}

export interface EvaluationDiagnostic {
  severity: number;
  message: string;
}

export interface EvaluationMetric {
}

export interface EvaluationMetricBooleanMetric {
  $type?: string | null;
  value?: boolean | null;
  name: string;
  reason?: string | null;
  interpretation?: null | EvaluationMetricInterpretation | null;
  context?: Record<string, EvaluationContext> | null;
  diagnostics?: Array<EvaluationDiagnostic> | null;
  metadata?: Record<string, string> | null;
}

export interface EvaluationMetricEvaluationMetric {
  $type?: string | null;
  name: string;
  reason?: string | null;
  interpretation?: null | EvaluationMetricInterpretation | null;
  context?: Record<string, EvaluationContext> | null;
  diagnostics?: Array<EvaluationDiagnostic> | null;
  metadata?: Record<string, string> | null;
}

export interface EvaluationMetricInterpretation {
  rating?: number | null;
  failed?: boolean | null;
  reason?: string | null;
}

export interface EvaluationMetricNumericMetric {
  $type?: string | null;
  value?: number | null;
  name: string;
  reason?: string | null;
  interpretation?: null | EvaluationMetricInterpretation | null;
  context?: Record<string, EvaluationContext> | null;
  diagnostics?: Array<EvaluationDiagnostic> | null;
  metadata?: Record<string, string> | null;
}

export interface EvaluationMetricStringMetric {
  $type?: string | null;
  value?: string | null;
  name: string;
  reason?: string | null;
  interpretation?: null | EvaluationMetricInterpretation | null;
  context?: Record<string, EvaluationContext> | null;
  diagnostics?: Array<EvaluationDiagnostic> | null;
  metadata?: Record<string, string> | null;
}

export interface EvaluationResult {
  metrics: Record<string, EvaluationMetric>;
}

export interface EvaluationRunRecord {
  id?: string | null;
  executionName?: string | null;
  scenarioName?: string | null;
  iterationName?: string | null;
  createdAt?: string | null;
  messages?: Array<ChatMessage> | null;
  modelResponse?: ChatResponse | null;
  evaluationResult?: EvaluationResult | null;
  chatDetails?: null | ChatDetails | null;
  judgeCalls?: Array<JudgeCallRecord> | null;
  tags?: Array<string> | null;
  metadata?: unknown;
  source?: number | null;
  agentName?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  turnIndex?: number | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseModelId?: string | null;
  datasetId?: string | null;
  datasetVersion?: string | null;
  caseId?: string | null;
  caseVersion?: string | null;
  caseValidFrom?: string | null;
  caseValidTo?: string | null;
  taskDuration?: string | null;
  evaluatorDuration?: string | null;
  totalDuration?: string | null;
}

export interface EvaluatorCatalogItem {
  id: string;
  name: string;
  category: string;
  kind: string;
  metricNames: Array<string>;
  defaultPolicy: string;
  requiresJudge: boolean;
  requiredContexts: Array<string>;
  description: string;
}

export interface EvaluatorSummary {
  evaluatorName: string;
  totalCount: number;
  averageScore: number;
  passRate: number;
  averageJudgeCostUsd: number;
  averageJudgeDuration: string;
  failureCount: number;
}

export interface FailureRateResponse {
  evaluatorName: string;
  failureRate: number;
}

export interface ForkThreadRequest {
  newThreadId: string | null;
  fromMessageId: string | null;
  name: string | null;
  description: string | null;
  tags: Array<string> | null;
  metadata?: unknown;
  compaction?: null | ThreadForkCompaction | null;
  subAgents?: null | SubAgentForkOptions | null;
  operationId?: string | null;
}

export interface FunctionRetryConfig {
  exclude?: RetryExclusionConfig | null;
  maxRetries?: number | null;
  retryDelay?: string | null;
  useProviderRetryDelays?: boolean | null;
  maxRetryDelay?: string | null;
  backoffMultiplier?: number | null;
  maxRetriesByCategory?: Record<string, number> | null;
}

export interface GoalConfig {
  enabled?: boolean | null;
  allowModelCreatedGoals?: boolean | null;
  maximumObjectiveLength?: number | null;
  requiredConsecutiveBlockerExecutions?: number | null;
  maximumConsecutiveNoProgressExecutions?: number | null;
  policies?: GoalPolicySelectionConfig | null;
}

export interface GoalPolicySelectionConfig {
  completion?: string | null;
  blocker?: string | null;
  continuation?: string | null;
  accounting?: string | null;
  fork?: string | null;
}

export interface HPDAgentAgentCapabilityRefreshPublishedEvent {
  type: "AGENT_CAPABILITY_REFRESH_PUBLISHED";
  previousEpoch: number;
  newEpoch: number;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentCapabilityRefreshRejectedEvent {
  type: "AGENT_CAPABILITY_REFRESH_REJECTED";
  retainedEpoch: number;
  error: string;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentCapabilityRefreshStartedEvent {
  type: "AGENT_CAPABILITY_REFRESH_STARTED";
  currentEpoch: number;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentCompletionEvent {
  type: "AGENT_COMPLETION";
  agentName: string;
  totalIterations: number;
  duration: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentDecisionEvent {
  type: "AGENT_DECISION";
  agentName: string;
  decisionType: string;
  iteration: number;
  consecutiveFailures: number;
  completedFunctionsCount: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationNotificationDeliveredEvent {
  type: "AGENT_OPERATION_NOTIFICATION_DELIVERED";
  kind?: number | null;
  notificationId: string;
  deliveredAt: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationNotificationInputEvent {
  type: "AGENT_OPERATION_NOTIFICATION_INPUT";
  notifications: Array<InlineHPDAgentAgentOperationNotificationInputEventNotificationsItem>;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentAgentOperationNotificationQueuedEvent {
  type: "AGENT_OPERATION_NOTIFICATION_QUEUED";
  kind?: number | null;
  notification: InlineHPDAgentAgentOperationNotificationQueuedEventNotification;
  queuedAt: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationNotificationSuppressedEvent {
  type: "AGENT_OPERATION_NOTIFICATION_SUPPRESSED";
  kind?: number | null;
  operationId: string;
  reason: string;
  suppressedAt: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationRegisteredEvent {
  type: "AGENT_OPERATION_REGISTERED";
  kind?: number | null;
  operation: InlineHPDAgentAgentOperationRegisteredEventOperation;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationTombstonedEvent {
  type: "AGENT_OPERATION_TOMBSTONED";
  kind?: number | null;
  tombstone: InlineHPDAgentAgentOperationTombstonedEventTombstone;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentOperationTransitionedEvent {
  type: "AGENT_OPERATION_TRANSITIONED";
  kind?: number | null;
  operationId: string;
  previousVersion: number;
  operation: InlineHPDAgentAgentOperationTransitionedEventOperation;
  providerDeduplicationKey?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentRequestTerminatedEvent {
  type: "AGENT_REQUEST_TERMINATED";
  requestId: string;
  sourceName: string;
  terminalKind: string;
  reason?: string | null;
  terminatedAt: string;
  kind?: number | null;
  channel?: number | null;
  direction?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentTurnCapabilitiesPinnedEvent {
  type: "AGENT_TURN_CAPABILITIES_PINNED";
  identity: InlineHPDAgentAgentTurnCapabilitiesPinnedEventIdentity;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentTurnFinishedEvent {
  type: "AGENT_TURN_FINISHED";
  messageTurnId: string;
  iteration: number;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  family: number;
  outcome: number;
  usage?: InlineHPDAgentAgentTurnFinishedEventUsage | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAgentTurnStartedEvent {
  type: "AGENT_TURN_STARTED";
  iteration: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentAudioSessionInputEvent {
  type: "AUDIO_SESSION_INPUT";
  command: InlineHPDAgentAudioSessionInputEventCommandAnyOf0 | InlineHPDAgentAudioSessionInputEventCommandAnyOf1 | InlineHPDAgentAudioSessionInputEventCommandAnyOf2 | InlineHPDAgentAudioSessionInputEventCommandAnyOf3 | InlineHPDAgentAudioSessionInputEventCommandAnyOf4 | InlineHPDAgentAudioSessionInputEventCommandAnyOf5 | InlineHPDAgentAudioSessionInputEventCommandAnyOf6;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentCircuitBreakerTriggeredEvent {
  type: "CIRCUIT_BREAKER_TRIGGERED";
  agentName: string;
  functionName: string;
  consecutiveCount: number;
  iteration: number;
  channel?: number | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentClientToolsClientToolInvokeOutcomeEvent {
  type: "CLIENT_TOOL_INVOKE_OUTCOME";
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  requestId: string;
  outcome: string;
  content?: Array<unknown> | null;
  errorMessage?: string | null;
  error?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventError | null;
  clientOperationId?: string | null;
  operationKind?: number | null;
  operationCapabilities?: number | null;
  augmentation?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentation | null;
  sourceName?: string | null;
  responderId?: string | null;
  responderGroup?: string | null;
  capabilities?: Array<string> | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentClientToolsClientToolInvokeRequestEvent {
  type: "CLIENT_TOOL_INVOKE_REQUEST";
  requestId: string;
  toolName: string;
  callId: string;
  arguments: InlineHPDAgentClientToolsClientToolInvokeRequestEventArguments;
  description?: string | null;
  channel?: number | null;
  kind?: number | null;
  sourceName?: string | null;
  responsePolicy?: number | null;
  target?: InlineHPDAgentClientToolsClientToolInvokeRequestEventTarget | null;
  visibility?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentClientToolsClientToolOperationOutcomeEvent {
  type: "CLIENT_TOOL_OPERATION_OUTCOME";
  clientOperationId: string;
  state: string;
  content?: Array<unknown> | null;
  augmentation?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentation | null;
  errorMessage?: string | null;
  errorType?: string | null;
  cancellationReason?: string | null;
  metadata?: Record<string, string> | null;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentCollapsedToolsVisibleEvent {
  type: "COLLAPSED_TOOLS_VISIBLE";
  agentName: string;
  iteration: number;
  visibleToolNames: Array<string>;
  expandedToolHarnesses: Array<string>;
  expandedSkills: Array<string>;
  totalToolCount: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentCollapsingStateEvent {
  type: "COLLAPSING_STATE";
  agentName: string;
  iteration: number;
  expandedToolHarnessesCount: number;
  expandedSkillsCount: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentCompactThreadInputEvent {
  type: "COMPACT_THREAD_INPUT";
  request: InlineHPDAgentCompactThreadInputEventRequest;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentCompactionEvent {
  type: "COMPACTION";
  agentName: string;
  iteration: number;
  status: number;
  startedAt: string;
  completedAt: string;
  originalMessageCount?: number | null;
  compactedMessageCount?: number | null;
  messagesRemoved?: number | null;
  summaryContent?: string | null;
  reason?: string | null;
  strategy?: string | null;
  continuation?: number | null;
  origin?: number | null;
  summarizer?: InlineHPDAgentCompactionEventSummarizer | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContainerExpandedEvent {
  type: "CONTAINER_EXPANDED";
  containerName: string;
  containerType: number;
  unlockedFunctions: Array<string>;
  iteration: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContentAddedEvent {
  type: "CONTENT_ADDED";
  messageId: string;
  role: string;
  content: InlineHPDAgentContentAddedEventContentAnyOf0 | InlineHPDAgentContentAddedEventContentAnyOf1 | InlineHPDAgentContentAddedEventContentAnyOf2 | InlineHPDAgentContentAddedEventContentAnyOf3 | InlineHPDAgentContentAddedEventContentAnyOf4 | InlineHPDAgentContentAddedEventContentAnyOf5 | InlineHPDAgentContentAddedEventContentAnyOf6 | InlineHPDAgentContentAddedEventContentAnyOf7 | InlineHPDAgentContentAddedEventContentAnyOf8 | InlineHPDAgentContentAddedEventContentAnyOf9 | InlineHPDAgentContentAddedEventContentAnyOf10 | InlineHPDAgentContentAddedEventContentAnyOf11 | InlineHPDAgentContentAddedEventContentAnyOf12 | InlineHPDAgentContentAddedEventContentAnyOf13 | InlineHPDAgentContentAddedEventContentAnyOf14 | InlineHPDAgentContentAddedEventContentAnyOf15 | InlineHPDAgentContentAddedEventContentAnyOf16 | InlineHPDAgentContentAddedEventContentAnyOf17 | InlineHPDAgentContentAddedEventContentAnyOf18 | InlineHPDAgentContentAddedEventContentAnyOf19 | InlineHPDAgentContentAddedEventContentAnyOf20 | InlineHPDAgentContentAddedEventContentAnyOf21 | InlineHPDAgentContentAddedEventContentAnyOf22 | InlineHPDAgentContentAddedEventContentAnyOf23 | InlineHPDAgentContentAddedEventContentAnyOf24;
  authorName?: string | null;
  createdAt?: string | null;
  clientInputId?: string | null;
  source?: number | null;
  visibility?: number | null;
  persistence?: number | null;
  additionalProperties?: unknown;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContentCaptureFinalizedEvent {
  type: "CONTENT_CAPTURE_FINALIZED";
  functionName?: string | null;
  callId: string;
  partName: string;
  mediaType: string;
  sizeBytes: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContentReadEvent {
  type: "CONTENT_READ";
  contentId: string;
  contentVersion?: string | null;
  partName: string;
  encoding: string;
  bytesReturned: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContentScopeCleanedEvent {
  type: "CONTENT_SCOPE_CLEANED";
  scope: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContinuationRequestEvent {
  type: "CONTINUATION_REQUEST";
  continuationId: string;
  sourceName: string;
  currentIteration: number;
  maxIterations: number;
  channel?: number | null;
  kind?: number | null;
  requestId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentContinuationResponseEvent {
  type: "CONTINUATION_RESPONSE";
  continuationId: string;
  sourceName: string;
  approved: boolean;
  extensionAmount?: number | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  requestId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentDeltaSendingActivatedEvent {
  type: "DELTA_SENDING_ACTIVATED";
  agentName: string;
  messageCountSent: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentDocumentProcessedEvent {
  type: "DOCUMENT_PROCESSED";
  agentName: string;
  documentPath: string;
  sizeBytes: number;
  duration: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentEventDroppedEvent {
  type: "EVENT_DROPPED";
  droppedEventFlowId: string;
  droppedEventType: string;
  droppedSequenceNumber: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentFunctionInvocationAuditedEvent {
  type: "FUNCTION_INVOCATION_AUDITED";
  kind?: number | null;
  invocation: InlineHPDAgentFunctionInvocationAuditedEventInvocation;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentFunctionRetryEvent {
  type: "FUNCTION_RETRY";
  functionName: string;
  attempt: number;
  maxRetries: number;
  delay: string;
  exceptionType: string;
  errorMessage: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsCreateGoalInputEvent {
  type: "CREATE_GOAL_INPUT";
  objective: string;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentGoalsGoalAwaitingInputEvent {
  type: "GOAL_AWAITING_INPUT";
  goal: InlineHPDAgentGoalsGoalAwaitingInputEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalBlockedEvent {
  type: "GOAL_BLOCKED";
  goal: InlineHPDAgentGoalsGoalBlockedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalBlockerRejectedEvent {
  type: "GOAL_BLOCKER_REJECTED";
  goal: InlineHPDAgentGoalsGoalBlockerRejectedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalBlockerReportedEvent {
  type: "GOAL_BLOCKER_REPORTED";
  goal: InlineHPDAgentGoalsGoalBlockerReportedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalClearedEvent {
  type: "GOAL_CLEARED";
  goal: InlineHPDAgentGoalsGoalClearedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalCompletedEvent {
  type: "GOAL_COMPLETED";
  goal: InlineHPDAgentGoalsGoalCompletedEventGoal;
  reason: string;
  acceptedProposal?: InlineHPDAgentGoalsGoalCompletedEventAcceptedProposal | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalCompletionProposedEvent {
  type: "GOAL_COMPLETION_PROPOSED";
  goal: InlineHPDAgentGoalsGoalCompletionProposedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalCompletionRejectedEvent {
  type: "GOAL_COMPLETION_REJECTED";
  goal: InlineHPDAgentGoalsGoalCompletionRejectedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalContinuationScheduledEvent {
  type: "GOAL_CONTINUATION_SCHEDULED";
  goal: InlineHPDAgentGoalsGoalContinuationScheduledEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalContinuationSkippedEvent {
  type: "GOAL_CONTINUATION_SKIPPED";
  goal: InlineHPDAgentGoalsGoalContinuationSkippedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalContinuationStartedEvent {
  type: "GOAL_CONTINUATION_STARTED";
  goal: InlineHPDAgentGoalsGoalContinuationStartedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalEditedEvent {
  type: "GOAL_EDITED";
  goal: InlineHPDAgentGoalsGoalEditedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalFaultedEvent {
  type: "GOAL_FAULTED";
  goal: InlineHPDAgentGoalsGoalFaultedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalPausedEvent {
  type: "GOAL_PAUSED";
  goal: InlineHPDAgentGoalsGoalPausedEventGoal;
  reason: string;
  cancellation?: InlineHPDAgentGoalsGoalPausedEventCancellation | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalProgressAccountedEvent {
  type: "GOAL_PROGRESS_ACCOUNTED";
  goal: InlineHPDAgentGoalsGoalProgressAccountedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalResumedEvent {
  type: "GOAL_RESUMED";
  goal: InlineHPDAgentGoalsGoalResumedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalStartedEvent {
  type: "GOAL_STARTED";
  goal: InlineHPDAgentGoalsGoalStartedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalUpdatedEvent {
  type: "GOAL_UPDATED";
  goal: InlineHPDAgentGoalsGoalUpdatedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentGoalsGoalUsageLimitedEvent {
  type: "GOAL_USAGE_LIMITED";
  goal: InlineHPDAgentGoalsGoalUsageLimitedEventGoal;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentInternalMessagePreparedEvent {
  type: "INTERNAL_MESSAGE_PREPARED";
  agentName: string;
  iteration: number;
  finalMessageCount: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentInternalParallelToolExecutionEvent {
  type: "INTERNAL_PARALLEL_TOOL_EXECUTION";
  agentName: string;
  iteration: number;
  toolCount: number;
  parallelBatchSize: number;
  approvedCount: number;
  deniedCount: number;
  duration: string;
  semaphoreWaitDuration?: string | null;
  isParallel: boolean;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentInterruptionHandledEvent {
  type: "INTERRUPTION_HANDLED";
  reason: string;
  source: number;
  channel?: number | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentIterationStartEvent {
  type: "ITERATION_START";
  agentName: string;
  iteration: number;
  maxIterations: number;
  currentMessageCount: number;
  historyMessageCount: number;
  turnHistoryMessageCount: number;
  completedFunctionsCount: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMaxConsecutiveErrorsExceededEvent {
  type: "MAX_CONSECUTIVE_ERRORS_EXCEEDED";
  agentName: string;
  consecutiveErrors: number;
  maxConsecutiveErrors: number;
  iteration: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMessageTurnErrorEvent {
  type: "MESSAGE_TURN_ERROR";
  messageTurnId: string;
  errorMessage: string;
  usage: InlineHPDAgentMessageTurnErrorEventUsage;
  kind?: number | null;
  conversationId?: string | null;
  agentId?: string | null;
  agentName?: string | null;
  errorType?: string | null;
  cancellation?: InlineHPDAgentMessageTurnErrorEventCancellation | null;
  category?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMessageTurnFinishedEvent {
  type: "MESSAGE_TURN_FINISHED";
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  duration: string;
  usage: InlineHPDAgentMessageTurnFinishedEventUsage;
  kind?: number | null;
  iteration?: number | null;
  terminationReason?: string | null;
  turnMessageCount?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMessageTurnStartedEvent {
  type: "MESSAGE_TURN_STARTED";
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  kind?: number | null;
  inputMessageCount?: number | null;
  isResume?: boolean | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareContentReferenceResolutionFailedEvent {
  type: "CONTENT_REFERENCE_RESOLUTION_FAILED";
  contentUri: string;
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareContentReferenceResolvedEvent {
  type: "CONTENT_REFERENCE_RESOLVED";
  contentUri: string;
  resolutionKind: number;
  mediaType: string;
  sizeBytes?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareContentUploadFailedEvent {
  type: "CONTENT_UPLOAD_FAILED";
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareContentUploadedEvent {
  type: "CONTENT_UPLOADED";
  contentId: string;
  mediaType: string;
  sizeBytes: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareHostedFileUploadFailedEvent {
  type: "HOSTED_FILE_UPLOAD_FAILED";
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareHostedFileUploadedEvent {
  type: "HOSTED_FILE_UPLOADED";
  fileId: string;
  mediaType: string;
  sizeBytes?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareErrorEvent {
  type: "MIDDLEWARE_ERROR";
  sourceName: string;
  errorMessage: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareStateChangedEvent {
  type: "MIDDLEWARE_STATE_CHANGED";
  agentName: string;
  iteration: number;
  phase: string;
  batchId?: string | null;
  functionCallId?: string | null;
  toolCallIndex?: number | null;
  changeCount: number;
  changes: Array<InlineHPDAgentMiddlewareStateChangedEventChangesItem>;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentMiddlewareStateSnapshotEvent {
  type: "MIDDLEWARE_STATE_SNAPSHOT";
  agentName: string;
  iteration: number;
  phase: string;
  batchId?: string | null;
  functionCallId?: string | null;
  toolCallIndex?: number | null;
  stateCount: number;
  states: Array<InlineHPDAgentMiddlewareStateSnapshotEventStatesItem>;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentModelCallRetryEvent {
  type: "MODEL_CALL_RETRY";
  attempt: number;
  maxRetries: number;
  delay: string;
  exceptionType: string;
  errorMessage: string;
  channel?: number | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentNestedAgentInvokedEvent {
  type: "NESTED_AGENT_INVOKED";
  orchestratorName: string;
  childAgentName: string;
  nestingDepth: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentOperationExecutionOwnerCleanupFailedEvent {
  type: "OPERATION_EXECUTION_OWNER_CLEANUP_FAILED";
  kind?: number | null;
  operationId: string;
  operationName: string;
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPIIDetectedEvent {
  type: "PII_DETECTED";
  agentName: string;
  piiType: string;
  strategy: number;
  occurrenceCount: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentParentQuestionRequestEvent {
  type: "PARENT_QUESTION_REQUEST";
  requestId: string;
  sourceName: string;
  controller: InlineHPDAgentParentQuestionRequestEventController;
  questions: Array<InlineHPDAgentParentQuestionRequestEventQuestionsItem>;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPermissionEvaluatedEvent {
  type: "PERMISSION_EVALUATED";
  permissionId: string;
  callId: string;
  agentId: string;
  domain: string;
  authority: string;
  action?: string | null;
  policyDescriptorId: string;
  outcomeKind: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPermissionRequestEvent {
  type: "PERMISSION_REQUEST";
  permissionId: string;
  callId: string;
  sourceName: string;
  permissionDomain: string;
  authority?: string | null;
  action?: string | null;
  permissionResource?: InlineHPDAgentPermissionRequestEventPermissionResource | null;
  reason?: string | null;
  risk?: string | null;
  choices: Array<InlineHPDAgentPermissionRequestEventChoicesItem>;
  evidence?: InlineHPDAgentPermissionRequestEventEvidence | null;
  persistenceUnavailableReason?: string | null;
  functionName?: string | null;
  arguments?: unknown;
  channel?: number | null;
  kind?: number | null;
  requestId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPermissionResolvedEvent {
  type: "PERMISSION_RESOLVED";
  permissionId: string;
  callId: string;
  agentId: string;
  domain: string;
  authority?: string | null;
  action?: string | null;
  policyDescriptorId?: string | null;
  permissionResource?: InlineHPDAgentPermissionResolvedEventPermissionResource | null;
  decisionKind: string;
  choiceId?: string | null;
  decisionSource: number;
  denialReason?: string | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPermissionResponseEvent {
  type: "PERMISSION_RESPONSE";
  permissionId: string;
  sourceName: string;
  choiceId: string;
  feedback?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  requestId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentPlanUpdatedEvent {
  type: "PLAN_UPDATED";
  planId: string;
  conversationId: string;
  updateType: number;
  plan: InlineHPDAgentPlanUpdatedEventPlan;
  explanation?: string | null;
  updatedAt: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentProviderOperationUsageEvent {
  type: "PROVIDER_OPERATION_USAGE";
  messageTurnId: string;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  operationKind: number;
  family: number;
  outcome: number;
  usage?: InlineHPDAgentProviderOperationUsageEventUsage | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentProviderValuationObservationEvent {
  type: "PROVIDER_VALUATION_OBSERVATION";
  messageTurnId: string;
  sourceEventId: string;
  observation: InlineHPDAgentProviderValuationObservationEventObservationAnyOf0;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentQuestionResponseEvent {
  type: "QUESTION_RESPONSE";
  requestId: string;
  sourceName: string;
  outcome: string;
  answers: Array<InlineHPDAgentQuestionResponseEventAnswersItem>;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentReasoningDeltaEvent {
  type: "REASONING_DELTA";
  text: string;
  messageId: string;
  protectedData?: string | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentReasoningMessageEndEvent {
  type: "REASONING_MESSAGE_END";
  messageId: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentReasoningMessageStartEvent {
  type: "REASONING_MESSAGE_START";
  messageId: string;
  role: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentRequestEventProcessedEvent {
  type: "REQUEST_EVENT_PROCESSED";
  agentName: string;
  eventType: string;
  requiresResponse: boolean;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp: string;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentRuntimeAgentDurableEvent {
  type: "RUNTIME_AGENT_DURABLE_EVENT";
  extensionNamespace: string;
  schemaName: string;
  schemaVersion: number;
  schemaDigest: string;
  payload: unknown;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentRuntimeAgentInputEvent {
  type: "RUNTIME_AGENT_INPUT";
  extensionNamespace: string;
  schemaName: string;
  schemaVersion: number;
  schemaDigest: string;
  payload: unknown;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentRuntimeAgentLiveEvent {
  type: "RUNTIME_AGENT_LIVE_EVENT";
  extensionNamespace: string;
  schemaName: string;
  schemaVersion: number;
  schemaDigest: string;
  payload: unknown;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillActivatedEvent {
  type: "SKILL_ACTIVATED";
  capabilityId: InlineHPDAgentSkillActivatedEventCapabilityId;
  name: string;
  revealedCapabilityCount: number;
  lifetime: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillActivationFailedEvent {
  type: "SKILL_ACTIVATION_FAILED";
  capabilityId: InlineHPDAgentSkillActivationFailedEventCapabilityId;
  name: string;
  errorType: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillActivationStartedEvent {
  type: "SKILL_ACTIVATION_STARTED";
  capabilityId: InlineHPDAgentSkillActivationStartedEventCapabilityId;
  name: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillResourceReadCompletedEvent {
  type: "SKILL_RESOURCE_READ_COMPLETED";
  capabilityId: InlineHPDAgentSkillResourceReadCompletedEventCapabilityId;
  name: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillResourceReadFailedEvent {
  type: "SKILL_RESOURCE_READ_FAILED";
  capabilityId: InlineHPDAgentSkillResourceReadFailedEventCapabilityId;
  name: string;
  errorType: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillResourceReadStartedEvent {
  type: "SKILL_RESOURCE_READ_STARTED";
  capabilityId: InlineHPDAgentSkillResourceReadStartedEventCapabilityId;
  name: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillScriptCompletedEvent {
  type: "SKILL_SCRIPT_COMPLETED";
  capabilityId: InlineHPDAgentSkillScriptCompletedEventCapabilityId;
  name: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillScriptFailedEvent {
  type: "SKILL_SCRIPT_FAILED";
  capabilityId: InlineHPDAgentSkillScriptFailedEventCapabilityId;
  name: string;
  category: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillScriptStartedEvent {
  type: "SKILL_SCRIPT_STARTED";
  capabilityId: InlineHPDAgentSkillScriptStartedEventCapabilityId;
  name: string;
  runner: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSkillScriptTimedOutEvent {
  type: "SKILL_SCRIPT_TIMED_OUT";
  capabilityId: InlineHPDAgentSkillScriptTimedOutEventCapabilityId;
  name: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentStateSnapshotEvent {
  type: "STATE_SNAPSHOT";
  currentIteration: number;
  maxIterations: number;
  isTerminated: boolean;
  terminationReason?: string | null;
  consecutiveErrorCount: number;
  completedFunctions: Array<string>;
  agentName: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentStructuredOutputCompleteEvent {
  type: "STRUCTURED_OUTPUT_COMPLETE";
  messageId: string;
  outputTypeName: string;
  totalParseAttempts: number;
  finalJsonLength: number;
  duration: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentStructuredOutputErrorEvent {
  type: "STRUCTURED_OUTPUT_ERROR";
  rawJson: string;
  errorMessage: string;
  expectedTypeName: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentStructuredOutputPartialEvent {
  type: "STRUCTURED_OUTPUT_PARTIAL";
  messageId: string;
  outputTypeName: string;
  parseAttempt: number;
  accumulatedJsonLength: number;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentStructuredOutputStartEvent {
  type: "STRUCTURED_OUTPUT_START";
  messageId: string;
  outputTypeName: string;
  outputMode: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentChildControllerAuthorityEvent {
  type: "SUBAGENT_CHILD_CONTROLLER_AUTHORITY";
  controller: InlineHPDAgentSubAgentChildControllerAuthorityEventController;
  localId: InlineHPDAgentSubAgentChildControllerAuthorityEventLocalId;
  forkOperationId: string;
  forkOperationSource: InlineHPDAgentSubAgentChildControllerAuthorityEventForkOperationSource;
  revoked: boolean;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentChildDetachedEvent {
  type: "SUBAGENT_CHILD_DETACHED";
  localId: InlineHPDAgentSubAgentChildDetachedEventLocalId;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentChildRegisteredEvent {
  type: "SUBAGENT_CHILD_REGISTERED";
  child: InlineHPDAgentSubAgentChildRegisteredEventChild;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentChildRemappedEvent {
  type: "SUBAGENT_CHILD_REMAPPED";
  localId: InlineHPDAgentSubAgentChildRemappedEventLocalId;
  childThread: InlineHPDAgentSubAgentChildRemappedEventChildThread;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentChildUnavailableEvent {
  type: "SUBAGENT_CHILD_UNAVAILABLE";
  localId: InlineHPDAgentSubAgentChildUnavailableEventLocalId;
  availability: number;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentContextReceivedEvent {
  type: "SUBAGENT_CONTEXT_RECEIVED";
  messageId: string;
  text: string;
  source: InlineHPDAgentSubAgentContextReceivedEventSource;
  sourceCursor: InlineHPDAgentSubAgentContextReceivedEventSourceCursor;
  formatVersion?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentControllerGrantedEvent {
  type: "SUBAGENT_CONTROLLER_GRANTED";
  localId: InlineHPDAgentSubAgentControllerGrantedEventLocalId;
  childThread: InlineHPDAgentSubAgentControllerGrantedEventChildThread;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentCreationAdvancedEvent {
  type: "SUBAGENT_CREATION_ADVANCED";
  record: InlineHPDAgentSubAgentCreationAdvancedEventRecord;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentCreationReservedEvent {
  type: "SUBAGENT_CREATION_RESERVED";
  record: InlineHPDAgentSubAgentCreationReservedEventRecord;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentExecutionControllerEvent {
  type: "SUBAGENT_EXECUTION_CONTROLLER";
  executionId: string;
  controller: InlineHPDAgentSubAgentExecutionControllerEventController;
  operationId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentInvocationCancelledEvent {
  type: "SUBAGENT_INVOCATION_CANCELLED";
  invocationId: string;
  reason?: string | null;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentInvocationCompletedEvent {
  type: "SUBAGENT_INVOCATION_COMPLETED";
  invocationId: string;
  summary?: string | null;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentInvocationFailedEvent {
  type: "SUBAGENT_INVOCATION_FAILED";
  invocationId: string;
  errorType: string;
  message: string;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentInvocationStartedEvent {
  type: "SUBAGENT_INVOCATION_STARTED";
  invocationId: string;
  parentToolCallId: string;
  childAgentId: string;
  childSessionId: string;
  childThreadId: string;
  roleName: string;
  contextPolicy: number;
  mode: unknown;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentQuestionRaisedEvent {
  type: "SUBAGENT_QUESTION_RAISED";
  requestId: string;
  child: InlineHPDAgentSubAgentQuestionRaisedEventChild;
  childExecutionId: string;
  questions: Array<InlineHPDAgentSubAgentQuestionRaisedEventQuestionsItem>;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentRegistrySeedEvent {
  type: "SUBAGENT_REGISTRY_SEED";
  entries: Array<InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf1>;
  pendingCreations: Array<InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItem>;
  controllerGrants?: Array<InlineHPDAgentSubAgentRegistrySeedEventControllerGrantsItem> | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentSubAgentResultSubmittedEvent {
  type: "SUBAGENT_RESULT_SUBMITTED";
  executionId: string;
  callId: string;
  controller: InlineHPDAgentSubAgentResultSubmittedEventController;
  report: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentTextDeltaEvent {
  type: "TEXT_DELTA";
  text: string;
  messageId: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentTextMessageEndEvent {
  type: "TEXT_MESSAGE_END";
  messageId: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentTextMessageStartEvent {
  type: "TEXT_MESSAGE_START";
  messageId: string;
  role: string;
  source?: number | null;
  visibility?: number | null;
  persistence?: number | null;
  authorName?: string | null;
  createdAt?: string | null;
  clientInputId?: string | null;
  additionalProperties?: unknown;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadCreatedEvent {
  type: "THREAD_CREATED";
  defaultAgentId: string;
  name?: string | null;
  description?: string | null;
  tags?: Array<string> | null;
  threadMetadata?: unknown;
  createdAt: string;
  threadKind?: number | null;
  visibility?: number | null;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  invocationId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  contextPolicy?: string | null;
  forkedFrom?: string | null;
  forkedAtMessageId?: string | null;
  forkedAtMessageIndex?: number | null;
  childThreads?: Array<string> | null;
  ancestors?: Record<string, string> | null;
  preparation?: InlineHPDAgentThreadCreatedEventPreparation | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadExecutionFinishedEvent {
  type: "THREAD_EXECUTION_FINISHED";
  threadExecutionId: string;
  agentId: string;
  outcome: string;
  finishedAt: string;
  error?: InlineHPDAgentThreadExecutionFinishedEventError | null;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadExecutionStartedEvent {
  type: "THREAD_EXECUTION_STARTED";
  threadExecutionId: string;
  agentId: string;
  startedAt: string;
  kind?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadForkOperationChangedEvent {
  type: "THREAD_FORK_OPERATION_CHANGED";
  operation: InlineHPDAgentThreadForkOperationChangedEventOperation;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadHistoryCompactionCheckpointEvent {
  type: "THREAD_HISTORY_COMPACTION_CHECKPOINT";
  compactionId: string;
  point: InlineHPDAgentThreadHistoryCompactionCheckpointEventPoint;
  preservation: InlineHPDAgentThreadHistoryCompactionCheckpointEventPreservation;
  compactedMessageIds: Array<string>;
  preservedMessageIds: Array<string>;
  carriedUserMessageSourceIds: Array<string>;
  afterPointMessageIds: Array<string>;
  replacementMessages: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItem>;
  strategy: InlineHPDAgentThreadHistoryCompactionCheckpointEventStrategy;
  commitMode: number;
  compactedAt: string;
  summarizer?: InlineHPDAgentThreadHistoryCompactionCheckpointEventSummarizer | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadMessageReplacedEvent {
  type: "THREAD_MESSAGE_REPLACED";
  messageId: string;
  replacement: InlineHPDAgentThreadMessageReplacedEventReplacement;
  reason: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadMiddlewareStateCommittedEvent {
  type: "THREAD_MIDDLEWARE_STATE_COMMITTED";
  state: Record<string, string>;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentThreadUpdatedEvent {
  type: "THREAD_UPDATED";
  defaultAgentId: string;
  name?: string | null;
  description?: string | null;
  tags?: Array<string> | null;
  threadMetadata?: unknown;
  threadKind?: number | null;
  visibility?: number | null;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  invocationId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  contextPolicy?: string | null;
  forkedFrom?: string | null;
  forkedAtMessageId?: string | null;
  forkedAtMessageIndex?: number | null;
  childThreads?: Array<string> | null;
  ancestors?: Record<string, string> | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolBodyOperationCommittedFailureEvent {
  type: "TOOL_BODY_OPERATION_COMMITTED_FAILURE";
  kind?: number | null;
  invocation: InlineHPDAgentToolBodyOperationCommittedFailureEventInvocation;
  committedOperation: InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperation;
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolCallArgsEvent {
  type: "TOOL_CALL_ARGS";
  callId: string;
  argsJson: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolCallEndEvent {
  type: "TOOL_CALL_END";
  callId: string;
  messageId: string;
  name: string;
  argsJson: string;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolCallResultEvent {
  type: "TOOL_CALL_RESULT";
  callId: string;
  result: InlineHPDAgentToolCallResultEventResult;
  toolHarnessName?: string | null;
  callType?: string | null;
  name?: string | null;
  kind?: number | null;
  messageId?: string | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolCallStartEvent {
  type: "TOOL_CALL_START";
  callId: string;
  name: string;
  messageId: string;
  toolHarnessName?: string | null;
  callType?: string | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolOutputProjectedEvent {
  type: "TOOL_OUTPUT_PROJECTED";
  functionName?: string | null;
  callId: string;
  contentId: string;
  contentKind: string;
  partCount: number;
  bytesRetained: number;
  storageCapability: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentToolOutputProjectionFailedEvent {
  type: "TOOL_OUTPUT_PROJECTION_FAILED";
  functionName?: string | null;
  callId: string;
  partName?: string | null;
  code: string;
  errorMessage: string;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentTotalErrorThresholdExceededEvent {
  type: "TOTAL_ERROR_THRESHOLD_EXCEEDED";
  agentName: string;
  totalErrorCount: number;
  maxTotalErrors: number;
  iteration: number;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentUserAudioTranscriptCompletedEvent {
  type: "USER_AUDIO_TRANSCRIPT_COMPLETED";
  text: string;
  messageId: string;
  providerItemId?: string | null;
  contentIndex?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentUserAudioTranscriptDeltaEvent {
  type: "USER_AUDIO_TRANSCRIPT_DELTA";
  text: string;
  messageId: string;
  providerItemId?: string | null;
  contentIndex?: number | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentUserAudioTranscriptFailedEvent {
  type: "USER_AUDIO_TRANSCRIPT_FAILED";
  messageId: string;
  errorMessage: string;
  providerItemId?: string | null;
  contentIndex?: number | null;
  error?: InlineHPDAgentUserAudioTranscriptFailedEventError | null;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentUserMessageEvent {
  type: "USER_MESSAGE";
  text: string;
  messageId: string;
  channel?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HPDAgentUserMessagesInputEvent {
  type: "USER_MESSAGES_INPUT";
  messages?: Array<InlineHPDAgentUserMessagesInputEventMessagesItem> | null;
  delivery?: string | null;
  clientInputId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  agentId?: string | null;
  runConfig?: AgentRunConfig | null;
  subAgentRunConfig?: SubAgentRunConfig | null;
  threadExecutionId?: string | null;
}

export interface HPDAgentUserQuestionRequestEvent {
  type: "USER_QUESTION_REQUEST";
  requestId: string;
  sourceName: string;
  questions: Array<InlineHPDAgentUserQuestionRequestEventQuestionsItem>;
  channel?: number | null;
  kind?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: AgentMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface HostedFilesClientConfig {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface HttpValidationProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, Array<string>> | null;
}

export interface ImageGenerationClientConfig {
  count?: number | null;
  imageSize?: null | ImageSizeRunConfig | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface ImageSizeRunConfig {
  width?: number | null;
  height?: number | null;
}

export interface InlineAgentRunConfigAudio {
  enabled?: boolean | null;
  inputMode?: number | null;
  outputMode?: number | null;
  assistantOutputMode?: number | null;
  pacing?: InlineAgentRunConfigAudioPacing | null;
  progressiveRouteMode?: number | null;
  pushTextAggregationMode?: number | null;
  artifactCapturePolicy?: number | null;
  contentType?: string | null;
  enablePlayback?: boolean | null;
}

export interface InlineAgentRunConfigAudioPacing {
  mode?: number | null;
  first?: InlineAgentRunConfigAudioPacingFirst | null;
  continuation?: InlineAgentRunConfigAudioPacingContinuation | null;
  boundaries?: InlineAgentRunConfigAudioPacingBoundaries | null;
  filtering?: InlineAgentRunConfigAudioPacingFiltering | null;
}

export interface InlineAgentRunConfigAudioPacingBoundaries {
  requireLookahead?: boolean | null;
  protectDecimals?: boolean | null;
  protectAbbreviations?: boolean | null;
  protectInitials?: boolean | null;
  protectUrls?: boolean | null;
  protectEllipses?: boolean | null;
}

export interface InlineAgentRunConfigAudioPacingContinuation {
  maxCharacters?: number | null;
  preferSentenceBoundaries?: boolean | null;
  allowPhraseBoundaries?: boolean | null;
  maxInFlightSynthesisRequests?: number | null;
}

export interface InlineAgentRunConfigAudioPacingFiltering {
  enabled?: boolean | null;
  removeCodeBlocks?: boolean | null;
  removeMarkdownTables?: boolean | null;
  stripMarkdownFormatting?: boolean | null;
  simplifyLinks?: boolean | null;
  normalizeWhitespace?: boolean | null;
  collapseRepeatedPunctuation?: boolean | null;
  emojiPolicy?: number | null;
}

export interface InlineAgentRunConfigAudioPacingFirst {
  minCharacters?: number | null;
  maxCharacters?: number | null;
  emitFirstSafeSentenceImmediately?: boolean | null;
}

export interface InlineAgentRunConfigBackgroundResponses {
  allow?: boolean | null;
  pollingInterval?: string | null;
  timeout?: string | null;
}

export interface InlineAgentRunConfigClients {
  transport?: number | null;
  chat?: InlineAgentRunConfigClientsChat | null;
  textToSpeech?: InlineAgentRunConfigClientsTextToSpeech | null;
  speechToText?: InlineAgentRunConfigClientsSpeechToText | null;
  realtime?: InlineAgentRunConfigClientsRealtime | null;
  imageGeneration?: InlineAgentRunConfigClientsImageGeneration | null;
  embeddings?: InlineAgentRunConfigClientsEmbeddings | null;
  hostedFiles?: InlineAgentRunConfigClientsHostedFiles | null;
  voiceActivity?: InlineAgentRunConfigClientsVoiceActivity | null;
  endOfTurn?: InlineAgentRunConfigClientsEndOfTurn | null;
}

export interface InlineAgentRunConfigClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string | null> | null;
  reasoning?: InlineAgentRunConfigClientsChatReasoning | null;
  provider?: InlineAgentRunConfigClientsChatProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsChatProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf0 | InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf1 | InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf2 | InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf3 | null;
}

export interface InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf0 {
  type?: unknown;
  secretKey: string;
}

export interface InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf1 {
  type?: unknown;
  accountId: string;
  scopes?: Array<string | null> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf2 {
  type?: unknown;
  credentialName: string;
}

export interface InlineAgentRunConfigClientsChatProviderAuthenticationAnyOf3 {
  type?: unknown;
}

export interface InlineAgentRunConfigClientsChatReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineAgentRunConfigClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineAgentRunConfigClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsEndOfTurn {
  provider?: InlineAgentRunConfigClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineAgentRunConfigClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineAgentRunConfigClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineAgentRunConfigClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineAgentRunConfigClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsRealtime {
  outputAudioFormat?: InlineAgentRunConfigClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string | null> | null;
  transcription?: InlineAgentRunConfigClientsRealtimeTranscription | null;
  provider?: InlineAgentRunConfigClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineAgentRunConfigClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineAgentRunConfigClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineAgentRunConfigClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineAgentRunConfigClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigClientsVoiceActivity {
  provider?: InlineAgentRunConfigClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string | null> | null;
}

export interface InlineAgentRunConfigClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineAgentRunConfigCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string | null> | null;
}

export interface InlineAgentRunConfigCompaction {
  automatic?: InlineAgentRunConfigCompactionAutomatic | null;
}

export interface InlineAgentRunConfigCompactionAutomatic {
  trigger: InlineAgentRunConfigCompactionAutomaticTriggerAnyOf0 | InlineAgentRunConfigCompactionAutomaticTriggerAnyOf1 | InlineAgentRunConfigCompactionAutomaticTriggerAnyOf2;
  compaction: InlineAgentRunConfigCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompaction {
  point: InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf0 | InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf1 | InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf2;
  preservation?: InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf0 | InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf1 | InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2 | null;
  strategy: InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf0 | InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf0 {
  $type?: unknown;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf1 {
  $type?: unknown;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPointAnyOf2 {
  $type?: unknown;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf0 {
  $type?: unknown;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf1 {
  $type?: unknown;
  count: number;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2 {
  $type?: unknown;
  limit: InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 | InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: unknown;
  count: number;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: unknown;
  tokens: number;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf0 {
  $type?: unknown;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1 {
  $type?: unknown;
  summarizer?: InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineAgentRunConfigCompactionAutomaticCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineAgentRunConfigCompactionAutomaticTriggerAnyOf0 {
  $type?: unknown;
  turns: number;
}

export interface InlineAgentRunConfigCompactionAutomaticTriggerAnyOf1 {
  $type?: unknown;
  inputTokens: number;
}

export interface InlineAgentRunConfigCompactionAutomaticTriggerAnyOf2 {
  $type?: unknown;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineAgentRunConfigContext {
  properties?: unknown;
}

export interface InlineAgentRunConfigGoals {
  toolAccess?: string | null;
  policies?: InlineAgentRunConfigGoalsPolicies | null;
}

export interface InlineAgentRunConfigGoalsPolicies {
  completion?: string | null;
  blocker?: string | null;
  continuation?: string | null;
  accounting?: string | null;
}

export interface InlineAgentRunConfigHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string | null> | null;
}

export interface InlineAgentRunConfigSecurity {
  approval?: number | null;
  sandbox?: InlineAgentRunConfigSecuritySandbox | null;
  permissionOverrides?: Array<InlineAgentRunConfigSecurityPermissionOverridesItem | null> | null;
}

export interface InlineAgentRunConfigSecurityPermissionOverridesItem {
  selector: InlineAgentRunConfigSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineAgentRunConfigSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineAgentRunConfigSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineAgentRunConfigSecuritySandboxCapabilities | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilities {
  filesystem?: Array<InlineAgentRunConfigSecuritySandboxCapabilitiesFilesystemItem | null> | null;
  network?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineAgentRunConfigSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string | null> | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkAllowedDomainsItem | null> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineAgentRunConfigStreaming {
  coalesceDeltas?: boolean | null;
}

export interface InlineAgentRunConfigStructuredOutput {
  mode?: string | null;
  schema?: unknown;
  schemaName?: string | null;
  schemaDescription?: string | null;
  toolName?: string | null;
  streamPartials?: boolean | null;
  partialDebounceMs?: number | null;
}

export interface InlineAgentRunConfigSystemInstructions {
  override?: string | null;
  append?: string | null;
}

export interface InlineAgentRunConfigToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineAgentRunConfigToolOutputsRetrievalLimits | null;
}

export interface InlineAgentRunConfigToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineAgentRunConfigTools {
  clientInput?: InlineAgentRunConfigToolsClientInput | null;
}

export interface InlineAgentRunConfigToolsClientInput {
  clientToolHarnesses?: Array<InlineAgentRunConfigToolsClientInputClientToolHarnessesItem | null> | null;
  expandedContainers?: Array<string | null> | null;
  hiddenTools?: Array<string | null> | null;
  context?: Array<InlineAgentRunConfigToolsClientInputContextItem | null> | null;
  state?: unknown;
  metadata?: unknown;
  resetClientState?: boolean | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItem {
  name: string;
  description: string | null;
  tools: Array<InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItem | null>;
  skills?: Array<InlineAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItem | null> | null;
  functionResult?: string | null;
  systemPrompt?: string | null;
  startCollapsed?: boolean | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItem {
  name: string;
  description: string;
  instructions: string;
  reinforcement?: string | null;
  references?: Array<InlineAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItemReferencesItem | null> | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItemReferencesItem {
  toolName: string;
  toolsetName?: string | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItem {
  name: string;
  description: string;
  parametersSchema: unknown;
  defaultPolicy?: InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicy | null;
  operationContract?: InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContract | null;
  metadata?: unknown;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicy {
  permission?: InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyOperationNotification | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContract {
  discriminator: string;
  actions: Record<string, InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty | null>;
}

export interface InlineAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty {
  permission?: unknown;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: unknown;
}

export interface InlineAgentRunConfigToolsClientInputContextItem {
  description: string;
  value: unknown;
  key?: string | null;
  effectiveKey?: string | null;
  valueText?: string | null;
}

export interface InlineHPDAgentAgentOperationNotificationInputEventNotificationsItem {
  notificationId: string;
  operationId: string;
  name: string;
  providerStatus: string;
  summary?: string | null;
  result?: InlineHPDAgentAgentOperationNotificationInputEventNotificationsItemResult | null;
  sourceThreadExecutionId?: string | null;
}

export interface InlineHPDAgentAgentOperationNotificationInputEventNotificationsItemResult {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineHPDAgentAgentOperationNotificationQueuedEventNotification {
  notificationId: string;
  operationId: string;
  name: string;
  providerStatus: string;
  summary?: string | null;
  result?: InlineHPDAgentAgentOperationNotificationQueuedEventNotificationResult | null;
  sourceThreadExecutionId?: string | null;
}

export interface InlineHPDAgentAgentOperationNotificationQueuedEventNotificationResult {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperation {
  operationId: string;
  providerOperationId?: string | null;
  sourceKind: number;
  name: string;
  address: InlineHPDAgentAgentOperationRegisteredEventOperationAddress;
  originatingThreadExecutionId?: string | null;
  invocation?: InlineHPDAgentAgentOperationRegisteredEventOperationInvocation | null;
  providerStatus: number;
  observationStatus: number;
  control: InlineHPDAgentAgentOperationRegisteredEventOperationControl;
  notification: InlineHPDAgentAgentOperationRegisteredEventOperationNotification;
  registeredAt: string;
  startedAt?: string | null;
  updatedAt: string;
  finishedAt?: string | null;
  completion?: InlineHPDAgentAgentOperationRegisteredEventOperationCompletion | null;
  failure?: InlineHPDAgentAgentOperationRegisteredEventOperationFailure | null;
  recovery?: InlineHPDAgentAgentOperationRegisteredEventOperationRecovery | null;
  version: number;
  metadata?: Record<string, string> | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationAddress {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationCompletion {
  summary?: string | null;
  artifactReferences?: Array<string> | null;
  resultPayload?: InlineHPDAgentAgentOperationRegisteredEventOperationCompletionResultPayload | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationCompletionResultPayload {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationControl {
  handleId?: string | null;
  kind: number;
  capabilities: number;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationFailure {
  code: string;
  message: string;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationInvocation {
  agentName: string;
  functionCallId: string;
  functionName: string;
  conversationId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  traceId?: string | null;
  threadExecutionId?: string | null;
  invocation?: InlineHPDAgentAgentOperationRegisteredEventOperationInvocationInvocation | null;
  batchId?: string | null;
  toolCallIndex?: number | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationInvocationInvocation {
  batchId: string;
  callId: string;
  functionName?: string | null;
  toolCallIndex: number;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentAgentOperationRegisteredEventOperationRecovery {
  kind: string;
  protectedReference: string;
}

export interface InlineHPDAgentAgentOperationTombstonedEventTombstone {
  operationId: string;
  address: InlineHPDAgentAgentOperationTombstonedEventTombstoneAddress;
  providerDeduplicationKeys: Array<string>;
  providerStatus: number;
  finishedAt: string;
  finalVersion: number;
}

export interface InlineHPDAgentAgentOperationTombstonedEventTombstoneAddress {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperation {
  operationId: string;
  providerOperationId?: string | null;
  sourceKind: number;
  name: string;
  address: InlineHPDAgentAgentOperationTransitionedEventOperationAddress;
  originatingThreadExecutionId?: string | null;
  invocation?: InlineHPDAgentAgentOperationTransitionedEventOperationInvocation | null;
  providerStatus: number;
  observationStatus: number;
  control: InlineHPDAgentAgentOperationTransitionedEventOperationControl;
  notification: InlineHPDAgentAgentOperationTransitionedEventOperationNotification;
  registeredAt: string;
  startedAt?: string | null;
  updatedAt: string;
  finishedAt?: string | null;
  completion?: InlineHPDAgentAgentOperationTransitionedEventOperationCompletion | null;
  failure?: InlineHPDAgentAgentOperationTransitionedEventOperationFailure | null;
  recovery?: InlineHPDAgentAgentOperationTransitionedEventOperationRecovery | null;
  version: number;
  metadata?: Record<string, string> | null;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationAddress {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationCompletion {
  summary?: string | null;
  artifactReferences?: Array<string> | null;
  resultPayload?: InlineHPDAgentAgentOperationTransitionedEventOperationCompletionResultPayload | null;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationCompletionResultPayload {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationControl {
  handleId?: string | null;
  kind: number;
  capabilities: number;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationFailure {
  code: string;
  message: string;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationInvocation {
  agentName: string;
  functionCallId: string;
  functionName: string;
  conversationId?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  traceId?: string | null;
  threadExecutionId?: string | null;
  invocation?: InlineHPDAgentAgentOperationTransitionedEventOperationInvocationInvocation | null;
  batchId?: string | null;
  toolCallIndex?: number | null;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationInvocationInvocation {
  batchId: string;
  callId: string;
  functionName?: string | null;
  toolCallIndex: number;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentAgentOperationTransitionedEventOperationRecovery {
  kind: string;
  protectedReference: string;
}

export interface InlineHPDAgentAgentTurnCapabilitiesPinnedEventIdentity {
  agentEpoch: number;
  overlayRevision: string;
  effectiveSnapshotId: string;
  sourceRevisions: Array<InlineHPDAgentAgentTurnCapabilitiesPinnedEventIdentitySourceRevisionsItem>;
}

export interface InlineHPDAgentAgentTurnCapabilitiesPinnedEventIdentitySourceRevisionsItem {
  sourceId: string;
  revision: number;
}

export interface InlineHPDAgentAgentTurnFinishedEventUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf0 {
  type?: string | null;
  bindings?: InlineHPDAgentAudioSessionInputEventCommandAnyOf0Bindings | null;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf0Bindings {
  bindings?: Array<InlineHPDAgentAudioSessionInputEventCommandAnyOf0BindingsBindingsItem> | null;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf0BindingsBindingsItem {
  componentInstance: string;
  schema: string;
  version: number;
  value: unknown;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf1 {
  type?: string | null;
  audioSessionId: string;
  expectedRevision: number;
  patch: InlineHPDAgentAudioSessionInputEventCommandAnyOf1Patch;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf1Patch {
  bindings?: Array<unknown> | null;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf2 {
  type?: string | null;
  audioSessionId: string;
  candidateId: string;
  expectedRevision: number;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf3 {
  type?: string | null;
  audioSessionId: string;
  enabled: boolean;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf4 {
  type?: string | null;
  audioSessionId: string;
  enabled: boolean;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf5 {
  type?: string | null;
  audioSessionId: string;
  expectedRevision: number;
  operationId: string;
}

export interface InlineHPDAgentAudioSessionInputEventCommandAnyOf6 {
  type?: string | null;
  audioSessionId: string;
  reason?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentation {
  injectToolHarnesses?: Array<InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItem> | null;
  removeToolHarnesses?: Array<string> | null;
  expandToolHarnesses?: Array<string> | null;
  collapseToolHarnesses?: Array<string> | null;
  hideTools?: Array<string> | null;
  showTools?: Array<string> | null;
  addContext?: Array<InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationAddContextItem> | null;
  removeContext?: Array<string> | null;
  updateState?: unknown;
  patchState?: unknown;
  hasChanges?: boolean | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationAddContextItem {
  description: string;
  value: unknown;
  key?: string | null;
  effectiveKey?: string | null;
  valueText?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItem {
  name: string;
  description?: string | null;
  tools: Array<InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItem>;
  skills?: Array<InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemSkillsItem> | null;
  functionResult?: string | null;
  systemPrompt?: string | null;
  startCollapsed?: boolean | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemSkillsItem {
  name: string;
  description: string;
  instructions: string;
  reinforcement?: string | null;
  references?: Array<InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemSkillsItemReferencesItem> | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemSkillsItemReferencesItem {
  toolName: string;
  toolsetName?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItem {
  name: string;
  description: string;
  parametersSchema: unknown;
  defaultPolicy?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicy | null;
  operationContract?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContract | null;
  metadata?: unknown;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicy {
  permission?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyOperationNotification | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContract {
  discriminator: string;
  actions: Record<string, InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty>;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty {
  permission?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventError {
  kind: string;
  message: string;
  retryable?: boolean | null;
  details?: Array<unknown> | null;
  currentContext?: InlineHPDAgentClientToolsClientToolInvokeOutcomeEventErrorCurrentContext | null;
  metadata?: unknown;
}

export interface InlineHPDAgentClientToolsClientToolInvokeOutcomeEventErrorCurrentContext {
  workspaceId?: string | null;
  documentId?: string | null;
  documentName?: string | null;
  pageId?: string | null;
  fileId?: string | null;
  projectId?: string | null;
  sceneId?: string | null;
  activeView?: string | null;
  selectionSummary?: string | null;
  appStateVersion?: string | null;
  metadata?: unknown;
}

export interface InlineHPDAgentClientToolsClientToolInvokeRequestEventArguments {
}

export interface InlineHPDAgentClientToolsClientToolInvokeRequestEventTarget {
  responderId?: string | null;
  responderGroup?: string | null;
  requiredCapabilities?: Array<string> | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentation {
  injectToolHarnesses?: Array<InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItem> | null;
  removeToolHarnesses?: Array<string> | null;
  expandToolHarnesses?: Array<string> | null;
  collapseToolHarnesses?: Array<string> | null;
  hideTools?: Array<string> | null;
  showTools?: Array<string> | null;
  addContext?: Array<InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationAddContextItem> | null;
  removeContext?: Array<string> | null;
  updateState?: unknown;
  patchState?: unknown;
  hasChanges?: boolean | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationAddContextItem {
  description: string;
  value: unknown;
  key?: string | null;
  effectiveKey?: string | null;
  valueText?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItem {
  name: string;
  description: string | null;
  tools: Array<InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItem>;
  skills?: Array<InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemSkillsItem> | null;
  functionResult?: string | null;
  systemPrompt?: string | null;
  startCollapsed?: boolean | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemSkillsItem {
  name: string;
  description: string;
  instructions: string;
  reinforcement?: string | null;
  references?: Array<InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemSkillsItemReferencesItem> | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemSkillsItemReferencesItem {
  toolName: string;
  toolsetName?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItem {
  name: string;
  description: string;
  parametersSchema: unknown;
  defaultPolicy?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicy | null;
  operationContract?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContract | null;
  metadata?: unknown;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicy {
  permission?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyOperationNotification | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemDefaultPolicyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContract {
  discriminator: string;
  actions: Record<string, InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty>;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty {
  permission?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineHPDAgentClientToolsClientToolOperationOutcomeEventAugmentationInjectToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineHPDAgentCompactThreadInputEventRequest {
  compaction: InlineHPDAgentCompactThreadInputEventRequestCompaction;
  continuation?: number | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompaction {
  point: InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf0 | InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf1 | InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf2;
  preservation?: InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf0 | InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf1 | InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf0 | InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentCompactThreadInputEventRequestCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentCompactionEventSummarizer {
  provider?: string | null;
  backend?: string | null;
  model?: string | null;
  responseModel?: string | null;
  promptRevision: string;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentContentAddedEventContentAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf0 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf1 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf2 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf4 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf0 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf1 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf2 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf3 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf4 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf5 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf6 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf7 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf8 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf9 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf10 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf11 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf12 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf13 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf14 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf16 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf17 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf18 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf19 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf20 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf21 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf22 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf23 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf24> | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf14 {
  $type: string;
  toolCall: unknown;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf3InputsItemAnyOf9 {
  $type: string;
  details: unknown;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: unknown;
  reason?: string | null;
  requestId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf17 {
  $type: string;
  outputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf19 {
  $type: string;
  outputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf21 {
  $type: string;
  outputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf23 {
  $type: string;
  outputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf24 {
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf9 {
  $type: string;
  details: InlineHPDAgentContentAddedEventContentAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentContentAddedEventContentAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentFunctionInvocationAuditedEventInvocation {
  functionName: string;
  functionCallId: string;
  action?: string | null;
  requestedMode?: unknown;
  resolvedMode: unknown;
  policy: unknown;
  handling: number;
  ingressProvenance: number;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalAwaitingInputEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalAwaitingInputEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalAwaitingInputEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalAwaitingInputEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalAwaitingInputEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalAwaitingInputEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalBlockedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalBlockedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalBlockedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalBlockedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalBlockedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalBlockerRejectedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalBlockerRejectedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalBlockerRejectedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalBlockerRejectedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalBlockerRejectedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerRejectedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalBlockerReportedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalBlockerReportedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalBlockerReportedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalBlockerReportedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalBlockerReportedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalBlockerReportedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalClearedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalClearedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalClearedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalClearedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalClearedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalClearedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventAcceptedProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalCompletedEventAcceptedProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventAcceptedProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalCompletedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalCompletedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalCompletedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalCompletedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalCompletedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalCompletionProposedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalCompletionProposedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalCompletionProposedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalCompletionProposedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalCompletionProposedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionProposedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalCompletionRejectedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalCompletionRejectedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalCompletionRejectedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalCompletionRejectedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalCompletionRejectedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalCompletionRejectedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalContinuationScheduledEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalContinuationScheduledEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalContinuationScheduledEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalContinuationScheduledEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalContinuationScheduledEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationScheduledEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalContinuationSkippedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalContinuationSkippedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalContinuationSkippedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalContinuationSkippedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalContinuationSkippedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationSkippedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalContinuationStartedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalContinuationStartedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalContinuationStartedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalContinuationStartedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalContinuationStartedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalContinuationStartedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalEditedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalEditedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalEditedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalEditedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalEditedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalEditedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalFaultedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalFaultedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalFaultedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalFaultedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalFaultedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalFaultedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalPausedEventCancellation {
  cause: string;
  reason?: string | null;
  source: string;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalPausedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalPausedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalPausedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalPausedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalPausedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalPausedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalProgressAccountedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalProgressAccountedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalProgressAccountedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalProgressAccountedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalProgressAccountedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalProgressAccountedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalResumedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalResumedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalResumedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalResumedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalResumedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalResumedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalStartedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalStartedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalStartedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalStartedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalStartedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalStartedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalUpdatedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalUpdatedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalUpdatedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalUpdatedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalUpdatedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalUpdatedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoal {
  goalId: string;
  objective: string;
  status: string;
  revision?: number | null;
  continuationGeneration?: number | null;
  continuation?: InlineHPDAgentGoalsGoalUsageLimitedEventGoalContinuation | null;
  accounting?: InlineHPDAgentGoalsGoalUsageLimitedEventGoalAccounting | null;
  consecutiveNoProgressExecutions?: number | null;
  completionProposal?: InlineHPDAgentGoalsGoalUsageLimitedEventGoalCompletionProposal | null;
  blocker?: InlineHPDAgentGoalsGoalUsageLimitedEventGoalBlocker | null;
  createdAt?: string | null;
  updatedAt?: string | null;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoalAccounting {
  tokensUsed?: number | null;
  usageQuality?: string | null;
  executionTime?: string | null;
  executionCount?: number | null;
  lastAccountedExecutionId?: string | null;
  lastAccountedMessageTurnId?: string | null;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoalBlocker {
  category: string;
  fingerprint: string;
  description: string;
  requiredChange: string;
  evidence: Array<string>;
  consecutiveExecutions: number;
  firstObservedAt: string;
  lastObservedAt: string;
  lastExecutionId: string;
  lastExecutionOrdinal: number;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoalCompletionProposal {
  summary: string;
  evidence: Array<InlineHPDAgentGoalsGoalUsageLimitedEventGoalCompletionProposalEvidenceItem>;
  proposedAt: string;
  executionId: string;
  remainingWork?: Array<string> | null;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoalCompletionProposalEvidenceItem {
  kind: string;
  description: string;
  reference?: string | null;
}

export interface InlineHPDAgentGoalsGoalUsageLimitedEventGoalContinuation {
  generation: number;
  expectedRevision: number;
  reservedAt: string;
  sourceExecutionId: string;
  activationOwner?: string | null;
}

export interface InlineHPDAgentMessageTurnErrorEventCancellation {
  cause: string;
  reason?: string | null;
  source: string;
}

export interface InlineHPDAgentMessageTurnErrorEventUsage {
  operations: Array<InlineHPDAgentMessageTurnErrorEventUsageOperationsItem>;
}

export interface InlineHPDAgentMessageTurnErrorEventUsageOperationsItem {
  sourceEventId: string;
  messageTurnId: string;
  threadSequenceNumber: number;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  operationKind: number;
  family: number;
  outcome: number;
  usage?: InlineHPDAgentMessageTurnErrorEventUsageOperationsItemUsage | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
}

export interface InlineHPDAgentMessageTurnErrorEventUsageOperationsItemUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentMessageTurnFinishedEventUsage {
  operations: Array<InlineHPDAgentMessageTurnFinishedEventUsageOperationsItem>;
}

export interface InlineHPDAgentMessageTurnFinishedEventUsageOperationsItem {
  sourceEventId: string;
  messageTurnId: string;
  threadSequenceNumber: number;
  operationId: string;
  logicalOperationId?: string | null;
  attempt: number;
  operationKind: number;
  family: number;
  outcome: number;
  usage?: InlineHPDAgentMessageTurnFinishedEventUsageOperationsItemUsage | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseId?: string | null;
}

export interface InlineHPDAgentMessageTurnFinishedEventUsageOperationsItemUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentMiddlewareStateChangedEventChangesItem {
  key: string;
  type: string;
  propertyName: string;
  scope: number;
  persistent: boolean;
  version: number;
  changeType: string;
  before: unknown;
  after: unknown;
  error?: string | null;
  redacted: boolean;
}

export interface InlineHPDAgentMiddlewareStateSnapshotEventStatesItem {
  key: string;
  type: string;
  propertyName: string;
  scope: number;
  persistent: boolean;
  version: number;
  json: unknown;
  error?: string | null;
  redacted: boolean;
}

export interface InlineHPDAgentParentQuestionRequestEventController {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentParentQuestionRequestEventQuestionsItem {
  id: string;
  text: string;
  header?: string | null;
  options?: Array<InlineHPDAgentParentQuestionRequestEventQuestionsItemOptionsItem> | null;
  multiple?: boolean | null;
  recommendedOptionId?: string | null;
}

export interface InlineHPDAgentParentQuestionRequestEventQuestionsItemOptionsItem {
  id: string;
  label: string;
  description?: string | null;
}

export interface InlineHPDAgentPermissionRequestEventChoicesItem {
  id: string;
  label: string;
  kind: string;
  deniedBehavior?: number | null;
}

export interface InlineHPDAgentPermissionRequestEventEvidence {
  kind: string;
  schemaVersion: string;
  payload: unknown;
}

export interface InlineHPDAgentPermissionRequestEventPermissionResource {
  value: string;
  displayName: string;
}

export interface InlineHPDAgentPermissionResolvedEventPermissionResource {
  value: string;
  displayName: string;
}

export interface InlineHPDAgentPlanUpdatedEventPlan {
  id?: string | null;
  goal?: string | null;
  steps?: Array<InlineHPDAgentPlanUpdatedEventPlanStepsItem> | null;
  createdAt?: string | null;
  contextNotes?: Array<string> | null;
  isComplete?: boolean | null;
  completedAt?: string | null;
}

export interface InlineHPDAgentPlanUpdatedEventPlanStepsItem {
  id?: string | null;
  description?: string | null;
  status?: number | null;
  notes?: string | null;
  lastUpdated?: string | null;
}

export interface InlineHPDAgentProviderOperationUsageEventUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentProviderValuationObservationEventObservationAnyOf0 {
  $type?: string | null;
  amount: number;
  currency: string;
  providerKey: string;
  responseId?: string | null;
  nativeField: string;
}

export interface InlineHPDAgentQuestionResponseEventAnswersItem {
  questionId: string;
  selectedOptionIds: Array<string>;
  customText?: string | null;
  notes?: string | null;
}

export interface InlineHPDAgentSkillActivatedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillActivationFailedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillActivationStartedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillResourceReadCompletedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillResourceReadFailedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillResourceReadStartedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillScriptCompletedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillScriptFailedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillScriptStartedEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSkillScriptTimedOutEventCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildControllerAuthorityEventController {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentChildControllerAuthorityEventForkOperationSource {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentChildControllerAuthorityEventLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildDetachedEventLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChild {
  localId: InlineHPDAgentSubAgentChildRegisteredEventChildLocalId;
  roleName: string;
  capabilityId: InlineHPDAgentSubAgentChildRegisteredEventChildCapabilityId;
  childAgentId: string;
  childThread: InlineHPDAgentSubAgentChildRegisteredEventChildChildThread;
  creationContext: number;
  creationInvocationId: string;
  parentToolCallId: string;
  executionPolicy: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicy;
  createdAt: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicy {
  contractVersion: number;
  initialRunConfig?: unknown;
  harnessOverrideLayers?: Array<Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHarnessOverrideLayersItemItem>> | null;
  handoffCompaction?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompaction | null;
  descendantDefaults?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaults | null;
  lockedClients: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClients;
  clientSources: Record<string, number>;
  authority: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthority;
  propagation: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf2;
  fingerprint: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthority {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthorityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthorityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthorityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthorityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaults {
  allowUserQuestions?: boolean | null;
  harnesses?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHarnessesItem> | null;
  toolOutputs?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsToolOutputs | null;
  context?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsContext | null;
  collapsing?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCollapsing | null;
  compaction?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompaction | null;
  handoffCompaction?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompaction | null;
  security?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurity | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompaction {
  automatic?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomatic | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomatic {
  trigger: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2;
  compaction: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction {
  point: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsContext {
  properties?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompaction {
  point: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurity {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem> | null;
  parentProxy?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompaction {
  point: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyHarnessOverrideLayersItemItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClients {
  transport?: number | null;
  chat?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsChat | null;
  textToSpeech?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsTextToSpeech | null;
  speechToText?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsSpeechToText | null;
  realtime?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtime | null;
  imageGeneration?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGeneration | null;
  embeddings?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEmbeddings | null;
  hostedFiles?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsHostedFiles | null;
  voiceActivity?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsVoiceActivity | null;
  endOfTurn?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEndOfTurn | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEndOfTurn {
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtime {
  outputAudioFormat?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeTranscription | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsVoiceActivity {
  provider?: InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyLockedClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf0 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf1 {
  kind?: string | null;
  remainingDepth: number;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildExecutionPolicyPropagationAnyOf2 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentChildRegisteredEventChildLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildRemappedEventChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentChildRemappedEventLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentChildUnavailableEventLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentContextReceivedEventSource {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentContextReceivedEventSourceCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentSubAgentControllerGrantedEventChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentControllerGrantedEventLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecord {
  key: InlineHPDAgentSubAgentCreationAdvancedEventRecordKey;
  request: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequest;
  localId: InlineHPDAgentSubAgentCreationAdvancedEventRecordLocalId;
  childThread: InlineHPDAgentSubAgentCreationAdvancedEventRecordChildThread;
  invocationId: string;
  threadExecutionId: string;
  phase: number;
  revision: number;
  createdAt: string;
  terminalStatus?: number | null;
  terminalOutput?: string | null;
  agentOperationId?: string | null;
  error?: InlineHPDAgentSubAgentCreationAdvancedEventRecordError | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordError {
  code: string;
  message: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordKey {
  parent: InlineHPDAgentSubAgentCreationAdvancedEventRecordKeyParent;
  parentToolCallId: string;
  capabilityId: InlineHPDAgentSubAgentCreationAdvancedEventRecordKeyCapabilityId;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordKeyCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordKeyParent {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequest {
  roleName: string;
  childAgentId: string;
  context: number;
  contextSourceCursor?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestContextSourceCursor | null;
  inputFingerprint: string;
  executionPolicy: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicy;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestContextSourceCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicy {
  contractVersion: number;
  initialRunConfig?: unknown;
  harnessOverrideLayers?: Array<Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHarnessOverrideLayersItemItem>> | null;
  handoffCompaction?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompaction | null;
  descendantDefaults?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaults | null;
  lockedClients: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClients;
  clientSources: Record<string, number>;
  authority: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthority;
  propagation: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf2;
  fingerprint: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthority {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaults {
  allowUserQuestions?: boolean | null;
  harnesses?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHarnessesItem> | null;
  toolOutputs?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputs | null;
  context?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsContext | null;
  collapsing?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCollapsing | null;
  compaction?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompaction | null;
  handoffCompaction?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompaction | null;
  security?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurity | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompaction {
  automatic?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomatic | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomatic {
  trigger: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2;
  compaction: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction {
  point: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsContext {
  properties?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompaction {
  point: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurity {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem> | null;
  parentProxy?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompaction {
  point: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyHarnessOverrideLayersItemItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClients {
  transport?: number | null;
  chat?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsChat | null;
  textToSpeech?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsTextToSpeech | null;
  speechToText?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsSpeechToText | null;
  realtime?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtime | null;
  imageGeneration?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGeneration | null;
  embeddings?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEmbeddings | null;
  hostedFiles?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsHostedFiles | null;
  voiceActivity?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsVoiceActivity | null;
  endOfTurn?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEndOfTurn | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEndOfTurn {
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtime {
  outputAudioFormat?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeTranscription | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsVoiceActivity {
  provider?: InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyLockedClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf0 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf1 {
  kind?: string | null;
  remainingDepth: number;
}

export interface InlineHPDAgentSubAgentCreationAdvancedEventRecordRequestExecutionPolicyPropagationAnyOf2 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecord {
  key: InlineHPDAgentSubAgentCreationReservedEventRecordKey;
  request: InlineHPDAgentSubAgentCreationReservedEventRecordRequest;
  localId: InlineHPDAgentSubAgentCreationReservedEventRecordLocalId;
  childThread: InlineHPDAgentSubAgentCreationReservedEventRecordChildThread;
  invocationId: string;
  threadExecutionId: string;
  phase: number;
  revision: number;
  createdAt: string;
  terminalStatus?: number | null;
  terminalOutput?: string | null;
  agentOperationId?: string | null;
  error?: InlineHPDAgentSubAgentCreationReservedEventRecordError | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordError {
  code: string;
  message: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordKey {
  parent: InlineHPDAgentSubAgentCreationReservedEventRecordKeyParent;
  parentToolCallId: string;
  capabilityId: InlineHPDAgentSubAgentCreationReservedEventRecordKeyCapabilityId;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordKeyCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordKeyParent {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequest {
  roleName: string;
  childAgentId: string;
  context: number;
  contextSourceCursor?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestContextSourceCursor | null;
  inputFingerprint: string;
  executionPolicy: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicy;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestContextSourceCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicy {
  contractVersion: number;
  initialRunConfig?: unknown;
  harnessOverrideLayers?: Array<Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHarnessOverrideLayersItemItem>> | null;
  handoffCompaction?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompaction | null;
  descendantDefaults?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaults | null;
  lockedClients: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClients;
  clientSources: Record<string, number>;
  authority: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthority;
  propagation: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf2;
  fingerprint: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthority {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthorityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyAuthoritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaults {
  allowUserQuestions?: boolean | null;
  harnesses?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHarnessesItem> | null;
  toolOutputs?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputs | null;
  context?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsContext | null;
  collapsing?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCollapsing | null;
  compaction?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompaction | null;
  handoffCompaction?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompaction | null;
  security?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurity | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompaction {
  automatic?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomatic | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomatic {
  trigger: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2;
  compaction: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction {
  point: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsContext {
  properties?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompaction {
  point: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurity {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem> | null;
  parentProxy?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompaction {
  point: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyHarnessOverrideLayersItemItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClients {
  transport?: number | null;
  chat?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsChat | null;
  textToSpeech?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsTextToSpeech | null;
  speechToText?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsSpeechToText | null;
  realtime?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtime | null;
  imageGeneration?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGeneration | null;
  embeddings?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEmbeddings | null;
  hostedFiles?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsHostedFiles | null;
  voiceActivity?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsVoiceActivity | null;
  endOfTurn?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEndOfTurn | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEndOfTurn {
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtime {
  outputAudioFormat?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeTranscription | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsVoiceActivity {
  provider?: InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyLockedClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf0 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf1 {
  kind?: string | null;
  remainingDepth: number;
}

export interface InlineHPDAgentSubAgentCreationReservedEventRecordRequestExecutionPolicyPropagationAnyOf2 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentExecutionControllerEventController {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentQuestionRaisedEventChild {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentQuestionRaisedEventQuestionsItem {
  id: string;
  text: string;
  header?: string | null;
  options?: Array<InlineHPDAgentSubAgentQuestionRaisedEventQuestionsItemOptionsItem> | null;
  multiple?: boolean | null;
  recommendedOptionId?: string | null;
}

export interface InlineHPDAgentSubAgentQuestionRaisedEventQuestionsItemOptionsItem {
  id: string;
  label: string;
  description?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventControllerGrantsItem {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0 {
  $kind?: string | null;
  child: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0Child;
  localId?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0LocalId | null;
  roleName?: string | null;
  availability?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0Child {
  localId: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildLocalId;
  roleName: string;
  capabilityId: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildCapabilityId;
  childAgentId: string;
  childThread: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildChildThread;
  creationContext: number;
  creationInvocationId: string;
  parentToolCallId: string;
  executionPolicy: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicy;
  createdAt: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicy {
  contractVersion: number;
  initialRunConfig?: unknown;
  harnessOverrideLayers?: Array<Array<InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHarnessOverrideLayersItemItem>> | null;
  handoffCompaction?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompaction | null;
  descendantDefaults?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaults | null;
  lockedClients: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClients;
  clientSources: Record<string, number>;
  authority: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyAuthority;
  propagation: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf1 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf2;
  fingerprint: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyAuthority {
  approval?: number | null;
  sandbox?: unknown;
  permissionOverrides?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaults {
  allowUserQuestions?: boolean | null;
  harnesses?: Array<unknown> | null;
  toolOutputs?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsToolOutputs | null;
  context?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsContext | null;
  collapsing?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCollapsing | null;
  compaction?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompaction | null;
  handoffCompaction?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsHandoffCompaction | null;
  security?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurity | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompaction {
  automatic?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomatic | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomatic {
  trigger: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2;
  compaction: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticCompaction {
  point: unknown;
  preservation?: unknown;
  strategy: unknown;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsContext {
  properties?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsHandoffCompaction {
  point: unknown;
  preservation?: unknown;
  strategy: unknown;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurity {
  approval?: number | null;
  sandbox?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandbox | null;
  permissionOverrides?: Array<InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItem {
  selector: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilities {
  filesystem?: Array<InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyDescendantDefaultsToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompaction {
  point: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf1 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf2;
  preservation?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf1 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyHarnessOverrideLayersItemItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClients {
  transport?: number | null;
  chat?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsChat | null;
  textToSpeech?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsTextToSpeech | null;
  speechToText?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsSpeechToText | null;
  realtime?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtime | null;
  imageGeneration?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGeneration | null;
  embeddings?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEmbeddings | null;
  hostedFiles?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsHostedFiles | null;
  voiceActivity?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsVoiceActivity | null;
  endOfTurn?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEndOfTurn | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEndOfTurn {
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtime {
  outputAudioFormat?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeTranscription | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsVoiceActivity {
  provider?: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyLockedClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf0 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf1 {
  kind?: string | null;
  remainingDepth: number;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildExecutionPolicyPropagationAnyOf2 {
  kind?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0ChildLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf0LocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf1 {
  $kind?: string | null;
  localId: InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf1LocalId;
  roleName: string;
  availability: number;
  reason: string;
  createdAt: string;
  executionPolicyFingerprint?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventEntriesItemAnyOf1LocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItem {
  key: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKey;
  request: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequest;
  localId: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemLocalId;
  childThread: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemChildThread;
  invocationId: string;
  threadExecutionId: string;
  phase: number;
  revision: number;
  createdAt: string;
  terminalStatus?: number | null;
  terminalOutput?: string | null;
  agentOperationId?: string | null;
  error?: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemError | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemChildThread {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemError {
  code: string;
  message: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKey {
  parent: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKeyParent;
  parentToolCallId: string;
  capabilityId: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKeyCapabilityId;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKeyCapabilityId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemKeyParent {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemLocalId {
  value: string;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequest {
  roleName: string;
  childAgentId: string;
  context: number;
  contextSourceCursor?: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequestContextSourceCursor | null;
  inputFingerprint: string;
  executionPolicy: InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequestExecutionPolicy;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequestContextSourceCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentSubAgentRegistrySeedEventPendingCreationsItemRequestExecutionPolicy {
  contractVersion: number;
  initialRunConfig?: unknown;
  harnessOverrideLayers?: unknown;
  handoffCompaction?: unknown;
  descendantDefaults?: unknown;
  lockedClients: unknown;
  clientSources: unknown;
  authority: unknown;
  propagation: unknown;
  fingerprint: string;
}

export interface InlineHPDAgentSubAgentResultSubmittedEventController {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadCreatedEventPreparation {
  operationId: string;
  source: InlineHPDAgentThreadCreatedEventPreparationSource;
  requestFingerprint: string;
  targetSeedFingerprint?: string | null;
  sourceBoundary?: InlineHPDAgentThreadCreatedEventPreparationSourceBoundary | null;
}

export interface InlineHPDAgentThreadCreatedEventPreparationSource {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadCreatedEventPreparationSourceBoundary {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentThreadExecutionFinishedEventError {
  type: string;
  message: string;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperation {
  operationId: string;
  source: InlineHPDAgentThreadForkOperationChangedEventOperationSource;
  target: InlineHPDAgentThreadForkOperationChangedEventOperationTarget;
  sourceBoundary: InlineHPDAgentThreadForkOperationChangedEventOperationSourceBoundary;
  requestFingerprint: string;
  subAgentPolicy: number;
  status: number;
  revision: number;
  preparedChildren: Array<InlineHPDAgentThreadForkOperationChangedEventOperationPreparedChildrenItem>;
  childOutcomes: Array<InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItem>;
  targetSeedFingerprint?: string | null;
  error?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItem {
  localId: string;
  policy: number;
  source?: InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemSource | null;
  target?: InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemTarget | null;
  availability: number;
  targetSeedFingerprint?: string | null;
  sourceBoundary?: InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemSourceBoundary | null;
  owningParent?: InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemOwningParent | null;
  controller?: InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemController | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemController {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemOwningParent {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemSource {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemSourceBoundary {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationChildOutcomesItemTarget {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationPreparedChildrenItem {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationSource {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationSourceBoundary {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineHPDAgentThreadForkOperationChangedEventOperationTarget {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventPoint {
  kind: string;
  messageId?: string | null;
  turnId?: string | null;
  expectedJournalGeneration?: number | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventPreservation {
  kind: string;
  count?: number | null;
  tokenBudget?: number | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItem {
  authorName?: string | null;
  createdAt?: string | null;
  role?: unknown;
  contents?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf24> | null;
  messageId?: string | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf17OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf19OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf21OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf5 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf6 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf7 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf8 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf9 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf10 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf11 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf12 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf13 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf16 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf17 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf18 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf19 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf20 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf21 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf22 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf23 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf23OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventReplacementMessagesItemContentsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventStrategy {
  kind: string;
  instructions?: string | null;
  evidence?: InlineHPDAgentThreadHistoryCompactionCheckpointEventStrategyEvidence | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventStrategyEvidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineHPDAgentThreadHistoryCompactionCheckpointEventSummarizer {
  provider?: string | null;
  backend?: string | null;
  model?: string | null;
  responseModel?: string | null;
  promptRevision: string;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacement {
  authorName?: string | null;
  createdAt?: string | null;
  role?: unknown;
  contents?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf24> | null;
  messageId?: string | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf17OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf19OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf21OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23 {
  $type: string;
  outputs?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf5 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf6 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf7 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf8 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf9 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf10 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf11 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf12 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf13 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf16 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf17 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf18 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf19 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf20 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf21 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf22 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf23 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf1 {
  $type: string;
  message?: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName?: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf23OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf6 {
  $type: string;
  text?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf7 {
  $type: string;
  text?: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentThreadMessageReplacedEventReplacementContentsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperation {
  receipt: InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceipt;
  functionName: string;
  functionCallId: string;
}

export interface InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceipt {
  operationId: string;
  providerOperationId?: string | null;
  sourceKind: number;
  name: string;
  address: InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceiptAddress;
  providerStatus: number;
  observationStatus: number;
  message?: string | null;
  control: InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceiptControl;
  metadata?: Record<string, string> | null;
}

export interface InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceiptAddress {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface InlineHPDAgentToolBodyOperationCommittedFailureEventCommittedOperationReceiptControl {
  handleId?: string | null;
  kind: number;
  capabilities: number;
}

export interface InlineHPDAgentToolBodyOperationCommittedFailureEventInvocation {
  functionName: string;
  functionCallId: string;
  action?: string | null;
  requestedMode?: unknown;
  resolvedMode: unknown;
  policy: unknown;
  handling: number;
  ingressProvenance: number;
}

export interface InlineHPDAgentToolCallResultEventResult {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineHPDAgentUserAudioTranscriptFailedEventError {
  code: string;
  message: string;
  category?: number | null;
  isRetryable?: boolean | null;
  statusCode?: number | null;
  errorType?: string | null;
  requestId?: string | null;
  retryAfter?: string | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItem {
  authorName?: string | null;
  createdAt?: string | null;
  role?: unknown;
  contents?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf24> | null;
  messageId?: string | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf3InputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17 {
  $type: string;
  outputs?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf17OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19 {
  $type: string;
  outputs?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf19OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21 {
  $type: string;
  outputs?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf21OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23 {
  $type: string;
  outputs?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf5 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf6 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf7 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf8 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf9 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf10 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf11 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf12 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf13 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf16 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf17 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf18 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf19 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf20 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf21 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf22 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf23 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf24> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0 {
  $type: string;
  uri: string;
  name?: string | null;
  annotations?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0 {
  $type: string;
  title?: string | null;
  url?: string | null;
  fileId?: string | null;
  toolName?: string | null;
  snippet?: string | null;
  annotatedRegions?: Array<InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf0AnnotatedRegionsItem {
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf0AnnotationsItemAnyOf1 {
  annotatedRegions?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf1 {
  $type: string;
  message: string | null;
  errorCode?: string | null;
  details?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf10 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf11 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf12 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf13 {
  $type: string;
  requestId?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14 {
  $type: string;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf3 {
  $type: string;
  inputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf4 {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf14ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15 {
  $type: string;
  approved: boolean;
  toolCall: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 | InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5;
  reason?: string | null;
  requestId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf0 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf1 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf2 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf3 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf4 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf15ToolCallAnyOf5 {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf16 {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf17 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf18 {
  $type: string;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf19 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf2 {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf20 {
  $type: string;
  inputs?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf21 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf22 {
  $type: string;
  queries?: unknown;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf23 {
  $type: string;
  outputs?: Array<unknown> | null;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf23OutputsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf24 {
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf3 {
  $type: string;
  result: unknown;
  callId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf4 {
  $type: string;
  fileId: string;
  mediaType?: string | null;
  name?: string | null;
  sizeInBytes?: number | null;
  createdAt?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf5 {
  $type: string;
  vectorStoreId: string;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf6 {
  $type: string;
  text: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf7 {
  $type: string;
  text: string | null;
  protectedData?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf8 {
  $type: string;
  uri: string;
  mediaType?: string | null;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf9 {
  $type: string;
  details: InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf9Details;
  annotations?: Array<unknown> | null;
  additionalProperties?: unknown;
}

export interface InlineHPDAgentUserMessagesInputEventMessagesItemContentsItemAnyOf9Details {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineHPDAgentUserQuestionRequestEventQuestionsItem {
  id: string;
  text: string;
  header?: string | null;
  options?: Array<InlineHPDAgentUserQuestionRequestEventQuestionsItemOptionsItem> | null;
  multiple?: boolean | null;
  recommendedOptionId?: string | null;
}

export interface InlineHPDAgentUserQuestionRequestEventQuestionsItemOptionsItem {
  id: string;
  label: string;
  description?: string | null;
}

export interface InlineMcpServerConfigToolInvocationModePolicies {
}

export interface InlineSubAgentRunConfigAudio {
  enabled?: boolean | null;
  inputMode?: number | null;
  outputMode?: number | null;
  assistantOutputMode?: number | null;
  pacing?: InlineSubAgentRunConfigAudioPacing | null;
  progressiveRouteMode?: number | null;
  pushTextAggregationMode?: number | null;
  artifactCapturePolicy?: number | null;
  contentType?: string | null;
  enablePlayback?: boolean | null;
}

export interface InlineSubAgentRunConfigAudioPacing {
  mode?: number | null;
  first?: InlineSubAgentRunConfigAudioPacingFirst | null;
  continuation?: InlineSubAgentRunConfigAudioPacingContinuation | null;
  boundaries?: InlineSubAgentRunConfigAudioPacingBoundaries | null;
  filtering?: InlineSubAgentRunConfigAudioPacingFiltering | null;
}

export interface InlineSubAgentRunConfigAudioPacingBoundaries {
  requireLookahead?: boolean | null;
  protectDecimals?: boolean | null;
  protectAbbreviations?: boolean | null;
  protectInitials?: boolean | null;
  protectUrls?: boolean | null;
  protectEllipses?: boolean | null;
}

export interface InlineSubAgentRunConfigAudioPacingContinuation {
  maxCharacters?: number | null;
  preferSentenceBoundaries?: boolean | null;
  allowPhraseBoundaries?: boolean | null;
  maxInFlightSynthesisRequests?: number | null;
}

export interface InlineSubAgentRunConfigAudioPacingFiltering {
  enabled?: boolean | null;
  removeCodeBlocks?: boolean | null;
  removeMarkdownTables?: boolean | null;
  stripMarkdownFormatting?: boolean | null;
  simplifyLinks?: boolean | null;
  normalizeWhitespace?: boolean | null;
  collapseRepeatedPunctuation?: boolean | null;
  emojiPolicy?: number | null;
}

export interface InlineSubAgentRunConfigAudioPacingFirst {
  minCharacters?: number | null;
  maxCharacters?: number | null;
  emitFirstSafeSentenceImmediately?: boolean | null;
}

export interface InlineSubAgentRunConfigBackgroundResponses {
  allow?: boolean | null;
  pollingInterval?: string | null;
  timeout?: string | null;
}

export interface InlineSubAgentRunConfigClientPropagationAnyOf0 {
  kind?: string | null;
}

export interface InlineSubAgentRunConfigClientPropagationAnyOf1 {
  kind?: string | null;
  depth: number;
}

export interface InlineSubAgentRunConfigClientPropagationAnyOf2 {
  kind?: string | null;
}

export interface InlineSubAgentRunConfigClients {
  transport?: number | null;
  chat?: InlineSubAgentRunConfigClientsChat | null;
  textToSpeech?: InlineSubAgentRunConfigClientsTextToSpeech | null;
  speechToText?: InlineSubAgentRunConfigClientsSpeechToText | null;
  realtime?: InlineSubAgentRunConfigClientsRealtime | null;
  imageGeneration?: InlineSubAgentRunConfigClientsImageGeneration | null;
  embeddings?: InlineSubAgentRunConfigClientsEmbeddings | null;
  hostedFiles?: InlineSubAgentRunConfigClientsHostedFiles | null;
  voiceActivity?: InlineSubAgentRunConfigClientsVoiceActivity | null;
  endOfTurn?: InlineSubAgentRunConfigClientsEndOfTurn | null;
}

export interface InlineSubAgentRunConfigClientsChat {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: unknown;
  reasoning?: unknown;
  provider?: unknown;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: unknown;
}

export interface InlineSubAgentRunConfigClientsEmbeddings {
  dimensions?: number | null;
  provider?: InlineSubAgentRunConfigClientsEmbeddingsProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsEmbeddingsProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsEndOfTurn {
  provider?: InlineSubAgentRunConfigClientsEndOfTurnProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsEndOfTurnProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsHostedFiles {
  scope?: string | null;
  purpose?: string | null;
  limit?: number | null;
  provider?: InlineSubAgentRunConfigClientsHostedFilesProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsHostedFilesProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsImageGeneration {
  count?: number | null;
  imageSize?: InlineSubAgentRunConfigClientsImageGenerationImageSize | null;
  mediaType?: string | null;
  streamingCount?: number | null;
  provider?: InlineSubAgentRunConfigClientsImageGenerationProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsImageGenerationImageSize {
  width?: number | null;
  height?: number | null;
}

export interface InlineSubAgentRunConfigClientsImageGenerationProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsRealtime {
  outputAudioFormat?: InlineSubAgentRunConfigClientsRealtimeOutputAudioFormat | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: InlineSubAgentRunConfigClientsRealtimeTranscription | null;
  provider?: InlineSubAgentRunConfigClientsRealtimeProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsRealtimeOutputAudioFormat {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface InlineSubAgentRunConfigClientsRealtimeProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsRealtimeTranscription {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface InlineSubAgentRunConfigClientsSpeechToText {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: InlineSubAgentRunConfigClientsSpeechToTextProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsSpeechToTextProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsTextToSpeech {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: InlineSubAgentRunConfigClientsTextToSpeechProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsTextToSpeechProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigClientsVoiceActivity {
  provider?: InlineSubAgentRunConfigClientsVoiceActivityProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigClientsVoiceActivityProvider {
  key: string;
  backend?: string | null;
  authentication?: unknown;
}

export interface InlineSubAgentRunConfigCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineSubAgentRunConfigCompaction {
  automatic?: InlineSubAgentRunConfigCompactionAutomatic | null;
}

export interface InlineSubAgentRunConfigCompactionAutomatic {
  trigger: InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf0 | InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf1 | InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf2;
  compaction: InlineSubAgentRunConfigCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineSubAgentRunConfigCompactionAutomaticCompaction {
  point: unknown;
  preservation?: unknown;
  strategy: unknown;
  commitMode?: number | null;
}

export interface InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineSubAgentRunConfigCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineSubAgentRunConfigContext {
  properties?: unknown;
}

export interface InlineSubAgentRunConfigDescendantDefaults {
  allowUserQuestions?: boolean | null;
  harnesses?: Array<InlineSubAgentRunConfigDescendantDefaultsHarnessesItem> | null;
  toolOutputs?: InlineSubAgentRunConfigDescendantDefaultsToolOutputs | null;
  context?: InlineSubAgentRunConfigDescendantDefaultsContext | null;
  collapsing?: InlineSubAgentRunConfigDescendantDefaultsCollapsing | null;
  compaction?: InlineSubAgentRunConfigDescendantDefaultsCompaction | null;
  handoffCompaction?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompaction | null;
  security?: InlineSubAgentRunConfigDescendantDefaultsSecurity | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCollapsing {
  enableErrorRecovery?: boolean | null;
  recoveryHistoryMode?: number | null;
  hideToolHarnessInteractionsWithinTurn?: boolean | null;
  neverCollapseFunctions?: Array<string> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompaction {
  automatic?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomatic | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomatic {
  trigger: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf2;
  compaction: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompaction;
  continuation?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompaction {
  point: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2;
  preservation?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 | null;
  strategy: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf0 {
  $type?: string | null;
  turns: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf1 {
  $type?: string | null;
  inputTokens: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsCompactionAutomaticTriggerAnyOf2 {
  $type?: string | null;
  totalInputTokens: number;
  percentage: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsContext {
  properties?: unknown;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompaction {
  point: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf2;
  preservation?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecurity {
  approval?: number | null;
  sandbox?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandbox | null;
  permissionOverrides?: Array<InlineSubAgentRunConfigDescendantDefaultsSecurityPermissionOverridesItem> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecurityPermissionOverridesItem {
  selector: InlineSubAgentRunConfigDescendantDefaultsSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilities | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilities {
  filesystem?: Array<InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem> | null;
  parentProxy?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkDeniedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineSubAgentRunConfigDescendantDefaultsToolOutputsRetrievalLimits | null;
}

export interface InlineSubAgentRunConfigDescendantDefaultsToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineSubAgentRunConfigGoals {
  toolAccess?: string | null;
  policies?: InlineSubAgentRunConfigGoalsPolicies | null;
}

export interface InlineSubAgentRunConfigGoalsPolicies {
  completion?: string | null;
  blocker?: string | null;
  continuation?: string | null;
  accounting?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompaction {
  point: InlineSubAgentRunConfigHandoffCompactionPointAnyOf0 | InlineSubAgentRunConfigHandoffCompactionPointAnyOf1 | InlineSubAgentRunConfigHandoffCompactionPointAnyOf2;
  preservation?: InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf0 | InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf1 | InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2 | null;
  strategy: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf0 | InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1;
  commitMode?: number | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionPointAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionPointAnyOf1 {
  $type?: string | null;
  messageId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionPointAnyOf2 {
  $type?: string | null;
  turnId: string;
  expectedJournalGeneration?: number | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf1 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2 {
  $type?: string | null;
  limit: InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2LimitAnyOf0 | InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2LimitAnyOf1;
}

export interface InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2LimitAnyOf0 {
  $type?: string | null;
  count: number;
}

export interface InlineSubAgentRunConfigHandoffCompactionPreservationAnyOf2LimitAnyOf1 {
  $type?: string | null;
  tokens: number;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf0 {
  $type?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1 {
  $type?: string | null;
  summarizer?: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1Summarizer | null;
  instructions?: string | null;
  evidence?: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1Evidence | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1Evidence {
  includeToolCalls?: boolean | null;
  includeToolResults?: boolean | null;
  includeErrors?: boolean | null;
  maxContentCharacters?: number | null;
  maxEvidenceCharacters?: number | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1Summarizer {
  temperature?: number | null;
  topP?: number | null;
  topK?: number | null;
  maxOutputTokens?: number | null;
  frequencyPenalty?: number | null;
  presencePenalty?: number | null;
  seed?: number | null;
  stopSequences?: Array<string> | null;
  reasoning?: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerReasoning | null;
  provider?: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProvider | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProvider {
  key: string;
  backend?: string | null;
  authentication?: InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 | InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 | InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 | InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf0 {
  type?: string | null;
  secretKey: string;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf1 {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf2 {
  type?: string | null;
  credentialName: string;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerProviderAuthenticationAnyOf3 {
  type?: string | null;
}

export interface InlineSubAgentRunConfigHandoffCompactionStrategyAnyOf1SummarizerReasoning {
  effort?: number | null;
  output?: number | null;
}

export interface InlineSubAgentRunConfigHarnessesItem {
  name?: string | null;
  config?: unknown;
  resets?: Array<string> | null;
}

export interface InlineSubAgentRunConfigSecurity {
  approval?: number | null;
  sandbox?: InlineSubAgentRunConfigSecuritySandbox | null;
  permissionOverrides?: Array<InlineSubAgentRunConfigSecurityPermissionOverridesItem> | null;
}

export interface InlineSubAgentRunConfigSecurityPermissionOverridesItem {
  selector: InlineSubAgentRunConfigSecurityPermissionOverridesItemSelector;
  requiresPermission: boolean;
}

export interface InlineSubAgentRunConfigSecurityPermissionOverridesItemSelector {
  functionName?: string | null;
  action?: string | null;
  authority?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandbox {
  mode?: number | null;
  escape?: number | null;
  capabilities?: InlineSubAgentRunConfigSecuritySandboxCapabilities | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilities {
  filesystem?: Array<InlineSubAgentRunConfigSecuritySandboxCapabilitiesFilesystemItem> | null;
  network?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetwork | null;
  interactive?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesInteractive | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesFilesystemItem {
  access: number;
  path: string;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesInteractive {
  allowPty?: boolean | null;
  allowStdin?: boolean | null;
  allowLocalBinding?: boolean | null;
  allowedMachLookups?: Array<string> | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetwork {
  mode: number;
  allowedDomains?: Array<InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkAllowedDomainsItem> | null;
  deniedDomains?: Array<unknown> | null;
  parentProxy?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxy | null;
  requestFilter?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilter | null;
  requireProxyMediation?: boolean | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkAllowedDomainsItem {
  pattern: string;
  kind?: number | null;
  reason?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxy {
  proxyUri?: string | null;
  credential?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxyCredential | null;
  allowEnvironmentProxy?: boolean | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkParentProxyCredential {
  value?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilter {
  policyName?: string | null;
  providerRuleSet?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSet {
  providerId?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId | null;
  schemaId?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId | null;
  contentType?: InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType | null;
  payload?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetContentType {
  value?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetProviderId {
  value?: string | null;
}

export interface InlineSubAgentRunConfigSecuritySandboxCapabilitiesNetworkRequestFilterProviderRuleSetSchemaId {
  value?: string | null;
}

export interface InlineSubAgentRunConfigStreaming {
  coalesceDeltas?: boolean | null;
}

export interface InlineSubAgentRunConfigStructuredOutput {
  mode?: string | null;
  schema?: unknown;
  schemaName?: string | null;
  schemaDescription?: string | null;
  toolName?: string | null;
  streamPartials?: boolean | null;
  partialDebounceMs?: number | null;
}

export interface InlineSubAgentRunConfigSystemInstructions {
  override?: string | null;
  append?: string | null;
}

export interface InlineSubAgentRunConfigToolOutputs {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: InlineSubAgentRunConfigToolOutputsRetrievalLimits | null;
}

export interface InlineSubAgentRunConfigToolOutputsRetrievalLimits {
  maxListResultsPerCall?: number | null;
  maxReadBytesPerCall?: number | null;
  maxReadBytesPerTurn?: number | null;
  maxReadCallsPerTurn?: number | null;
  maxListCallsPerTurn?: number | null;
}

export interface InlineSubAgentRunConfigTools {
  clientInput?: InlineSubAgentRunConfigToolsClientInput | null;
}

export interface InlineSubAgentRunConfigToolsClientInput {
  clientToolHarnesses?: Array<InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItem> | null;
  expandedContainers?: Array<string> | null;
  hiddenTools?: Array<string> | null;
  context?: Array<InlineSubAgentRunConfigToolsClientInputContextItem> | null;
  state?: unknown;
  metadata?: unknown;
  resetClientState?: boolean | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItem {
  name: string;
  description: string | null;
  tools: Array<InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItem>;
  skills?: Array<InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItem> | null;
  functionResult?: string | null;
  systemPrompt?: string | null;
  startCollapsed?: boolean | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItem {
  name: string;
  description: string;
  instructions: string;
  reinforcement?: string | null;
  references?: Array<InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItemReferencesItem> | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemSkillsItemReferencesItem {
  toolName: string;
  toolsetName?: string | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItem {
  name: string;
  description: string;
  parametersSchema: unknown;
  defaultPolicy?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicy | null;
  operationContract?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContract | null;
  metadata?: unknown;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicy {
  permission?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyOperationNotification | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemDefaultPolicyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContract {
  discriminator: string;
  actions: Record<string, InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty>;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalProperty {
  permission?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission | null;
  mutatesState?: boolean | null;
  requiresFreshContext?: boolean | null;
  destructive?: boolean | null;
  idempotent?: boolean | null;
  invocationModePolicy?: unknown;
  operationNotification?: InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyOperationNotification {
  includeInputRequired?: boolean | null;
  includeProgress?: boolean | null;
  includeTerminal?: boolean | null;
  deduplicationKey?: string | null;
  minimumInterval?: string | null;
}

export interface InlineSubAgentRunConfigToolsClientInputClientToolHarnessesItemToolsItemOperationContractActionsAdditionalPropertyPermission {
  requiresPermission: boolean;
  authority: string;
  policyDescriptorId?: string | null;
  source: number;
}

export interface InlineSubAgentRunConfigToolsClientInputContextItem {
  description: string;
  value: unknown;
  key?: string | null;
  effectiveKey?: string | null;
  valueText?: string | null;
}

export interface InlineUpdateSessionRequestMetadata {
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostRequestApplicationJson {
  runConfig: unknown;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostResponse200ApplicationJson {
  sessionId?: string | null;
  threadId?: string | null;
  providerKey?: string | null;
  modelId?: string | null;
  contextWindow?: number | null;
  lastObservedInputTokens?: number | null;
  estimatedInputTokens?: number | null;
  effectiveInputTokens?: number | null;
  usageRatio?: number | null;
  isEstimate?: boolean | null;
  source?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostResponse400ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, Array<string>> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdContextUsagePostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStream {
  id?: string | null;
  event: string;
  data: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf1;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0 {
  event: HPDAgentAgentCapabilityRefreshPublishedEvent | HPDAgentAgentCapabilityRefreshRejectedEvent | HPDAgentAgentCapabilityRefreshStartedEvent | HPDAgentAgentCompletionEvent | HPDAgentAgentDecisionEvent | HPDAgentAgentOperationNotificationDeliveredEvent | HPDAgentAgentOperationNotificationQueuedEvent | HPDAgentAgentOperationNotificationSuppressedEvent | HPDAgentAgentOperationRegisteredEvent | HPDAgentAgentOperationTombstonedEvent | HPDAgentAgentOperationTransitionedEvent | HPDAgentAgentRequestTerminatedEvent | HPDAgentAgentTurnCapabilitiesPinnedEvent | HPDAgentAgentTurnFinishedEvent | HPDAgentAgentTurnStartedEvent | HPDAgentCircuitBreakerTriggeredEvent | HPDAgentClientToolsClientToolInvokeOutcomeEvent | HPDAgentClientToolsClientToolInvokeRequestEvent | HPDAgentCollapsedToolsVisibleEvent | HPDAgentCollapsingStateEvent | HPDAgentCompactionEvent | HPDAgentContainerExpandedEvent | HPDAgentContentAddedEvent | HPDAgentContentCaptureFinalizedEvent | HPDAgentContentReadEvent | HPDAgentMiddlewareContentReferenceResolutionFailedEvent | HPDAgentMiddlewareContentReferenceResolvedEvent | HPDAgentContentScopeCleanedEvent | HPDAgentMiddlewareContentUploadedEvent | HPDAgentMiddlewareContentUploadFailedEvent | HPDAgentContinuationRequestEvent | HPDAgentContinuationResponseEvent | HPDAgentDeltaSendingActivatedEvent | HPDAgentDocumentProcessedEvent | HPDAgentEventDroppedEvent | HPDAgentFunctionInvocationAuditedEvent | HPDAgentFunctionRetryEvent | HPDAgentGoalsGoalAwaitingInputEvent | HPDAgentGoalsGoalBlockedEvent | HPDAgentGoalsGoalBlockerRejectedEvent | HPDAgentGoalsGoalBlockerReportedEvent | HPDAgentGoalsGoalClearedEvent | HPDAgentGoalsGoalCompletedEvent | HPDAgentGoalsGoalCompletionProposedEvent | HPDAgentGoalsGoalCompletionRejectedEvent | HPDAgentGoalsGoalContinuationScheduledEvent | HPDAgentGoalsGoalContinuationSkippedEvent | HPDAgentGoalsGoalContinuationStartedEvent | HPDAgentGoalsGoalEditedEvent | HPDAgentGoalsGoalFaultedEvent | HPDAgentGoalsGoalPausedEvent | HPDAgentGoalsGoalProgressAccountedEvent | HPDAgentGoalsGoalResumedEvent | HPDAgentGoalsGoalStartedEvent | HPDAgentGoalsGoalUpdatedEvent | HPDAgentGoalsGoalUsageLimitedEvent | HPDAgentMiddlewareHostedFileUploadedEvent | HPDAgentMiddlewareHostedFileUploadFailedEvent | HPDAgentInternalMessagePreparedEvent | HPDAgentInternalParallelToolExecutionEvent | HPDAgentInterruptionHandledEvent | HPDAgentIterationStartEvent | HPDAgentMaxConsecutiveErrorsExceededEvent | HPDAgentMessageTurnErrorEvent | HPDAgentMessageTurnFinishedEvent | HPDAgentMessageTurnStartedEvent | HPDAgentMiddlewareErrorEvent | HPDAgentMiddlewareStateChangedEvent | HPDAgentMiddlewareStateSnapshotEvent | HPDAgentModelCallRetryEvent | HPDAgentNestedAgentInvokedEvent | HPDAgentOperationExecutionOwnerCleanupFailedEvent | HPDAgentParentQuestionRequestEvent | HPDAgentPermissionEvaluatedEvent | HPDAgentPermissionRequestEvent | HPDAgentPermissionResolvedEvent | HPDAgentPermissionResponseEvent | HPDAgentPIIDetectedEvent | HPDAgentPlanUpdatedEvent | HPDAgentProviderOperationUsageEvent | HPDAgentProviderValuationObservationEvent | HPDAgentQuestionResponseEvent | HPDAgentReasoningDeltaEvent | HPDAgentReasoningMessageEndEvent | HPDAgentReasoningMessageStartEvent | HPDAgentRequestEventProcessedEvent | HPDAgentRuntimeAgentDurableEvent | HPDAgentRuntimeAgentLiveEvent | HPDAgentSkillActivatedEvent | HPDAgentSkillActivationFailedEvent | HPDAgentSkillActivationStartedEvent | HPDAgentSkillResourceReadCompletedEvent | HPDAgentSkillResourceReadFailedEvent | HPDAgentSkillResourceReadStartedEvent | HPDAgentSkillScriptCompletedEvent | HPDAgentSkillScriptFailedEvent | HPDAgentSkillScriptStartedEvent | HPDAgentSkillScriptTimedOutEvent | HPDAgentStateSnapshotEvent | HPDAgentStructuredOutputCompleteEvent | HPDAgentStructuredOutputErrorEvent | HPDAgentStructuredOutputPartialEvent | HPDAgentStructuredOutputStartEvent | HPDAgentSubAgentChildControllerAuthorityEvent | HPDAgentSubAgentChildDetachedEvent | HPDAgentSubAgentChildRegisteredEvent | HPDAgentSubAgentChildRemappedEvent | HPDAgentSubAgentChildUnavailableEvent | HPDAgentSubAgentContextReceivedEvent | HPDAgentSubAgentControllerGrantedEvent | HPDAgentSubAgentCreationAdvancedEvent | HPDAgentSubAgentCreationReservedEvent | HPDAgentSubAgentExecutionControllerEvent | HPDAgentSubAgentInvocationCancelledEvent | HPDAgentSubAgentInvocationCompletedEvent | HPDAgentSubAgentInvocationFailedEvent | HPDAgentSubAgentInvocationStartedEvent | HPDAgentSubAgentQuestionRaisedEvent | HPDAgentSubAgentRegistrySeedEvent | HPDAgentSubAgentResultSubmittedEvent | HPDAgentTextDeltaEvent | HPDAgentTextMessageEndEvent | HPDAgentTextMessageStartEvent | HPDAgentThreadCreatedEvent | HPDAgentThreadExecutionFinishedEvent | HPDAgentThreadExecutionStartedEvent | HPDAgentThreadForkOperationChangedEvent | HPDAgentThreadHistoryCompactionCheckpointEvent | HPDAgentThreadMessageReplacedEvent | HPDAgentThreadMiddlewareStateCommittedEvent | HPDAgentThreadUpdatedEvent | HPDAgentToolBodyOperationCommittedFailureEvent | HPDAgentToolCallArgsEvent | HPDAgentToolCallEndEvent | HPDAgentToolCallResultEvent | HPDAgentToolCallStartEvent | HPDAgentToolOutputProjectedEvent | HPDAgentToolOutputProjectionFailedEvent | HPDAgentTotalErrorThresholdExceededEvent | HPDAgentUserAudioTranscriptCompletedEvent | HPDAgentUserAudioTranscriptDeltaEvent | HPDAgentUserAudioTranscriptFailedEvent | HPDAgentUserMessageEvent | HPDAgentUserQuestionRequestEvent;
  route: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0Route;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0Route {
  origin: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0RouteOrigin;
  path: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0RoutePathItem>;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0RouteOrigin {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf0RoutePathItem {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse200TextEventStreamDataOneOf1 {
  previousGeneration: number;
  currentGeneration: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse400ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, Array<string>> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdEventsGetResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdExecutionsThreadExecutionIdCancelPostResponse200ApplicationJson {
  threadExecutionId: string;
  status: string;
  cancellationApplied: boolean;
  queuePromotion: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdExecutionsThreadExecutionIdCancelPostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdExecutionsThreadExecutionIdCancelPostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdExecutionsThreadExecutionIdCancelPostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJson {
  disposition: string;
  threadExecutionId?: string | null;
  startedAt?: string | null;
  activeExecution?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecution | null;
  result?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf1 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf2 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3 | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecution {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecutionError | null;
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecutionOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecutionError {
  type: string | null;
  message: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonActiveExecutionOperationsItem {
  operationId: string;
  providerOperationId: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId: string | null;
  version: number;
  registeredAt: string;
  startedAt: string | null;
  updatedAt: string;
  finishedAt: string | null;
  completionSummary: string | null;
  artifactReferences: Array<string> | null;
  failureCode: string | null;
  failureMessage: string | null;
  metadata: Record<string, string> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0 {
  type?: string | null;
  turnResult: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResult;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResult {
  text?: string | null;
  events?: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItem> | null;
  toolCalls?: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItem> | null;
  started?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStarted | null;
  finished?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinished | null;
  usage?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultUsage | null;
  duration?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItem {
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItemMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItemMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: Array<string> | null;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinished {
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  duration: string;
  usage: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsage;
  kind?: number | null;
  iteration?: number | null;
  terminationReason?: string | null;
  turnMessageCount?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: unknown;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsage {
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItem {
  sourceEventId: string;
  messageTurnId: string;
  threadSequenceNumber: number;
  operationId: string;
  logicalOperationId: string | null;
  attempt: number;
  operationKind: number;
  family: number;
  outcome: number;
  usage: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItemUsage | null;
  providerKey: string | null;
  modelId: string | null;
  responseId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItemUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStarted {
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  kind?: number | null;
  inputMessageCount?: number | null;
  isResume?: boolean | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStartedMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStartedMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: unknown;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItem {
  callId?: string | null;
  name?: string | null;
  argumentsJson?: string | null;
  result?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItemResult | null;
  text?: string | null;
  toolHarnessName?: string | null;
  callType?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItemResult {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultUsage {
  operations: unknown;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf1 {
  type?: string | null;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf2 {
  type?: string | null;
  disposition: number;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3 {
  type?: string | null;
  result: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf0 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf1 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf2 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf3 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf4 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf5 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf6 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf7 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf8 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf9 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf10 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf11;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf0 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf1 {
  type?: string | null;
  audioSessionId: string;
  previousRevision: number;
  currentRevision: number;
  disposition: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf10 {
  type?: string | null;
  disposition: string;
  safeCode: string;
  currentRevision?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf11 {
  type?: string | null;
  operationId: string;
  audioSessionId?: string | null;
  currentRevision?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf2 {
  type?: string | null;
  audioSessionId: string;
  candidateId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf3 {
  type?: string | null;
  audioSessionId: string;
  candidateId: string;
  revision: number;
  safeCode: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf4 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  enabled: boolean;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf5 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  enabled: boolean;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf6 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf7 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf8 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
  safeCode: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf9 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse400ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, Array<string>> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdInputsPostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse200ApplicationJson {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse200ApplicationJsonError | null;
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse200ApplicationJsonOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse200ApplicationJsonError {
  type: string | null;
  message: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse200ApplicationJsonOperationsItem {
  operationId: string;
  providerOperationId: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId: string | null;
  version: number;
  registeredAt: string;
  startedAt: string | null;
  updatedAt: string;
  finishedAt: string | null;
  completionSummary: string | null;
  artifactReferences: Array<string> | null;
  failureCode: string | null;
  failureMessage: string | null;
  metadata: Record<string, string> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdQueueStartPostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdResponsesPostResponse200ApplicationJson {
  status: number;
  requestId: string;
  message?: string | null;
  accepted?: boolean | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdResponsesPostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdResponsesPostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdResponsesPostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJson {
  observedCursor: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonObservedCursor;
  activeExecution?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecution | null;
  pendingRequests: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonPendingRequestsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecution {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecutionError | null;
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecutionOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecutionError {
  type: string | null;
  message: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonActiveExecutionOperationsItem {
  operationId: string;
  providerOperationId: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId: string | null;
  version: number;
  registeredAt: string;
  startedAt: string | null;
  updatedAt: string;
  finishedAt: string | null;
  completionSummary: string | null;
  artifactReferences: Array<string> | null;
  failureCode: string | null;
  failureMessage: string | null;
  metadata: Record<string, string> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonObservedCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse200ApplicationJsonPendingRequestsItem {
  request: HPDAgentClientToolsClientToolInvokeRequestEvent | HPDAgentContinuationRequestEvent | HPDAgentParentQuestionRequestEvent | HPDAgentPermissionRequestEvent | HPDAgentUserQuestionRequestEvent;
  createdAt: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdStateGetResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJson {
  disposition: string;
  threadExecutionId?: string | null;
  startedAt?: string | null;
  activeExecution?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecution | null;
  result?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf1 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf2 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3 | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecution {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecutionError | null;
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecutionOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecutionError {
  type: string | null;
  message: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonActiveExecutionOperationsItem {
  operationId: string;
  providerOperationId: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId: string | null;
  version: number;
  registeredAt: string;
  startedAt: string | null;
  updatedAt: string;
  finishedAt: string | null;
  completionSummary: string | null;
  artifactReferences: Array<string> | null;
  failureCode: string | null;
  failureMessage: string | null;
  metadata: Record<string, string> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0 {
  type?: string | null;
  turnResult: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResult;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResult {
  text?: string | null;
  events?: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItem> | null;
  toolCalls?: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItem> | null;
  started?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStarted | null;
  finished?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinished | null;
  usage?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultUsage | null;
  duration?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItem {
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItemMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  kind?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultEventsItemMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: Array<string> | null;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinished {
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  duration: string;
  usage: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsage;
  kind?: number | null;
  iteration?: number | null;
  terminationReason?: string | null;
  turnMessageCount?: number | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: unknown;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsage {
  operations: Array<InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItem>;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItem {
  sourceEventId: string;
  messageTurnId: string;
  threadSequenceNumber: number;
  operationId: string;
  logicalOperationId: string | null;
  attempt: number;
  operationKind: number;
  family: number;
  outcome: number;
  usage: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItemUsage | null;
  providerKey: string | null;
  modelId: string | null;
  responseId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultFinishedUsageOperationsItemUsage {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStarted {
  messageTurnId: string;
  conversationId: string;
  agentId: string;
  agentName: string;
  kind?: number | null;
  inputMessageCount?: number | null;
  isResume?: boolean | null;
  eventId?: string | null;
  threadSequenceNumber?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  threadExecutionId?: string | null;
  metadata?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStartedMetadata | null;
  traceId?: string | null;
  spanId?: string | null;
  parentSpanId?: string | null;
  channel?: number | null;
  direction?: number | null;
  eventFlowId?: string | null;
  canInterrupt?: boolean | null;
  timestamp?: string | null;
  exchangeTimestampNs?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultStartedMetadata {
  agentName: string;
  agentId: string;
  parentAgentId?: string | null;
  agentChain?: unknown;
  depth?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItem {
  callId?: string | null;
  name?: string | null;
  argumentsJson?: string | null;
  result?: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItemResult | null;
  text?: string | null;
  toolHarnessName?: string | null;
  callType?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultToolCallsItemResult {
  text?: string | null;
  json?: unknown;
  content?: Array<unknown> | null;
  resultType?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf0TurnResultUsage {
  operations: unknown;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf1 {
  type?: string | null;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf2 {
  type?: string | null;
  disposition: number;
  threadExecutionId: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3 {
  type?: string | null;
  result: InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf0 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf1 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf2 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf3 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf4 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf5 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf6 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf7 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf8 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf9 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf10 | InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf11;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf0 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf1 {
  type?: string | null;
  audioSessionId: string;
  previousRevision: number;
  currentRevision: number;
  disposition: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf10 {
  type?: string | null;
  disposition: string;
  safeCode: string;
  currentRevision?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf11 {
  type?: string | null;
  operationId: string;
  audioSessionId?: string | null;
  currentRevision?: number | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf2 {
  type?: string | null;
  audioSessionId: string;
  candidateId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf3 {
  type?: string | null;
  audioSessionId: string;
  candidateId: string;
  revision: number;
  safeCode: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf4 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  enabled: boolean;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf5 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  enabled: boolean;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf6 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf7 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf8 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
  operationId: string;
  safeCode: string;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse202ApplicationJsonResultAnyOf3ResultAnyOf9 {
  type?: string | null;
  audioSessionId: string;
  revision: number;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse400ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
  errors?: Record<string, Array<string>> | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse404ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse409ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineAgentsAgentIdSessionsSessionIdThreadsThreadIdSubagentsLocalIdInputsPostResponse500ApplicationProblemJson {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  instance?: string | null;
}

export interface InlineEventCatalogGetResponse200ApplicationJson {
  events: Array<InlineEventCatalogGetResponse200ApplicationJsonEventsItem>;
  digest: string;
}

export interface InlineEventCatalogGetResponse200ApplicationJsonEventsItem {
  discriminator: string;
  contractName: string;
  durability: number;
  moduleId: string;
  contractDigest: string;
}

export interface InlineInputCatalogGetResponse200ApplicationJson {
  inputs: Array<InlineInputCatalogGetResponse200ApplicationJsonInputsItem>;
  digest: string;
}

export interface InlineInputCatalogGetResponse200ApplicationJsonInputsItem {
  discriminator: string;
  contractName: string;
  moduleId: string;
  isPublic: boolean;
}

export interface InlineRuntimeExtensionCatalogGetResponse200ApplicationJson {
  schemas: Array<InlineRuntimeExtensionCatalogGetResponse200ApplicationJsonSchemasItem>;
  digest: string;
}

export interface InlineRuntimeExtensionCatalogGetResponse200ApplicationJsonSchemasItem {
  key: string;
  namespace: string;
  name: string;
  version: number;
  usage: number;
  payloadSchemaDigest: string;
  payloadSchema: unknown;
  description: string | null;
}

export interface InlineSessionsSessionIdThreadsThreadIdContentPostRequestMultipartFormData {
  file: string;
}

export interface InlineStorageGetResponse200ApplicationJson {
  sessions: InlineStorageGetResponse200ApplicationJsonSessions;
  agentDefinitions: InlineStorageGetResponse200ApplicationJsonAgentDefinitions;
  content: InlineStorageGetResponse200ApplicationJsonContent;
}

export interface InlineStorageGetResponse200ApplicationJsonAgentDefinitions {
  persistence: string;
  configured: boolean;
  usingDefaultFallback: boolean;
}

export interface InlineStorageGetResponse200ApplicationJsonContent {
  persistence: string;
  configured: boolean;
  usingDefaultFallback: boolean;
}

export interface InlineStorageGetResponse200ApplicationJsonSessions {
  persistence: string;
  configured: boolean;
  usingDefaultFallback: boolean;
}

export interface InputMediaPolicy {
  handlingMode?: number | null;
  allowBatchTranscription?: boolean | null;
  retainInputMediaArtifact?: boolean | null;
  allowDerivedTextPersistence?: boolean | null;
  allowDigestCapture?: boolean | null;
}

export interface JudgeCallRecord {
  evaluatorName: string;
  phase: string;
  prompt: Array<ChatMessage>;
  response: null | ChatResponse;
  modelId: string | null;
  usage: null | UsageDetails;
  duration: string;
  succeeded: boolean;
  errorMessage: string | null;
}

export interface McpManifestReference {
  path?: string | null;
  sourceName?: string | null;
}

export interface McpOAuthOptions {
  registrationMode?: number | null;
  allowDynamicRegistration?: boolean | null;
  redirectUri?: string | null;
  clientIdMetadataDocument?: string | null;
  clientId?: string | null;
  clientSecretKey?: string | null;
  scopes?: Array<string> | null;
}

export interface McpPermissionConfig {
  requiresPermission?: boolean | null;
  authority?: string | null;
  policy?: string | null;
  tools?: Record<string, McpToolPermissionOverride> | null;
}

export interface McpProcessIsolationOptions {
  enabled?: boolean | null;
  allowNetwork?: boolean | null;
  allowedPaths?: Array<string> | null;
  maxMessageBytes?: number | null;
}

export interface McpServerConfig {
  name?: string | null;
  transport?: string | null;
  command?: string | null;
  arguments?: Array<string> | null;
  workingDirectory?: string | null;
  inheritEnvironmentVariables?: boolean | null;
  useDefaultEnvironmentVariables?: boolean | null;
  environment?: Record<string, string> | null;
  environmentSecretKeys?: Record<string, string> | null;
  processIsolation?: null | McpProcessIsolationOptions | null;
  endpoint?: string | null;
  headers?: Record<string, string> | null;
  headerSecretKeys?: Record<string, string> | null;
  exactVersion?: string | null;
  description?: string | null;
  enabled?: boolean | null;
  enableCollapsing?: boolean | null;
  enableResources?: boolean | null;
  enablePrompts?: boolean | null;
  invocationModePolicy?: unknown;
  toolInvocationModePolicies?: InlineMcpServerConfigToolInvocationModePolicies | null;
  maxResourceListResults?: number | null;
  maxResourceContentLength?: number | null;
  maxPromptListResults?: number | null;
  maxPromptContentLength?: number | null;
  allowedTools?: Array<string> | null;
  permission?: null | McpPermissionConfig | null;
  oauth?: null | McpOAuthOptions | null;
}

export interface McpToolPermissionOverride {
  requiresPermission?: boolean | null;
  authority?: string | null;
  policy?: string | null;
}

export interface ModelRetryConfig {
  autoRefreshTokensOn401?: boolean | null;
  maxRetries?: number | null;
  retryDelay?: string | null;
  useProviderRetryDelays?: boolean | null;
  maxRetryDelay?: string | null;
  backoffMultiplier?: number | null;
  maxRetriesByCategory?: Record<string, number> | null;
}

export interface ObservabilityConfig {
  textDeltaSamplingRate?: number | null;
  reasoningDeltaSamplingRate?: number | null;
  enableSampling?: boolean | null;
  emitObservabilityEvents?: boolean | null;
  maxConcurrentObservers?: number | null;
  maxConsecutiveFailures?: number | null;
  successesToResetCircuitBreaker?: number | null;
}

export interface PreviousHistoryLimit {
}

export interface PreviousHistoryLimitPreviousItemCountLimit {
  $type?: string | null;
  count: number;
}

export interface PreviousHistoryLimitPreviousTokenBudgetLimit {
  $type?: string | null;
  tokens: number;
}

export interface PrivacyPolicy {
  redactRawAudioByDefault?: boolean | null;
  allowMetadataOnlyReplay?: boolean | null;
  allowTranscriptReplay?: boolean | null;
}

export interface ProviderAuthentication {
}

export interface ProviderAuthenticationAnonymousProviderAuthentication {
  type?: string | null;
}

export interface ProviderAuthenticationApiKeyProviderAuthentication {
  type?: string | null;
  secretKey: string;
}

export interface ProviderAuthenticationExternalIdentityProviderAuthentication {
  type?: string | null;
  credentialName: string;
}

export interface ProviderAuthenticationOAuthProviderAuthentication {
  type?: string | null;
  accountId: string;
  scopes?: Array<string> | null;
  authorizationProfile?: string | null;
  storeKey?: string | null;
}

export interface ProviderReference {
  key: string;
  backend?: string | null;
  authentication?: null | ProviderAuthentication | null;
}

export interface RateResponse {
  evaluatorName: string;
  passRate: number;
}

export interface RealtimeAudioFormatRunConfig {
  mediaType?: string | null;
  sampleRate?: number | null;
}

export interface RealtimeClientConfig {
  outputAudioFormat?: null | RealtimeAudioFormatRunConfig | null;
  voice?: string | null;
  maxOutputTokens?: number | null;
  outputModalities?: Array<string> | null;
  transcription?: null | RealtimeTranscriptionRunConfig | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface RealtimeTranscriptionRunConfig {
  modelName?: string | null;
  speechLanguage?: string | null;
  prompt?: string | null;
}

export interface ReasoningOptions {
  effort?: null | number | null | null;
  output?: null | number | null | null;
}

export interface RedTeamAnalyticsSummary {
  attackSuccessRate: number;
  attackSuccessRateByPlugin: Record<string, number>;
  attackSuccessRateByStrategy: Record<string, number>;
  findingCount: number;
}

export interface RedTeamFinding {
  scoreRecordId: string;
  pluginId: string | null;
  strategyId: string | null;
  category: string | null;
  severity: string | null;
  attackGoal: string | null;
  attackSucceeded: boolean;
  evaluatorName: string;
  sessionId: string;
  threadId: string;
  turnIndex: number;
  createdAt: string;
}

export interface RedTeamPluginCatalogItem {
  id: string;
  displayName: string;
  category: string;
}

export interface RedTeamStrategyCatalogItem {
  id: string;
  displayName: string;
}

export interface RegisterStringDatasetRequest {
  datasetId?: string | null;
  version?: string | null;
  description?: string | null;
  registeredAt?: string | null;
  cases?: Array<StringEvalCaseDto> | null;
}

export interface RetryExclusionConfig {
  harnesses?: Array<string> | null;
  functions?: Array<string> | null;
  actions?: Array<ActionRetryExclusion> | null;
}

export interface RiskAutonomyDataPoint {
  sessionId: string;
  threadId: string;
  turnIndex: number;
  agentName: string;
  riskScore: number;
  autonomyScore: number;
  createdAt: string;
}

export interface SafetyAnalyticsSummary {
  totalCount: number;
  passedCount: number;
  failedCount: number;
  passRate: number;
  averageSafetyScore: number;
  byCategory: Record<string, number>;
  bySeverity: Record<string, number>;
  byRecommendedAction: Record<string, number>;
}

export interface ScoreAggregate {
  average: number;
  min: number;
  max: number;
  count: number;
  passRate: number;
}

export interface ScoreBucket {
  start: string;
  average: number;
  min: number;
  max: number;
  count: number;
  passRate: number;
}

export interface ScoreRecord {
  id?: string | null;
  evaluatorName?: string | null;
  evaluatorVersion?: string | null;
  result?: EvaluationResult | null;
  source?: number | null;
  sessionId?: string | null;
  threadId?: string | null;
  turnIndex?: number | null;
  agentName?: string | null;
  providerKey?: string | null;
  modelId?: string | null;
  responseModelId?: string | null;
  datasetId?: string | null;
  datasetVersion?: string | null;
  caseId?: string | null;
  caseVersion?: string | null;
  caseValidFrom?: string | null;
  caseValidTo?: string | null;
  redTeamPluginId?: string | null;
  redTeamStrategyId?: string | null;
  redTeamCategory?: string | null;
  redTeamSeverity?: string | null;
  attackGoal?: string | null;
  attackSucceeded?: boolean | null;
  turnUsage?: null | UsageDetails | null;
  turnDuration?: string | null;
  attributes?: unknown;
  metrics?: Record<string, number> | null;
  judgeModelId?: string | null;
  judgeUsage?: null | UsageDetails | null;
  judgeDuration?: string | null;
  judgeCalls?: Array<JudgeCallRecord> | null;
  samplingRate?: number | null;
  policy?: number | null;
  createdAt?: string | null;
}

export interface ScoreTrend {
  evaluatorName: string;
  buckets: Array<ScoreBucket>;
}

export interface SearchSessionsRequest {
  metadata: unknown;
  offset?: number | null;
  limit?: number | null;
}

export interface SessionDto {
  id: string;
  createdAt: string;
  lastActivity: string;
  metadata: unknown;
}

export interface SkillRuntimeOptions {
  instructionDelivery?: number | null;
}

export interface SpeechToTextClientConfig {
  speechLanguage?: string | null;
  speechSampleRate?: number | null;
  textLanguage?: string | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface StoredAgentDto {
  id: string;
  name: string;
  config: AgentConfig;
  createdAt: string;
  updatedAt: string;
  metadata: unknown;
}

export interface StringDatasetCaseChangeDto {
  caseId?: string | null;
  before?: StringEvalCaseDto | null;
  after?: StringEvalCaseDto | null;
}

export interface StringDatasetDiffResponse {
  datasetId?: string | null;
  fromVersion?: string | null;
  toVersion?: string | null;
  added?: Array<StringEvalCaseDto> | null;
  removed?: Array<StringEvalCaseDto> | null;
  changed?: Array<StringDatasetCaseChangeDto> | null;
}

export interface StringDatasetVersionResponse {
  datasetId?: string | null;
  version?: string | null;
  cases?: Array<StringEvalCaseDto> | null;
}

export interface StringEvalCaseDto {
  caseId?: string | null;
  name?: string | null;
  version?: string | null;
  validFrom?: string | null;
  validTo?: string | null;
  input?: string | null;
  groundTruth?: string | null;
}

export interface SubAgentDto {
  localId: string;
  role: string;
  availability: number;
  agentId: string | null;
  sessionId: string | null;
  threadId: string | null;
  status: string | null;
  messageCount: number;
  reason: string | null;
  providerKey: string | null;
  modelName: string | null;
}

export interface SubAgentForkChildOutcome {
  localId: string;
  policy: number;
  source: null | ThreadKey;
  target: null | ThreadKey;
  availability: number;
  targetSeedFingerprint?: string | null;
  sourceBoundary?: null | ThreadJournalCursor | null;
  owningParent?: null | ThreadKey | null;
  controller?: null | ThreadKey | null;
}

export interface SubAgentForkOptions {
  policy?: number | null;
  descendantPolicy?: number | null;
}

export interface SubAgentRunConfig {
  handoffCompaction?: InlineSubAgentRunConfigHandoffCompaction | null;
  descendantDefaults?: InlineSubAgentRunConfigDescendantDefaults | null;
  clientPropagation?: InlineSubAgentRunConfigClientPropagationAnyOf0 | InlineSubAgentRunConfigClientPropagationAnyOf1 | InlineSubAgentRunConfigClientPropagationAnyOf2 | null;
  harnesses?: Array<InlineSubAgentRunConfigHarnessesItem> | null;
  toolOutputs?: InlineSubAgentRunConfigToolOutputs | null;
  allowUserQuestions?: boolean | null;
  enableParentCommunication?: boolean | null;
  security?: InlineSubAgentRunConfigSecurity | null;
  clients?: InlineSubAgentRunConfigClients | null;
  systemInstructions?: InlineSubAgentRunConfigSystemInstructions | null;
  tools?: InlineSubAgentRunConfigTools | null;
  context?: InlineSubAgentRunConfigContext | null;
  backgroundResponses?: InlineSubAgentRunConfigBackgroundResponses | null;
  streaming?: InlineSubAgentRunConfigStreaming | null;
  uploadStrategy?: number | null;
  audio?: InlineSubAgentRunConfigAudio | null;
  compaction?: InlineSubAgentRunConfigCompaction | null;
  goals?: InlineSubAgentRunConfigGoals | null;
  collapsing?: InlineSubAgentRunConfigCollapsing | null;
  structuredOutput?: InlineSubAgentRunConfigStructuredOutput | null;
}

export interface TextToSpeechBoundaryOptions {
  requireLookahead?: boolean | null;
  protectDecimals?: boolean | null;
  protectAbbreviations?: boolean | null;
  protectInitials?: boolean | null;
  protectUrls?: boolean | null;
  protectEllipses?: boolean | null;
}

export interface TextToSpeechClientConfig {
  voiceId?: string | null;
  language?: string | null;
  audioFormat?: string | null;
  speed?: number | null;
  pitch?: number | null;
  volume?: number | null;
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface TextToSpeechContinuationOptions {
  maxCharacters?: number | null;
  preferSentenceBoundaries?: boolean | null;
  allowPhraseBoundaries?: boolean | null;
  maxInFlightSynthesisRequests?: number | null;
}

export interface TextToSpeechFilteringOptions {
  enabled?: boolean | null;
  removeCodeBlocks?: boolean | null;
  removeMarkdownTables?: boolean | null;
  stripMarkdownFormatting?: boolean | null;
  simplifyLinks?: boolean | null;
  normalizeWhitespace?: boolean | null;
  collapseRepeatedPunctuation?: boolean | null;
  emojiPolicy?: number | null;
}

export interface TextToSpeechFirstSegmentOptions {
  minCharacters?: number | null;
  maxCharacters?: number | null;
  emitFirstSafeSentenceImmediately?: boolean | null;
}

export interface TextToSpeechPacingOptions {
  mode?: number | null;
  first?: TextToSpeechFirstSegmentOptions | null;
  continuation?: TextToSpeechContinuationOptions | null;
  boundaries?: TextToSpeechBoundaryOptions | null;
  filtering?: TextToSpeechFilteringOptions | null;
}

export interface ThreadComparisonResult {
  sessionId: string;
  threadId1: string;
  threadId2: string;
  thread1Scores: Record<string, ScoreAggregate>;
  thread2Scores: Record<string, ScoreAggregate>;
}

export interface ThreadDto {
  id: string;
  sessionId: string;
  defaultAgentId: string;
  name: string;
  description: string | null;
  forkedFrom: string | null;
  forkedAtMessageId: string | null;
  forkedAtMessageIndex: number | null;
  createdAt: string;
  lastActivity: string;
  messageCount: number;
  tags: Array<string> | null;
  ancestors: Record<string, string> | null;
  totalForks: number;
  metadata?: unknown;
  kind?: number | null;
  visibility?: number | null;
  parentSessionId?: string | null;
  parentThreadId?: string | null;
  subAgentName?: string | null;
  invocationId?: string | null;
  subAgentSourceKind?: string | null;
  parentToolCallId?: string | null;
  contextPolicy?: string | null;
}

export interface ThreadExecutionDto {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: null | ThreadExecutionErrorDto;
  operations: Array<ThreadExecutionOperationDto>;
}

export interface ThreadExecutionErrorDto {
  type: string | null;
  message: string | null;
}

export interface ThreadExecutionOperationDto {
  operationId: string;
  providerOperationId: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId: string | null;
  version: number;
  registeredAt: string;
  startedAt: string | null;
  updatedAt: string;
  finishedAt: string | null;
  completionSummary: string | null;
  artifactReferences: Array<string> | null;
  failureCode: string | null;
  failureMessage: string | null;
  metadata: Record<string, string> | null;
}

export interface ThreadForkCompaction {
}

export interface ThreadForkCompactionApplyThreadForkCompaction {
  $type?: string | null;
  compaction: CompactionSpecification;
}

export interface ThreadForkCompactionDisableThreadForkCompaction {
  $type?: string | null;
}

export interface ThreadForkCompactionInheritThreadForkCompaction {
  $type?: string | null;
}

export interface ThreadForkGroupDto {
  id: string;
  sourceThreadId: string;
  forkedAtMessageId: string | null;
  forkedAtMessageIndex: number | null;
  choiceMessageIndex: number;
  members: Array<ThreadForkGroupMemberDto>;
}

export interface ThreadForkGroupMemberDto {
  threadId: string;
  name: string;
  index: number;
  isSource: boolean;
  choiceMessageId: string | null;
  choiceMessageIndex: number | null;
  messageCount: number;
  createdAt: string;
  lastActivity: string;
}

export interface ThreadForkResultDto {
  operationId: string;
  target: ThreadDto;
  sourceGeneration: number;
  sourceSequence: number;
  subAgentPolicy: number;
  status: number;
  children: Array<SubAgentForkChildOutcome>;
}

export interface ThreadGraphDto {
  threads: Array<ThreadDto>;
  forkGroups: Array<ThreadForkGroupDto>;
  runtimeChildren: Array<ThreadRuntimeChildDto>;
}

export interface ThreadJournalCursor {
  generation?: number | null;
  sequenceNumber?: number | null;
}

export interface ThreadKey {
  sessionId?: string | null;
  threadId?: string | null;
}

export interface ThreadProjectionPolicy {
  projectCommittedUserTurns?: boolean | null;
  projectCommittedAssistantOutputs?: boolean | null;
  projectInputContentMetadata?: boolean | null;
  projectRawInputMedia?: boolean | null;
}

export interface ThreadRuntimeChildDto {
  threadId: string;
  sessionId: string;
  defaultAgentId: string;
  parentSessionId: string;
  parentThreadId: string;
  name: string;
  kind: number;
  visibility: number;
  subAgentName: string | null;
  invocationId: string | null;
  subAgentSourceKind: string | null;
  parentToolCallId: string | null;
  contextPolicy: string | null;
  status: string | null;
  messageCount: number;
  createdAt: string;
  lastActivity: string;
}

export interface ToolCallContent {
}

export interface ToolCallContentBase {
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface ToolCallContentCodeInterpreterToolCallContent {
  $type: string;
  inputs?: Array<AIContent> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface ToolCallContentFunctionCallContent {
  $type: string;
  name: string;
  arguments?: unknown;
  informationalOnly?: boolean | null;
  callId: string;
  annotations?: unknown;
  additionalProperties?: unknown;
}

export interface ToolCallContentImageGenerationToolCallContent {
  $type: string;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface ToolCallContentMcpServerToolCallContent {
  $type: string;
  name: string;
  serverName: string | null;
  arguments?: unknown;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface ToolCallContentWebSearchToolCallContent {
  $type: string;
  queries?: Array<string> | null;
  callId: string;
  annotations?: Array<AIAnnotation> | null;
  additionalProperties?: unknown;
}

export interface ToolOutputConfig {
  enabled?: boolean | null;
  maxInlineResultBytes?: number | null;
  failureMode?: number | null;
  contentRetrievalEnabled?: boolean | null;
  retrievalLimits?: ContentRetrievalLimits | null;
}

export interface ToolSelectionConfig {
  toolMode?: string | null;
  requiredFunctionName?: string | null;
}

export interface ToolUsageSummary {
  totalCalls: number;
  permissionDeniedCount: number;
  permissionDeniedRate: number;
}

export interface TraceCapturePolicy {
  captureTraceRecords?: boolean | null;
  captureStructEventSamples?: boolean | null;
  captureRawMedia?: boolean | null;
  captureProviderPayloads?: boolean | null;
}

export interface UpdateAgentRequest {
  config: AgentConfig;
}

export interface UpdateSessionRequest {
  metadata: InlineUpdateSessionRequestMetadata;
}

export interface UpdateThreadRequest {
  name: string | null;
  description: string | null;
  tags: Array<string> | null;
  metadata?: unknown;
}

export interface UsageDetails {
  inputTokenCount?: number | null;
  outputTokenCount?: number | null;
  totalTokenCount?: number | null;
  cachedInputTokenCount?: number | null;
  reasoningTokenCount?: number | null;
  additionalCounts?: Record<string, number> | null;
}

export interface ValidationConfig {
  enableAsyncValidation?: boolean | null;
  timeoutMs?: number | null;
  failOnValidationError?: boolean | null;
}

export interface VoiceActivityClientConfig {
  provider?: null | ProviderReference | null;
  modelName?: string | null;
  endpoint?: string | null;
  customHeaders?: Record<string, string> | null;
}

export interface WriteScoreRequest {
  evaluatorName?: string | null;
  evaluatorVersion?: string | null;
  result?: null | EvaluationResult | null;
  source?: string | null;
  sessionId?: string | null;
  threadId?: string | null;
  turnIndex?: number | null;
  agentName?: string | null;
  turnDuration?: string | null;
  samplingRate?: number | null;
  policy?: string | null;
  createdAt?: string | null;
}

export interface ClientToolHarnessDefinition {
  name: string;
  description: string | null;
  tools: Array<ClientToolDefinition>;
  skills?: Array<ClientSkillDefinition> | null;
  functionResult?: string | null;
  systemPrompt?: string | null;
  startCollapsed?: boolean | null;
}

