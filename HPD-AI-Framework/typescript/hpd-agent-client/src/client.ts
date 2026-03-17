import type {
  AgentEvent,
  PermissionRequestEvent,
  ClarificationRequestEvent,
  ContinuationRequestEvent,
  ClientToolInvokeRequestEvent,
  clientToolKitsRegisteredEvent,
  PermissionChoice,
} from './types/events.js';
import { EventTypes } from './types/events.js';
import type { AgentTransport } from './types/transport.js';
import type {
  clientToolKitDefinition,
  ContextItem,
  ClientToolInvokeResponse,
} from './types/client-tools.js';
import type {
  Session,
  Branch,
  SiblingBranch,
  BranchMessage,
  AssetReference,
  CreateSessionRequest,
  UpdateSessionRequest,
  ListSessionsOptions,
  CreateBranchRequest,
  ForkBranchRequest,
} from './types/session.js';
import type {
  AgentSummaryDto,
  StoredAgentDto,
  CreateAgentRequest,
  UpdateAgentRequest,
} from './types/agent.js';
import type { RunConfig } from './types/run-config.js';
import type {
  ScoreRecord,
  EvaluatorSummary,
  RiskAutonomyDataPoint,
  ScoreTrend,
  PassRateResult,
  FailureRateResult,
  AgentComparisonResult,
  BranchComparisonResult,
  ToolUsageSummary,
  CostBreakdown,
} from './types/evals.js';
import { SseTransport } from './transports/sse.js';
import { WebSocketTransport } from './transports/websocket.js';
import { MauiTransport } from './transports/maui.js';

// ============================================
// Event Handler Interfaces
// ============================================

/**
 * Response to a permission request.
 */
export interface PermissionResponse {
  approved: boolean;
  choice?: PermissionChoice;
  reason?: string;
}

/**
 * Options for streaming.
 */
export interface StreamOptions {
  signal?: AbortSignal;
  /** Client tool groups to register */
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
  /** Run configuration (temperature, model overrides, etc.) */
  runConfig?: RunConfig;
  /** Agent definition ID to run (defaults to "default") */
  agentId?: string;
}

/**
 * Event handlers for the agent client.
 * All handlers are optional - only implement what you need.
 */
export interface EventHandlers {
  // ============================================
  // Content Handlers
  // ============================================

  /** Called for each text chunk */
  onTextDelta?: (text: string, messageId: string) => void;

  /** Called when a new message starts */
  onTextMessageStart?: (messageId: string, role: string) => void;

  /** Called when a message completes */
  onTextMessageEnd?: (messageId: string) => void;

  // ============================================
  // Tool Handlers
  // ============================================

  /** Called when a tool invocation starts */
  onToolCallStart?: (callId: string, name: string, messageId: string) => void;

  /** Called with tool arguments (JSON string) */
  onToolCallArgs?: (callId: string, argsJson: string) => void;

  /** Called when tool execution completes */
  onToolCallEnd?: (callId: string) => void;

  /** Called with tool result */
  onToolCallResult?: (callId: string, result: string) => void;

  // ============================================
  // Reasoning Handlers
  // ============================================

  /** Called for reasoning/thinking content */
  onReasoning?: (text: string, messageId: string) => void;

  // ============================================
  // Bidirectional Handlers (async for user interaction)
  // ============================================

  /**
   * Called when permission is required.
   * Return a PermissionResponse to approve/deny.
   */
  onPermissionRequest?: (request: PermissionRequestEvent) => Promise<PermissionResponse>;

  /**
   * Called when clarification is needed.
   * Return user's clarifying response.
   */
  onClarificationRequest?: (request: ClarificationRequestEvent) => Promise<string>;

  /**
   * Called when agent wants to continue (e.g., iteration limit).
   * Return true to continue, false to stop.
   */
  onContinuationRequest?: (request: ContinuationRequestEvent) => Promise<boolean>;

  // ============================================
  // Client Tool Handlers
  // ============================================

  /**
   * Called when a client tool needs to be invoked.
   * Implement this to execute tools in the browser/client context.
   * Return the tool result to send back to the agent.
   */
  onClientToolInvoke?: (request: ClientToolInvokeRequestEvent) => Promise<ClientToolInvokeResponse>;

  /**
   * Called when client tool groups are successfully registered.
   * Useful for debugging and UI updates.
   */
  onclientToolKitsRegistered?: (event: clientToolKitsRegisteredEvent) => void;

  // ============================================
  // Lifecycle Handlers
  // ============================================

  /** Called when an agent iteration starts */
  onTurnStart?: (iteration: number) => void;

