import type { AgentEvent } from '../types/events.js';
import type {
  AgentTransport,
  ClientMessage,
  ConnectOptions,
} from '../types/transport.js';
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
} from '../types/session.js';
import type {
  AgentSummaryDto,
  StoredAgentDto,
  CreateAgentRequest,
  UpdateAgentRequest,
} from '../types/agent.js';
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
} from '../types/evals.js';
import { SseParser } from '../parser.js';

/**
 * SSE (Server-Sent Events) transport implementation.
 * Uses fetch with streaming for event delivery.
 * Bidirectional messages are sent via separate HTTP POST requests.
 */
export class SseTransport implements AgentTransport {
  private baseUrl: string;
  private sessionId?: string;
  private branchId?: string;
  private abortController?: AbortController;
  private eventHandler?: (event: AgentEvent) => void;
  private errorHandler?: (error: Error) => void;
  private closeHandler?: () => void;
  private _connected = false;

  constructor(baseUrl: string) {
    // Remove trailing slash for consistent URL building
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  get connected(): boolean {
    return this._connected;
  }

  async connect(options: ConnectOptions): Promise<void> {
    if (this._connected) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    this.sessionId = options.sessionId;
    this.branchId = options.branchId || 'main';
    this.abortController = new AbortController();

    // Combine user signal with our internal abort controller
    const signal = options.signal
      ? this.combineSignals(options.signal, this.abortController.signal)
      : this.abortController.signal;

    const url = `${this.baseUrl}/sessions/${this.sessionId}/branches/${this.branchId}/stream`;

    // Build request body with all stream options
    const requestBody: Record<string, unknown> = {
      messages: options.messages,
    };

    // Include client tools configuration
    if (options.clientToolKits && options.clientToolKits.length > 0) {
      requestBody.clientToolKits = options.clientToolKits;
    }
    if (options.context && options.context.length > 0) {
      requestBody.context = options.context;
    }
    if (options.state !== undefined) {
      requestBody.state = options.state;
    }
    if (options.expandedContainers && options.expandedContainers.length > 0) {
      requestBody.expandedContainers = options.expandedContainers;
    }
    if (options.hiddenTools && options.hiddenTools.length > 0) {
      requestBody.hiddenTools = options.hiddenTools;
    }
    if (options.resetClientState) {
      requestBody.resetClientState = options.resetClientState;
    }
    if (options.runConfig !== undefined) {
      requestBody.runConfig = options.runConfig;
    }
    if (options.agentId !== undefined) {
      requestBody.agentId = options.agentId;
    }

    const response = await fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
      },
      body: JSON.stringify(requestBody),
      signal,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    if (!response.body) {
      throw new Error('No response body');
    }

    this._connected = true;
    await this.processStream(response.body);
  }

