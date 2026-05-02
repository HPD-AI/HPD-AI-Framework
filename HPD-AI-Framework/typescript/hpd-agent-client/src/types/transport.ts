import type { AgentEvent, AgentRunInputEvent } from './events.js';
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
} from './session.js';
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
} from './evals.js';
import type {
  AgentSummaryDto,
  StoredAgentDto,
  CreateAgentRequest,
  UpdateAgentRequest,
} from './agent.js';

/**
 * Runtime connection scope for long-lived transports such as WebSocket.
 */
export interface RuntimeScope {
  /** Session ID for scoped transports */
  sessionId?: string;
  /** Branch ID for scoped transports (default: 'main') */
  branchId?: string;
  /** Optional AbortSignal for cancellation */
  signal?: AbortSignal;
  /** Agent definition ID to run when the input event omits agentId */
  agentId?: string;
}

export interface RunTransportOptions {
  signal?: AbortSignal;
}

/**
 * Abstract transport interface.
 * Implementations handle the specifics of SSE, WebSocket, etc.
 */
export interface AgentTransport {
  /** Connect/start a long-lived runtime transport. SSE transports may no-op. */
  connect(scope?: RuntimeScope): Promise<void>;

  /** Send an agent input event. */
  run(event: AgentRunInputEvent, options?: RunTransportOptions): Promise<void>;

  /** Register event handler */
  onEvent(handler: (event: AgentEvent) => void): void;

  /** Register error handler */
  onError(handler: (error: Error) => void): void;

  /** Register close handler */
  onClose(handler: () => void): void;

  /** Disconnect */
  disconnect(): void;

  /** Connection state */
  readonly connected: boolean;

  // ============================================
  // SESSION CRUD (V3)
  // ============================================

  /** List all sessions */
  listSessions(options?: ListSessionsOptions): Promise<Session[]>;

  /** Get a session by ID */
  getSession(sessionId: string): Promise<Session | null>;

  /** Create a new session */
  createSession(options?: CreateSessionRequest): Promise<Session>;

  /** Update session metadata */
  updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session>;

  /** Delete a session and all its branches */
  deleteSession(sessionId: string): Promise<void>;

  // ============================================
  // BRANCH CRUD (V3)
  // ============================================

  /** List all branches in a session */
  listBranches(sessionId: string): Promise<Branch[]>;

  /** Get a branch by ID */
  getBranch(sessionId: string, branchId: string): Promise<Branch | null>;

  /** Create a new branch in a session */
  createBranch(sessionId: string, options?: CreateBranchRequest): Promise<Branch>;

  /** Fork a branch at a specific message index */
  forkBranch(sessionId: string, branchId: string, options: ForkBranchRequest): Promise<Branch>;

  /** Delete a branch. Pass recursive: true to delete the entire subtree (must be enabled server-side via AllowRecursiveBranchDelete). */
  deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void>;

  /** Get all messages in a branch */
  getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]>;

  // ============================================
  // SIBLING NAVIGATION (V3)
  // ============================================

  /** Get sibling branches (ordered by siblingIndex) */
  getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]>;

  /** Get the next sibling branch (null if last) */
  getNextSibling(sessionId: string, branchId: string): Promise<Branch | null>;

  /** Get the previous sibling branch (null if first) */
  getPreviousSibling(sessionId: string, branchId: string): Promise<Branch | null>;

  // ============================================
  // AGENT DEFINITION CRUD
  // ============================================

  /** List all agent definitions */
  listAgents(): Promise<AgentSummaryDto[]>;

  /** Get an agent definition by ID (null if not found) */
  getAgent(agentId: string): Promise<StoredAgentDto | null>;

  /** Create a new agent definition */
  createAgent(request: CreateAgentRequest): Promise<StoredAgentDto>;

  /** Update an agent definition */
  updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto>;

  /** Delete an agent definition */
  deleteAgent(agentId: string): Promise<void>;

  // ============================================
  // EVAL QUERIES
  // ============================================

  /** Query scores by evaluator name and optional time range */
  getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]>;

  /** Query scores for a session, optionally filtered to a branch */
  getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]>;

  /** Write a score record; server assigns the id */
  writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord>;

  /** Summary statistics for all evaluators in an optional time range */
  getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]>;

  /** Paired risk/autonomy data points for every turn where both scores exist */
  getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]>;

  /** Time-bucketed trend for an evaluator (bucketSize is ISO 8601 duration, e.g. "PT1H") */
  getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend>;

  /** Pass rate for an evaluator in an optional time range */
  getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult>;

  /** Failure rate for an evaluator in an optional time range */
  getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult>;

  /** Compare score aggregates across multiple agents for a given evaluator */
  getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult>;

  /** Compare scores between two branches on given evaluators */
  getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult>;

  /** Tool usage summary keyed by tool name */
  getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>>;

  /** Cost breakdown keyed by cost category */
  getCost(from?: string, to?: string): Promise<CostBreakdown>;

  /** Query scores for a specific evaluator version */
  getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]>;

  // ============================================
  // ASSET UPLOAD
  // ============================================

  /**
   * Upload a file asset to a session.
   * Calls POST /sessions/{sid}/assets as multipart/form-data.
   */
  uploadAsset(sessionId: string, file: File | Blob, name?: string): Promise<AssetReference>;
}