  /** Called when an agent iteration ends */
  onTurnEnd?: (iteration: number) => void;

  /** Called when the entire message turn completes */
  onComplete?: () => void;

  /** Called on error */
  onError?: (message: string) => void;

  /** Called on middleware progress */
  onProgress?: (sourceName: string, message: string, percentComplete?: number) => void;

  // ============================================
  // Audio Event Handlers (TTS/STT/Voice)
  // ============================================

  /** Called when TTS synthesis starts */
  onSynthesisStarted?: (event: import('./types/events.js').SynthesisStartedEvent) => void;

  /** Called for each audio chunk during TTS */
  onAudioChunk?: (event: import('./types/events.js').AudioChunkEvent) => void;

  /** Called when TTS synthesis completes */
  onSynthesisCompleted?: (event: import('./types/events.js').SynthesisCompletedEvent) => void;

  /** Called with streaming transcription updates */
  onTranscriptionDelta?: (event: import('./types/events.js').TranscriptionDeltaEvent) => void;

  /** Called when transcription completes */
  onTranscriptionCompleted?: (event: import('./types/events.js').TranscriptionCompletedEvent) => void;

  /** Called when user interrupts agent speech */
  onUserInterrupted?: (event: import('./types/events.js').UserInterruptedEvent) => void;

  /** Called when speech is paused (potential interruption) */
  onSpeechPaused?: (event: import('./types/events.js').SpeechPausedEvent) => void;

  /** Called when paused speech resumes (false interruption) */
  onSpeechResumed?: (event: import('./types/events.js').SpeechResumedEvent) => void;

  /** Called when preemptive LLM generation starts */
  onPreemptiveGenerationStarted?: (event: import('./types/events.js').PreemptiveGenerationStartedEvent) => void;

  /** Called when preemptive generation is discarded */
  onPreemptiveGenerationDiscarded?: (event: import('./types/events.js').PreemptiveGenerationDiscardedEvent) => void;

  /** Called when voice activity detected (user starts speaking) */
  onVadStartOfSpeech?: (event: import('./types/events.js').VadStartOfSpeechEvent) => void;

  /** Called when voice activity ends (user stops speaking) */
  onVadEndOfSpeech?: (event: import('./types/events.js').VadEndOfSpeechEvent) => void;

  /** Called with audio pipeline metrics */
  onAudioPipelineMetrics?: (event: import('./types/events.js').AudioPipelineMetricsEvent) => void;

  /** Called when turn detected (user finished speaking) */
  onTurnDetected?: (event: import('./types/events.js').TurnDetectedEvent) => void;

  /** Called when filler audio is played */
  onFillerAudioPlayed?: (event: import('./types/events.js').FillerAudioPlayedEvent) => void;

  // ============================================
  // Raw Event Access
  // ============================================

  /** Called for every event (for logging, custom handling) */
  onEvent?: (event: AgentEvent) => void;
}

// ============================================
// Client Configuration
// ============================================

export type TransportType = 'sse' | 'websocket' | 'maui';

export interface AgentClientConfig {
  /** Base URL of the HPD-Agent API */
  baseUrl: string;

  /** Transport type (default: 'sse') */
  transport?: TransportType;

  /** Custom headers for requests (SSE only) */
  headers?: Record<string, string>;

  /** Client tool groups to register automatically on every stream */
  clientToolKits?: clientToolKitDefinition[];

  /** Default context items to include on every stream */
  defaulTMetadata?: ContextItem[];

  /** Handler for client tool invocations */
  onClientToolInvoke?: (request: ClientToolInvokeRequestEvent) => Promise<ClientToolInvokeResponse>;
}

// ============================================
// Agent Client
// ============================================

/**
 * Main client for interacting with HPD-Agent.
 * Provides typed event handlers and automatic transport management.
 */
export class AgentClient {
  private config: AgentClientConfig;
  private transport: AgentTransport;

  /**
   * Create a new AgentClient.
   * @param config Configuration object or base URL string
   */
  constructor(config: AgentClientConfig | string) {
    this.config = typeof config === 'string' ? { baseUrl: config } : config;
    this.transport = this.createTransport();
  }

  private createTransport(): AgentTransport {
    const type = this.config.transport ?? 'sse';

    switch (type) {
      case 'websocket':
        return new WebSocketTransport(this.config.baseUrl);
      case 'maui':
        return new MauiTransport();
      case 'sse':
      default:
        return new SseTransport(this.config.baseUrl);
    }
  }

