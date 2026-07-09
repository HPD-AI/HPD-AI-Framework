import type {
  AgentEvent,
  AgentEventOfType,
  AgentRunInputEvent,
  ClientToolInvokeRequestEvent,
  KnownAgentEvent,
} from './types/events.js';
import { EventTypes } from './types/events.js';
import type { AgentTransport, RuntimeScope, RunTransportOptions, SubmitInputResult } from './types/transport.js';
import type {
  Session,
  Thread,
  ThreadMessage,
  ThreadGraph,
  ThreadEvent,
  ContentReference,
  CreateSessionRequest,
  SearchSessionsRequest,
  UpdateSessionRequest,
  ListSessionsOptions,
  CreateThreadRequest,
  ForkThreadRequest,
  UpdateThreadRequest,
} from './types/session.js';
import type { ThreadRun } from './types/thread-run.js';
import type {
  ContextUsageRequest,
  ThreadContextUsage,
} from './types/context-usage.js';
import type {
  AgentSummaryDto,
  StoredAgentDto,
  CreateAgentRequest,
  UpdateAgentRequest,
} from './types/agent.js';
import type {
  ScoreRecord,
  EvaluatorSummary,
  RiskAutonomyDataPoint,
  ScoreTrend,
  PassRateResult,
  FailureRateResult,
  AgentComparisonResult,
  ThreadComparisonResult,
  ToolUsageSummary,
  CostBreakdown,
} from './types/evals.js';
import { SseTransport } from './transports/sse.js';
import { WebSocketTransport } from './transports/websocket.js';
import type { TransportRequestOptions } from './transports/options.js';
import { AgentHttpApi } from './api.js';
import { ChatManager } from './chat.js';
import { ClientToolRegistry } from './tools.js';

export type MaybePromise<T> = T | Promise<T>;

export interface EventSubscription {
  dispose(): void;
}

export type AgentEventHandler<TEvent extends AgentEvent> =
  (event: TEvent) => MaybePromise<void>;

// ============================================
// Client Configuration
// ============================================

export type TransportType = 'sse' | 'websocket';

export interface AgentClientConfig {
  /** Base URL of the HPD-Agent API */
  baseUrl: string;

  /** Transport type (default: 'sse') */
  transport?: TransportType;

  /** Custom headers for HTTP requests */
  headers?: Record<string, string>;

  /** Fetch credentials mode for HTTP requests. Use 'include' for cookie-backed auth. */
  credentials?: RequestCredentials;

  /** Stable responder identity for targeted request sessions. */
  responderId?: string;

  /** Responder groups this client belongs to. */
  responderGroups?: string[];

  /** Capabilities available to this client. */
  capabilities?: string[];

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
  readonly api: AgentHttpApi;
  readonly tools = new ClientToolRegistry();
  readonly chat: ChatManager;
  private typedHandlers = new Map<string, Set<AgentEventHandler<AgentEvent>>>();
  private anyHandlers = new Set<AgentEventHandler<AgentEvent>>();
  private errorHandlers = new Set<(error: Error) => MaybePromise<void>>();
  private outputDispatchQueue: Promise<void> = Promise.resolve();

  /**
   * Create a new AgentClient.
   * @param config Configuration object or base URL string
   */
  constructor(config: AgentClientConfig | string) {
    this.config = typeof config === 'string' ? { baseUrl: config } : config;
    const requestOptions: TransportRequestOptions = {
      headers: this.config.headers,
      credentials: this.config.credentials,
    };
    this.api = new AgentHttpApi(this.config.baseUrl, requestOptions);
    this.chat = new ChatManager(this);
    this.transport = this.createTransport();
    this.transport.onEvent((event) => {
      this.outputDispatchQueue = this.outputDispatchQueue.then(() => this.dispatchOutputEvent(event));
    });
    this.transport.onError((error) => {
      void this.dispatchError(error);
    });
  }

  private createTransport(): AgentTransport {
    const type = this.config.transport ?? 'sse';
    const requestOptions: TransportRequestOptions = {
      headers: this.config.headers,
      credentials: this.config.credentials,
    };

    switch (type) {
      case 'websocket':
        return new WebSocketTransport(this.config.baseUrl);
      case 'sse':
      default:
        return new SseTransport(this.config.baseUrl, requestOptions);
    }
  }

  async start(scope?: RuntimeScope): Promise<void> {
    await this.transport.connect(scope);
  }

  async stop(): Promise<void> {
    this.transport.disconnect();
  }

