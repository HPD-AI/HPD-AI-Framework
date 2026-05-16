import type { AgentEvent, AgentRunInputEvent } from '../types/events.js';
import { EventTypes } from '../types/events.js';
import type {
  AgentTransport,
  RunTransportOptions,
  RuntimeScope,
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
import { AgentError, parseErrorResponse } from '../errors.js';
import type { TransportRequestOptions } from './options.js';

/**
 * SSE (Server-Sent Events) transport implementation.
 * Uses fetch with streaming for event delivery.
 * Bidirectional messages are sent via separate HTTP POST requests.
 */
export class SseTransport implements AgentTransport {
  private baseUrl: string;
  private requestOptions: TransportRequestOptions;
  private agentId?: string;
  private sessionId?: string;
  private branchId?: string;
  private abortController?: AbortController;
  private eventHandler?: (event: AgentEvent) => void;
  private errorHandler?: (error: Error) => void;
  private closeHandler?: () => void;
  private _connected = false;

  constructor(baseUrl: string, requestOptions: TransportRequestOptions = {}) {
    // Remove trailing slash for consistent URL building
    this.baseUrl = baseUrl.replace(/\/$/, '');
    this.requestOptions = requestOptions;
  }

  private fetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
    const headers = {
      ...(this.requestOptions.headers ?? {}),
      ...((init.headers as Record<string, string> | undefined) ?? {}),
    };

    return globalThis.fetch(input, {
      ...init,
      credentials: this.requestOptions.credentials,
      headers,
    });
  }

  private url(path: string): URL {
    const base = /^[a-z][a-z\d+.-]*:\/\//i.test(this.baseUrl)
      ? this.baseUrl
      : `${globalThis.location?.origin ?? 'http://localhost'}${this.baseUrl.startsWith('/') ? '' : '/'}${this.baseUrl}`;

    return new URL(`${base}${path}`);
  }

  get connected(): boolean {
    return this._connected;
  }

  async connect(scope?: RuntimeScope): Promise<void> {
    if (this._connected) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    this.sessionId = scope?.sessionId;
    this.branchId = scope?.branchId || 'main';
    this.agentId = scope?.agentId;
  }

  async run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void> {
    const sessionId = 'sessionId' in input ? input.sessionId : undefined;
    const branchId = 'branchId' in input ? input.branchId : undefined;
    const agentId = 'agentId' in input ? input.agentId : undefined;

    this.sessionId = sessionId ?? this.sessionId;
    this.branchId = branchId ?? this.branchId ?? 'main';
    this.agentId = agentId ?? this.agentId;

    if (this.isMiddlewareResponse(input)) {
      await this.postMiddlewareResponse(input);
      return;
    }

    if (this._connected) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    if (!this.sessionId) {
      throw new Error('Input event must include sessionId for SSE run()');
    }

    if (!this.agentId) {
      throw new Error('Input event must include agentId for SSE run()');
    }

    this.abortController = new AbortController();

    // Combine user signal with our internal abort controller
    const signal = options?.signal
      ? this.combineSignals(options.signal, this.abortController.signal)
      : this.abortController.signal;

    const isTextInput = input.type === EventTypes.USER_TEXT_INPUT;
    const url = isTextInput
      ? `${this.baseUrl}/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/stream`
      : `${this.baseUrl}/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/events/stream`;

    const response = await this.fetch(url, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
      },
      body: JSON.stringify(isTextInput
        ? { text: input.text, runConfig: input.runConfig }
        : input),
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

  private isMiddlewareResponse(input: AgentRunInputEvent): boolean {
    return input.type === EventTypes.PERMISSION_RESPONSE ||
      input.type === EventTypes.CONTINUATION_RESPONSE ||
      input.type === EventTypes.CLARIFICATION_RESPONSE ||
      input.type === EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
  }

  private async postMiddlewareResponse(message: AgentRunInputEvent): Promise<void> {
    if (!this.agentId || !this.sessionId || !this.branchId) {
      throw new Error('Not connected');
    }

    const endpoint = this.getEndpointForMessage(message);

    const response = await this.fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(message),
    });

    if (!response.ok) {
      if (response.status === 409) {
        throw new AgentError(
          'Response was not accepted because the request is no longer pending',
          'STALE_RESPONSE',
          { statusCode: response.status },
        );
      }

      const body = await response.json().catch(() => null);
      throw parseErrorResponse(response, body);
    }
  }

  private getEndpointForMessage(message: AgentRunInputEvent): string {
    switch (message.type) {
      case EventTypes.PERMISSION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/permissions/respond`;
      case EventTypes.CONTINUATION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/continuation/respond`;
      case EventTypes.CLARIFICATION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/clarifications/respond`;
      case EventTypes.CLIENT_TOOL_INVOKE_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/client-tools/respond`;
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
    const url = this.url(`/sessions`);

    if (options?.limit) url.searchParams.set('limit', options.limit.toString());
    if (options?.offset) url.searchParams.set('offset', options.offset.toString());
    if (options?.sortBy) url.searchParams.set('sortBy', options.sortBy);
    if (options?.sortDirection) url.searchParams.set('sortDirection', options.sortDirection);

    const response = await this.fetch(url.toString(), {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}`, {
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
    const agentId = options?.agentId ?? this.agentId;
    if (!agentId) {
      throw new Error('createBranch() requires agentId');
    }

    const { agentId: _agentId, ...body } = options ?? {};
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}/sessions/${sessionId}/branches`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
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
    const agentId = options.agentId ?? this.agentId;
    if (!agentId) {
      throw new Error('forkBranch() requires agentId');
    }

    const { agentId: _agentId, ...body } = options;
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/fork`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to fork branch: HTTP ${response.status}: ${text}`);
    }

    return response.json();
  }

  async deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    const url = this.url(`/sessions/${sessionId}/branches/${branchId}`);
    if (options?.recursive) url.searchParams.set('recursive', 'true');
    const response = await this.fetch(url.toString(), {
      method: 'DELETE',
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }

  async getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]> {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/messages`, {
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
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/siblings`, {
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
    const response = await this.fetch(`${this.baseUrl}/agents`, {
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
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
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
    const response = await this.fetch(`${this.baseUrl}/agents`, {
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
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
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
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
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
    const url = this.url(`/evals/scores`);
    url.searchParams.set('evaluatorName', evaluatorName);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/scores/by-branch`);
    url.searchParams.set('sessionId', sessionId);
    if (branchId) url.searchParams.set('branchId', branchId);

    const response = await this.fetch(url.toString(), {
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
    const response = await this.fetch(`${this.baseUrl}/evals/scores`, {
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
    const url = this.url(`/evals/evaluators`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/risk-autonomy`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/trend/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('from', from);
    url.searchParams.set('to', to);
    if (bucketSize) url.searchParams.set('bucketSize', bucketSize);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/pass-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/failure-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('agentNames', agentNames.join(','));
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/branch-comparison`);
    url.searchParams.set('sessionId', sessionId);
    url.searchParams.set('branchId1', branchId1);
    url.searchParams.set('branchId2', branchId2);
    url.searchParams.set('evaluatorNames', evaluatorNames.join(','));

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/tool-usage`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/cost`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);

    const response = await this.fetch(url.toString(), {
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
    const url = this.url(`/evals/scores/by-version`);
    url.searchParams.set('evaluatorName', evaluatorName);
    url.searchParams.set('version', version);

    const response = await this.fetch(url.toString(), {
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

    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/assets`, {
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