  private async processStream(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const parser = new SseParser();

    try {
      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          // Process any remaining data in the buffer
          const finalEvents = parser.flush();
          for (const event of finalEvents) {
            this.eventHandler?.(event);
          }
          break;
        }

        const events = parser.processChunk(value);
        for (const event of events) {
          this.eventHandler?.(event);
        }
      }
    } catch (error) {
      // Don't treat abort as an error
      if ((error as DOMException)?.name !== 'AbortError') {
        this.errorHandler?.(error as Error);
      }
    } finally {
      reader.releaseLock();
      this._connected = false;
      this.closeHandler?.();
    }
  }

  async send(message: ClientMessage): Promise<void> {
    if (!this.sessionId || !this.branchId) {
      throw new Error('Not connected');
    }

    // SSE is unidirectional - send via separate HTTP request
    const endpoint = this.getEndpointForMessage(message);

    const response = await fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(message),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to send message: HTTP ${response.status}: ${text}`);
    }
  }

  private getEndpointForMessage(message: ClientMessage): string {
    switch (message.type) {
      case 'permission_response':
        return `/sessions/${this.sessionId}/branches/${this.branchId}/permissions/respond`;
      case 'clarification_response':
        return `/sessions/${this.sessionId}/branches/${this.branchId}/clarifications/respond`;
      case 'continuation_response':
        return `/sessions/${this.sessionId}/branches/${this.branchId}/continuations/respond`;
      case 'client_tool_response':
        return `/sessions/${this.sessionId}/branches/${this.branchId}/client-tools/respond`;
      default:
        throw new Error(`Unknown message type: ${(message as { type: string }).type}`);
    }
  }

  onEvent(handler: (event: AgentEvent) => void): void {
    this.eventHandler = handler;
  }

  onError(handler: (error: Error) => void): void {
    this.errorHandler = handler;
  }

  onClose(handler: () => void): void {
    this.closeHandler = handler;
  }

  disconnect(): void {
    this.abortController?.abort();
    this._connected = false;
  }

  /**
   * Combine multiple AbortSignals into one.
   * Aborts when any of the input signals abort.
   */
  private combineSignals(...signals: AbortSignal[]): AbortSignal {
    const controller = new AbortController();

    for (const signal of signals) {
      if (signal.aborted) {
        controller.abort(signal.reason);
        return controller.signal;
      }
      signal.addEventListener('abort', () => controller.abort(signal.reason), { once: true });
    }

    return controller.signal;
  }

  // ============================================
  // SESSION CRUD (V3)
  // ============================================

  async listSessions(options?: ListSessionsOptions): Promise<Session[]> {
    const url = new URL(`${this.baseUrl}/sessions`);

    if (options?.limit) url.searchParams.set('limit', options.limit.toString());
    if (options?.offset) url.searchParams.set('offset', options.offset.toString());
    if (options?.sortBy) url.searchParams.set('sortBy', options.sortBy);
    if (options?.sortDirection) url.searchParams.set('sortDirection', options.sortDirection);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to list sessions: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getSession(sessionId: string): Promise<Session | null> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (response.status === 404) {
      return null;
    }

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get session: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async createSession(options?: CreateSessionRequest): Promise<Session> {
    const response = await fetch(`${this.baseUrl}/sessions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {}),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to create session: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to update session: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async deleteSession(sessionId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: 'DELETE',
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete session: HTTP ${response.status}: ${text}`);
    }
  }

  // ============================================
  // BRANCH CRUD (V3)
  // ============================================

  async listBranches(sessionId: string): Promise<Branch[]> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to list branches: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getBranch(sessionId: string, branchId: string): Promise<Branch | null> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (response.status === 404) {
      return null;
    }

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get branch: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async createBranch(sessionId: string, options?: CreateBranchRequest): Promise<Branch> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {}),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to create branch: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async forkBranch(
    sessionId: string,
    branchId: string,
    options: ForkBranchRequest
  ): Promise<Branch> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/fork`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to fork branch: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    const url = new URL(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}`);
    if (options?.recursive) url.searchParams.set('recursive', 'true');
    const response = await fetch(url.toString(), {
      method: 'DELETE',
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }

  async getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/messages`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get branch messages: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  // ============================================
  // SIBLING NAVIGATION (V3)
  // ============================================

  async getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]> {
    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/siblings`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get siblings: HTTP ${response.status}: ${text}`);
    }

    // Backend returns ordered SiblingBranchDto[] (already sorted by siblingIndex)
    return response.json();
  }

  async getNextSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.nextSiblingId) {
      return null;
    }

    return this.getBranch(sessionId, branch.nextSiblingId);
  }

  async getPreviousSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.previousSiblingId) {
      return null;
    }

    return this.getBranch(sessionId, branch.previousSiblingId);
  }

  // ============================================
  // AGENT DEFINITION CRUD
  // ============================================

  async listAgents(): Promise<AgentSummaryDto[]> {
    const response = await fetch(`${this.baseUrl}/agents`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to list agents: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getAgent(agentId: string): Promise<StoredAgentDto | null> {
    const response = await fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (response.status === 404) return null;

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get agent: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async createAgent(request: CreateAgentRequest): Promise<StoredAgentDto> {
    const response = await fetch(`${this.baseUrl}/agents`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to create agent: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto> {
    const response = await fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });

    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to update agent: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async deleteAgent(agentId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: 'DELETE',
    });

    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete agent: HTTP ${response.status}: ${text}`);
    }
  }

  // ============================================
  // EVAL QUERIES
  // ============================================

  async getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]> {
    const url = new URL(`${this.baseUrl}/evals/scores`);
    url.searchParams.set('evaluatorName', evaluatorName);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get scores: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]> {
    const url = new URL(`${this.baseUrl}/evals/scores/by-branch`);
    url.searchParams.set('sessionId', sessionId);
    if (branchId) url.searchParams.set('branchId', branchId);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get scores by branch: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord> {
    const response = await fetch(`${this.baseUrl}/evals/scores`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(record),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to write score: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]> {
    const url = new URL(`${this.baseUrl}/evals/evaluators`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get evaluator summary: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]> {
    const url = new URL(`${this.baseUrl}/evals/risk-autonomy`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get risk/autonomy distribution: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend> {
    const url = new URL(`${this.baseUrl}/evals/trend/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('from', from);
    url.searchParams.set('to', to);
    if (bucketSize) url.searchParams.set('bucketSize', bucketSize);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get trend: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult> {
    const url = new URL(`${this.baseUrl}/evals/pass-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get pass rate: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult> {
    const url = new URL(`${this.baseUrl}/evals/failure-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get failure rate: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult> {
    const url = new URL(`${this.baseUrl}/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('agentNames', agentNames.join(','));
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get agent comparison: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult> {
    const url = new URL(`${this.baseUrl}/evals/branch-comparison`);
    url.searchParams.set('sessionId', sessionId);
    url.searchParams.set('branchId1', branchId1);
    url.searchParams.set('branchId2', branchId2);
    url.searchParams.set('evaluatorNames', evaluatorNames.join(','));

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get branch comparison: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>> {
    const url = new URL(`${this.baseUrl}/evals/tool-usage`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get tool usage: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getCost(from?: string, to?: string): Promise<CostBreakdown> {
    const url = new URL(`${this.baseUrl}/evals/cost`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get cost breakdown: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]> {
    const url = new URL(`${this.baseUrl}/evals/scores/by-version`);
    url.searchParams.set('evaluatorName', evaluatorName);
    url.searchParams.set('version', version);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to get scores by version: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async uploadAsset(sessionId: string, file: File | Blob, name?: string): Promise<AssetReference> {
    const form = new FormData();
    form.append('file', file, name ?? (file instanceof File ? file.name : 'upload'));

    const response = await fetch(`${this.baseUrl}/sessions/${sessionId}/assets`, {
      method: 'POST',
      body: form,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to upload asset: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }
}