  async submitInput(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<SubmitInputResult> {
    const result = await this.transport.submitInput(input, options);
    await this.outputDispatchQueue;
    return result;
  }

  async run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<SubmitInputResult> {
    return this.submitInput(input, options);
  }

  abort(): void {
    this.transport.disconnect();
  }

  on<TType extends KnownAgentEvent['type']>(
    type: TType,
    handler: AgentEventHandler<AgentEventOfType<TType>>
  ): EventSubscription {
    const handlers = this.typedHandlers.get(type) ?? new Set<AgentEventHandler<AgentEvent>>();
    const stored = handler as AgentEventHandler<AgentEvent>;
    handlers.add(stored);
    this.typedHandlers.set(type, handlers);

    return {
      dispose: () => {
        handlers.delete(stored);
        if (handlers.size === 0) {
          this.typedHandlers.delete(type);
        }
      },
    };
  }

  onAny(handler: AgentEventHandler<AgentEvent>): EventSubscription {
    this.anyHandlers.add(handler);
    return {
      dispose: () => {
        this.anyHandlers.delete(handler);
      },
    };
  }

  onError(handler: (error: Error) => MaybePromise<void>): EventSubscription {
    this.errorHandlers.add(handler);
    return {
      dispose: () => {
        this.errorHandlers.delete(handler);
      },
    };
  }

  private async dispatchOutputEvent(event: AgentEvent): Promise<void> {
    const typedHandlers = this.typedHandlers.get(event.type);
    if (typedHandlers) {
      for (const handler of typedHandlers) {
        await handler(event);
      }
    }

    for (const handler of this.anyHandlers) {
      await handler(event);
    }

    if (event.type === EventTypes.CLIENT_TOOL_INVOKE_REQUEST &&
      this.matchesResponderTarget(event as ClientToolInvokeRequestEvent)) {
      const toolResponse = await this.tools.handleInvoke(event as ClientToolInvokeRequestEvent);
      await this.transport.submitInput({
        type: EventTypes.CLIENT_TOOL_INVOKE_OUTCOME,
        requestId: toolResponse.requestId,
        responderId: this.config.responderId,
        responderGroup: this.config.responderGroups?.[0],
        capabilities: this.responderCapabilities(),
        outcome: toolResponse.outcome,
        content: toolResponse.content,
        errorMessage: toolResponse.errorMessage,
        clientOperationId: toolResponse.clientOperationId,
        handleKind: toolResponse.handleKind,
        supportedOperations: toolResponse.supportedOperations,
        augmentation: toolResponse.augmentation,
      });
    }
  }

  private matchesResponderTarget(request: ClientToolInvokeRequestEvent): boolean {
    if (!isTargetedResponderPolicy(request.responsePolicy) || !request.target) {
      return true;
    }

    const target = request.target;
    if (target.responderId && target.responderId !== this.config.responderId) {
      return false;
    }

    const groups = new Set(this.config.responderGroups ?? []);
    if (target.responderGroup && !groups.has(target.responderGroup)) {
      return false;
    }

    const capabilities = new Set(this.responderCapabilities());
    return (target.requiredCapabilities ?? []).every((capability) => capabilities.has(capability));
  }

  private responderCapabilities(): string[] {
    return [...new Set([
      ...(this.config.capabilities ?? []),
      ...this.tools.capabilities,
    ])];
  }

  private async dispatchError(error: Error): Promise<void> {
    for (const handler of this.errorHandlers) {
      await handler(error);
    }
  }

  /**
   * Disconnect the active transport observer.
   */
  disconnectLive(): void {
    this.transport.disconnect();
  }

  /**
   * Check if the live observer transport is connected.
   */
  get connected(): boolean {
    return this.transport.connected;
  }

  // ============================================
  // Session CRUD
  // ============================================

  listSessions(options?: ListSessionsOptions): Promise<Session[]> {
    return this.api.listSessions(options);
  }

  searchSessions(request?: SearchSessionsRequest): Promise<Session[]> {
    return this.api.searchSessions(request);
  }

  getSession(sessionId: string): Promise<Session | null> {
    return this.api.getSession(sessionId);
  }

  createSession(options?: CreateSessionRequest): Promise<Session> {
    return this.api.createSession(options);
  }

  updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session> {
    return this.api.updateSession(sessionId, request);
  }

  deleteSession(sessionId: string): Promise<void> {
    return this.api.deleteSession(sessionId);
  }

  // ============================================
  // Thread CRUD
  // ============================================

  listThreads(sessionId: string): Promise<Thread[]> {
    return this.api.listThreads(sessionId);
  }

  getThread(sessionId: string, threadId: string): Promise<Thread | null> {
    return this.api.getThread(sessionId, threadId);
  }

  createThread(sessionId: string, options?: CreateThreadRequest): Promise<Thread> {
    return this.api.createThread(sessionId, options);
  }

  forkThread(sessionId: string, threadId: string, options: ForkThreadRequest): Promise<Thread> {
    return this.api.forkThread(sessionId, threadId, options);
  }

  updateThread(sessionId: string, threadId: string, request: UpdateThreadRequest): Promise<Thread> {
    return this.api.updateThread(sessionId, threadId, request);
  }

  deleteThread(sessionId: string, threadId: string, options?: { recursive?: boolean }): Promise<void> {
    return this.api.deleteThread(sessionId, threadId, options);
  }

  getThreadEvents(sessionId: string, threadId: string): Promise<ThreadEvent[]> {
    return this.api.getThreadEvents(sessionId, threadId);
  }

  getThreadMessages(sessionId: string, threadId: string): Promise<ThreadMessage[]> {
    return this.api.getThreadMessages(sessionId, threadId);
  }

  getThreadRuns(agentId: string, sessionId: string, threadId: string): Promise<ThreadRun[]> {
    return this.api.getThreadRuns(agentId, sessionId, threadId);
  }

  getActiveThreadRun(agentId: string, sessionId: string, threadId: string): Promise<ThreadRun | null> {
    return this.api.getActiveThreadRun(agentId, sessionId, threadId);
  }

  estimateContextUsage(
    agentId: string,
    sessionId: string,
    threadId: string,
    request?: ContextUsageRequest,
  ): Promise<ThreadContextUsage> {
    return this.api.estimateContextUsage(agentId, sessionId, threadId, request);
  }

  getThreadRun(agentId: string, sessionId: string, threadId: string, runtimeRunId: string): Promise<ThreadRun | null> {
    return this.api.getThreadRun(agentId, sessionId, threadId, runtimeRunId);
  }

  getThreadGraph(sessionId: string): Promise<ThreadGraph> {
    return this.api.getThreadGraph(sessionId);
  }

  // ============================================
  // Agent Definition CRUD
  // ============================================

  listAgents(): Promise<AgentSummaryDto[]> {
    return this.api.listAgents();
  }

  getAgent(agentId: string): Promise<StoredAgentDto | null> {
    return this.api.getAgent(agentId);
  }

  createAgent(request: CreateAgentRequest): Promise<StoredAgentDto> {
    return this.api.createAgent(request);
  }

  updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto> {
    return this.api.updateAgent(agentId, request);
  }

  deleteAgent(agentId: string): Promise<void> {
    return this.api.deleteAgent(agentId);
  }

  // ============================================
  // Eval Queries
  // ============================================

  getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]> {
    return this.api.getScores(evaluatorName, from, to);
  }

