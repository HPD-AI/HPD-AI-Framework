import type {
  AgentEvent,
  AgentEventOfType,
  AgentRunInputEvent,
  ClientToolInvokeRequestEvent,
} from './types/events.js';
import { EventTypes } from './types/events.js';
import type { AgentTransport, RuntimeScope, RunTransportOptions } from './types/transport.js';
import type {
  clientToolKitDefinition,
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

export type MaybePromise<T> = T | Promise<T>;

export interface EventSubscription {
  dispose(): void;
}

export type AgentEventHandler<TEvent extends AgentEvent> =
  (event: TEvent) => MaybePromise<void>;

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

  /** Client tool groups registered locally for browser-side invocation */
  clientToolKits?: clientToolKitDefinition[];

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

  async start(scope?: RuntimeScope): Promise<void> {
    await this.transport.connect(scope);
  }

  async stop(): Promise<void> {
    this.transport.disconnect();
  }

  async run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void> {
    await this.transport.run(input, options);
    await this.outputDispatchQueue;
  }

  on<TType extends AgentEvent['type']>(
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

    if (event.type === EventTypes.CLIENT_TOOL_INVOKE_REQUEST && this.config.onClientToolInvoke) {
      const toolResponse = await this.config.onClientToolInvoke(event);
      await this.transport.run({
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