  /**
   * Stream agent events with typed handlers.
   * Returns a promise that resolves on completion or rejects on error.
   *
   * @param sessionId The session to stream from
   * @param branchId The branch to stream from (default: 'main')
   * @param messages Messages to send to the agent
   * @param handlers Event handlers
   * @param options Streaming options (e.g., abort signal)
   */
  async stream(
    sessionId: string,
    branchId: string | undefined,
    messages: Array<{ content: string; role?: string }>,
    handlers: EventHandlers,
    options?: StreamOptions
  ): Promise<void> {
    return new Promise((resolve, reject) => {
      let completed = false;

      const complete = () => {
        if (!completed) {
          completed = true;
          resolve();
        }
      };

      const fail = (error: Error) => {
        if (!completed) {
          completed = true;
          reject(error);
        }
      };

      // Event queue for sequential processing
      const eventQueue: AgentEvent[] = [];
      let processing = false;

      // Process events sequentially to ensure proper reactivity
      const processQueue = async () => {
        if (processing || eventQueue.length === 0) return;

        processing = true;
        try {
          while (eventQueue.length > 0) {
            const event = eventQueue.shift()!;

            // Call raw handler first (synchronous)
            handlers.onEvent?.(event);

            // Dispatch to typed handlers (await to ensure sequential processing)
            try {
              await this.dispatchEvent(event, handlers, complete, fail);
            } catch (error) {
              fail(error as Error);
              break;
            }
          }
        } finally {
          processing = false;
        }
      };

      // Set up event dispatching - queue events for sequential processing
      this.transport.onEvent((event) => {
        eventQueue.push(event);
        // Process queue asynchronously but don't await
        processQueue().catch(fail);
      });

      this.transport.onError((error) => {
        handlers.onError?.(error.message);
        fail(error);
      });

      this.transport.onClose(() => {
        complete();
      });

      // Merge config defaults with stream options
      const mergedToolKits = [
        ...(this.config.clientToolKits ?? []),
        ...(options?.clientToolKits ?? []),
      ];
      const mergedContext = [
        ...(this.config.defaulTMetadata ?? []),
        ...(options?.context ?? []),
      ];

      // Connect
      this.transport
        .connect({
          sessionId,
          branchId: branchId || 'main',
          messages,
          signal: options?.signal,
          clientToolKits: mergedToolKits.length > 0 ? mergedToolKits : undefined,
          context: mergedContext.length > 0 ? mergedContext : undefined,
          state: options?.state,
          expandedContainers: options?.expandedContainers,
          hiddenTools: options?.hiddenTools,
          resetClientState: options?.resetClientState,
          runConfig: options?.runConfig,
          agentId: options?.agentId,
        })
        .catch(fail);
    });
  }