  getScoresByThread(sessionId: string, threadId?: string): Promise<ScoreRecord[]> {
    return this.api.getScoresByThread(sessionId, threadId);
  }

  writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord> {
    return this.api.writeScore(record);
  }

  getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]> {
    return this.api.getEvaluatorSummary(from, to);
  }

  getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]> {
    return this.api.getRiskAutonomyDistribution(from, to);
  }

  getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend> {
    return this.api.getTrend(evaluatorName, from, to, bucketSize);
  }

  getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult> {
    return this.api.getPassRate(evaluatorName, from, to);
  }

  getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult> {
    return this.api.getFailureRate(evaluatorName, from, to);
  }

  getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult> {
    return this.api.getAgentComparison(evaluatorName, agentNames, from, to);
  }

  getThreadComparison(sessionId: string, threadId1: string, threadId2: string, evaluatorNames: string[]): Promise<ThreadComparisonResult> {
    return this.api.getThreadComparison(sessionId, threadId1, threadId2, evaluatorNames);
  }

  getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>> {
    return this.api.getToolUsage(from, to);
  }

  getCost(from?: string, to?: string): Promise<CostBreakdown> {
    return this.api.getCost(from, to);
  }

  getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]> {
    return this.api.getScoresByVersion(evaluatorName, version);
  }

  uploadContent(sessionId: string, threadId: string, file: File | Blob, name?: string): Promise<ContentReference> {
    return this.api.uploadContent(sessionId, threadId, file, name);
  }
}

function isTargetedResponderPolicy(value: unknown): boolean {
  return value === 'targetedResponder' ||
    value === 'TargetedResponder' ||
    value === 1;
}
