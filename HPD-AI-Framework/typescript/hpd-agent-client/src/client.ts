import type {
  AgentEvent,
  AgentEventOfType,
  AgentRunInputEvent,
  ClientToolInvokeRequestEvent,
  KnownAgentEvent,
} from './types/events.js';
import { EventTypes } from './types/events.js';
import type { AgentTransport, RuntimeScope, RunTransportOptions } from './types/transport.js';
import type {
  Session,
  Branch,
  BranchMessage,
  SiblingBranch,
  BranchEvent,
  ContentReference,
  CreateSessionRequest,
  SearchSessionsRequest,
  UpdateSessionRequest,
  ListSessionsOptions,
  CreateBranchRequest,
  ForkBranchRequest,
  UpdateBranchRequest,
} from './types/session.js';
import type { BranchRun } from './types/branch-run.js';
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
  BranchComparisonResult,
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

  async submitInput(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void> {
    await this.transport.submitInput(input, options);
    await this.outputDispatchQueue;
  }

  async run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void> {
    await this.submitInput(input, options);
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

    if (event.type === EventTypes.CLIENT_TOOL_INVOKE_REQUEST) {
      const toolResponse = await this.tools.handleInvoke(event as ClientToolInvokeRequestEvent);
      await this.transport.submitInput({
        type: EventTypes.CLIENT_TOOL_INVOKE_RESPONSE,
        requestId: toolResponse.requestId,
        content: toolResponse.content,
        success: toolResponse.success,
        errorMessage: toolResponse.errorMessage,
        augmentation: toolResponse.augmentation,
      });
    }
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
  // Branch CRUD
  // ============================================

  listBranches(sessionId: string): Promise<Branch[]> {
    return this.api.listBranches(sessionId);
  }

  getBranch(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.api.getBranch(sessionId, branchId);
  }

  createBranch(sessionId: string, options?: CreateBranchRequest): Promise<Branch> {
    return this.api.createBranch(sessionId, options);
  }

  forkBranch(sessionId: string, branchId: string, options: ForkBranchRequest): Promise<Branch> {
    return this.api.forkBranch(sessionId, branchId, options);
  }

  updateBranch(sessionId: string, branchId: string, request: UpdateBranchRequest): Promise<Branch> {
    return this.api.updateBranch(sessionId, branchId, request);
  }

  deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    return this.api.deleteBranch(sessionId, branchId, options);
  }

  getBranchEvents(sessionId: string, branchId: string): Promise<BranchEvent[]> {
    return this.api.getBranchEvents(sessionId, branchId);
  }

  getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]> {
    return this.api.getBranchMessages(sessionId, branchId);
  }

  getBranchRuns(agentId: string, sessionId: string, branchId: string): Promise<BranchRun[]> {
    return this.api.getBranchRuns(agentId, sessionId, branchId);
  }

  getActiveBranchRun(agentId: string, sessionId: string, branchId: string): Promise<BranchRun | null> {
    return this.api.getActiveBranchRun(agentId, sessionId, branchId);
  }

  getBranchRun(agentId: string, sessionId: string, branchId: string, runtimeRunId: string): Promise<BranchRun | null> {
    return this.api.getBranchRun(agentId, sessionId, branchId, runtimeRunId);
  }

  // ============================================
  // Sibling Navigation
  // ============================================

  getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]> {
    return this.api.getBranchSiblings(sessionId, branchId);
  }

  getNextSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.api.getNextSibling(sessionId, branchId);
  }

  getPreviousSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    return this.api.getPreviousSibling(sessionId, branchId);
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

  getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]> {
    return this.api.getScoresByBranch(sessionId, branchId);
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

  getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult> {
    return this.api.getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames);
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

  uploadContent(sessionId: string, branchId: string, file: File | Blob, name?: string): Promise<ContentReference> {
    return this.api.uploadContent(sessionId, branchId, file, name);
  }
}