  private async dispatchEvent(
    event: AgentEvent,
    handlers: EventHandlers,
    complete: () => void,
    fail: (error: Error) => void
  ): Promise<void> {
    try {
      switch (event.type) {
        // Content events
        case EventTypes.TEXT_DELTA:
          handlers.onTextDelta?.(event.text, event.messageId);
          break;

        case EventTypes.TEXT_MESSAGE_START:
          handlers.onTextMessageStart?.(event.messageId, event.role);
          break;

        case EventTypes.TEXT_MESSAGE_END:
          handlers.onTextMessageEnd?.(event.messageId);
          break;

        // Tool events
        case EventTypes.TOOL_CALL_START:
          handlers.onToolCallStart?.(event.callId, event.name, event.messageId);
          break;

        case EventTypes.TOOL_CALL_ARGS:
          handlers.onToolCallArgs?.(event.callId, event.argsJson);
          break;

        case EventTypes.TOOL_CALL_END:
          handlers.onToolCallEnd?.(event.callId);
          break;

        case EventTypes.TOOL_CALL_RESULT:
          handlers.onToolCallResult?.(event.callId, event.result);
          break;

        // Reasoning
        case EventTypes.REASONING_DELTA:
          handlers.onReasoning?.(event.text, event.messageId);
          break;

        // Bidirectional - Permission
        case EventTypes.PERMISSION_REQUEST:
          if (handlers.onPermissionRequest) {
            const response = await handlers.onPermissionRequest(event);
            await this.transport.send({
              type: 'permission_response',
              permissionId: event.permissionId,
              approved: response.approved,
              choice: response.choice,
              reason: response.reason,
            });
          }
          break;

        // Bidirectional - Clarification
        case EventTypes.CLARIFICATION_REQUEST:
          if (handlers.onClarificationRequest) {
            const answer = await handlers.onClarificationRequest(event);
            await this.transport.send({
              type: 'clarification_response',
              clarificationId: event.requestId,
              response: answer,
            });
          }
          break;

        // Bidirectional - Continuation
        case EventTypes.CONTINUATION_REQUEST:
          if (handlers.onContinuationRequest) {
            const shouldContinue = await handlers.onContinuationRequest(event);
            await this.transport.send({
              type: 'continuation_response',
              continuationId: event.continuationId,
              shouldContinue,
            });
          }
          break;

        // Bidirectional - Client Tool Invocation
        case EventTypes.CLIENT_TOOL_INVOKE_REQUEST:
          // Use handler from stream() or fall back to config handler
          const toolHandler = handlers.onClientToolInvoke ?? this.config.onClientToolInvoke;
          if (toolHandler) {
            const toolResponse = await toolHandler(event);
            await this.transport.send({
              type: 'client_tool_response',
              requestId: toolResponse.requestId,
              content: toolResponse.content,
              success: toolResponse.success,
              errorMessage: toolResponse.errorMessage,
              augmentation: toolResponse.augmentation,
            });
          }
          break;

        // Client Tool Groups Registered (informational)
        case EventTypes.CLIENT_TOOL_GROUPS_REGISTERED:
          handlers.onclientToolKitsRegistered?.(event);
          break;

        // Lifecycle
        case EventTypes.AGENT_TURN_STARTED:
          handlers.onTurnStart?.(event.iteration);
          break;

        case EventTypes.AGENT_TURN_FINISHED:
          handlers.onTurnEnd?.(event.iteration);
          break;

        // Middleware progress
        case EventTypes.MIDDLEWARE_PROGRESS:
          handlers.onProgress?.(event.sourceName, event.message, event.percentComplete);
          break;

        // Audio Events (TTS)
        case EventTypes.SYNTHESIS_STARTED:
          handlers.onSynthesisStarted?.(event);
          break;

        case EventTypes.AUDIO_CHUNK:
          handlers.onAudioChunk?.(event);
          break;

        case EventTypes.SYNTHESIS_COMPLETED:
          handlers.onSynthesisCompleted?.(event);
          break;

        // Audio Events (STT)
        case EventTypes.TRANSCRIPTION_DELTA:
          handlers.onTranscriptionDelta?.(event);
          break;

        case EventTypes.TRANSCRIPTION_COMPLETED:
          handlers.onTranscriptionCompleted?.(event);
          break;

        // Audio Events (Interruption)
        case EventTypes.USER_INTERRUPTED:
          handlers.onUserInterrupted?.(event);
          break;

        case EventTypes.SPEECH_PAUSED:
          handlers.onSpeechPaused?.(event);
          break;

        case EventTypes.SPEECH_RESUMED:
          handlers.onSpeechResumed?.(event);
          break;

        // Audio Events (Preemptive Generation)
        case EventTypes.PREEMPTIVE_GENERATION_STARTED:
          handlers.onPreemptiveGenerationStarted?.(event);
          break;

        case EventTypes.PREEMPTIVE_GENERATION_DISCARDED:
          handlers.onPreemptiveGenerationDiscarded?.(event);
          break;

        // Audio Events (VAD)
        case EventTypes.VAD_START_OF_SPEECH:
          handlers.onVadStartOfSpeech?.(event);
          break;

        case EventTypes.VAD_END_OF_SPEECH:
          handlers.onVadEndOfSpeech?.(event);
          break;

        // Audio Events (Metrics/Turn/Filler)
        case EventTypes.AUDIO_PIPELINE_METRICS:
          handlers.onAudioPipelineMetrics?.(event);
          break;

        case EventTypes.TURN_DETECTED:
          handlers.onTurnDetected?.(event);
          break;

        case EventTypes.FILLER_AUDIO_PLAYED:
          handlers.onFillerAudioPlayed?.(event);
          break;

        // Terminal events
        case EventTypes.MESSAGE_TURN_FINISHED:
          handlers.onComplete?.();
          complete();
          break;

        case EventTypes.MESSAGE_TURN_ERROR:
          handlers.onError?.(event.message);
          fail(new Error(event.message));
          break;
      }
    } catch (error) {
      fail(error as Error);
    }
  }

  /**
   * Abort the current stream.
   */
  abort(): void {
    this.transport.disconnect();
  }

  /**
   * Check if currently streaming.
   */
  get streaming(): boolean {
    return this.transport.connected;
  }

  // ============================================
  // Client Tool Group Management
  // ============================================

  /**
   * Register a client tool group. It will be automatically included in all future streams.
   */
  registerToolKit(ToolKit: clientToolKitDefinition): void {
    if (!this.config.clientToolKits) {
      this.config.clientToolKits = [];
    }
    // Remove existing tool group with same name (update)
    this.config.clientToolKits = this.config.clientToolKits.filter(g => g.name !== ToolKit.name);
    this.config.clientToolKits.push(ToolKit);
  }

  /**
   * Register multiple client tool groups.
   */
  registerToolKits(ToolKits: clientToolKitDefinition[]): void {
    ToolKits.forEach(g => this.registerToolKit(g));
  }

  /**
   * Unregister a client tool group by name.
   */
  unregisterToolKit(ToolKitName: string): void {
    if (this.config.clientToolKits) {
      this.config.clientToolKits = this.config.clientToolKits.filter(g => g.name !== ToolKitName);
    }
  }

  /**
   * Get all registered tool groups.
   */
  get ToolKits(): clientToolKitDefinition[] {
    return this.config.clientToolKits ?? [];
  }

  /**
   * Set the handler for client tool invocations.
   */
  setToolHandler(handler: (request: ClientToolInvokeRequestEvent) => Promise<ClientToolInvokeResponse>): void {
    this.config.onClientToolInvoke = handler;
  }

  // ============================================
  // Session CRUD
  // ============================================

  listSessions(options?: ListSessionsOptions): Promise<Session[]> {
    return this.transport.listSessions(options);
  }

  getSession(sessionId: string): Promise<Session | null> {
    return this.transport.getSession(sessionId);
  }

  createSession(options?: CreateSessionRequest): Promise<Session> {
    return this.transport.createSession(options);
  }

  updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session> {
    return this.transport.updateSession(sessionId, request);
  }

  deleteSession(sessionId: string): Promise<void> {
    return this.transport.deleteSession(sessionId);
  }

  // ============================================
  // Branch CRUD
  // ============================================

  listBranches(sessionId: string): Promise<Branch[]> {
    return this.transport.listBranches(sessionId);
  }

  getBranch(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.transport.getBranch(sessionId, branchId);
  }

  createBranch(sessionId: string, options?: CreateBranchRequest): Promise<Branch> {
    return this.transport.createBranch(sessionId, options);
  }

  forkBranch(sessionId: string, branchId: string, options: ForkBranchRequest): Promise<Branch> {
    return this.transport.forkBranch(sessionId, branchId, options);
  }

  deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    return this.transport.deleteBranch(sessionId, branchId, options);
  }

  getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]> {
    return this.transport.getBranchMessages(sessionId, branchId);
  }

  // ============================================
  // Sibling Navigation
  // ============================================

  getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]> {
    return this.transport.getBranchSiblings(sessionId, branchId);
  }

  getNextSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.transport.getNextSibling(sessionId, branchId);
  }

  getPreviousSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.transport.getPreviousSibling(sessionId, branchId);
  }

  // ============================================
  // Agent Definition CRUD
  // ============================================

  listAgents(): Promise<AgentSummaryDto[]> {
    return this.transport.listAgents();
  }

  getAgent(agentId: string): Promise<StoredAgentDto | null> {
    return this.transport.getAgent(agentId);
  }

  createAgent(request: CreateAgentRequest): Promise<StoredAgentDto> {
    return this.transport.createAgent(request);
  }

  updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto> {
    return this.transport.updateAgent(agentId, request);
  }

  deleteAgent(agentId: string): Promise<void> {
    return this.transport.deleteAgent(agentId);
  }

  // ============================================
  // Eval Queries
  // ============================================

  getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]> {
    return this.transport.getScores(evaluatorName, from, to);
  }

  getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]> {
    return this.transport.getScoresByBranch(sessionId, branchId);
  }

  writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord> {
    return this.transport.writeScore(record);
  }

  getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]> {
    return this.transport.getEvaluatorSummary(from, to);
  }

  getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]> {
    return this.transport.getRiskAutonomyDistribution(from, to);
  }

  getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend> {
    return this.transport.getTrend(evaluatorName, from, to, bucketSize);
  }

  getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult> {
    return this.transport.getPassRate(evaluatorName, from, to);
  }

  getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult> {
    return this.transport.getFailureRate(evaluatorName, from, to);
  }

  getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult> {
    return this.transport.getAgentComparison(evaluatorName, agentNames, from, to);
  }

  getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult> {
    return this.transport.getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames);
  }

  getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>> {
    return this.transport.getToolUsage(from, to);
  }

  getCost(from?: string, to?: string): Promise<CostBreakdown> {
    return this.transport.getCost(from, to);
  }

  getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]> {
    return this.transport.getScoresByVersion(evaluatorName, version);
  }

  uploadAsset(sessionId: string, file: File | Blob, name?: string): Promise<AssetReference> {
    return this.transport.uploadAsset(sessionId, file, name);
  }
}
