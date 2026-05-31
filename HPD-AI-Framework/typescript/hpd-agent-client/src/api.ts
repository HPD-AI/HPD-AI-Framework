import { AgentError, parseErrorResponse } from './errors.js';
import type {
  AgentSummaryDto,
  CreateAgentRequest,
  StoredAgentDto,
  UpdateAgentRequest,
} from './types/agent.js';
import type {
  AgentComparisonResult,
  BranchComparisonResult,
  CostBreakdown,
  EvaluatorSummary,
  FailureRateResult,
  PassRateResult,
  RiskAutonomyDataPoint,
  ScoreRecord,
  ScoreTrend,
  ToolUsageSummary,
} from './types/evals.js';
import type {
  ContentReference,
  Branch,
  BranchEvent,
  BranchMessage,
  CreateBranchRequest,
  CreateSessionRequest,
  ForkBranchRequest,
  AIContent,
  ListSessionsOptions,
  SearchSessionsRequest,
  Session,
  SiblingBranch,
  UpdateSessionRequest,
  UpdateBranchRequest,
} from './types/session.js';
import type { BranchRun } from './types/branch-run.js';
import type { TransportRequestOptions } from './transports/options.js';

export class AgentHttpApi {
  private readonly baseUrl: string;

  constructor(baseUrl: string, private readonly requestOptions: TransportRequestOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
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

  private url(path: string, query?: Record<string, string | number | boolean | undefined>): string {
    const normalizedPath = path.startsWith('/') ? path : `/${path}`;
    const base = this.baseUrl;
    const requestUrl = /^[a-z][a-z\d+.-]*:\/\//i.test(base)
      ? `${base}${normalizedPath}`
      : `${base.startsWith('/') ? base : `/${base}`}${normalizedPath}`;

    if (!query) return requestUrl;

    const search = new URLSearchParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined) search.set(key, String(value));
    }

    const queryString = search.toString();
    if (!queryString) return requestUrl;

    return `${requestUrl}${requestUrl.includes('?') ? '&' : '?'}${queryString}`;
  }

  private async readJson<T>(response: Response, failureMessage: string): Promise<T> {
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      if (body) throw parseErrorResponse(response, body);
      const text = await response.text().catch(() => 'Unknown error');
      throw new AgentError(`${failureMessage}: HTTP ${response.status}: ${text}`, 'HTTP_ERROR', {
        statusCode: response.status,
      });
    }

    return response.json();
  }

  private async readNullableJson<T>(response: Response, failureMessage: string): Promise<T | null> {
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      if (body) throw parseErrorResponse(response, body);
      const text = await response.text().catch(() => 'Unknown error');
      throw new AgentError(`${failureMessage}: HTTP ${response.status}: ${text}`, 'HTTP_ERROR', {
        statusCode: response.status,
      });
    }

    const text = await response.text().catch(() => '');
    if (text.trim()) return JSON.parse(text) as T | null;

    return response.json?.().catch(() => null) as Promise<T | null>;
  }

  private async send(path: string, init: RequestInit, failureMessage: string): Promise<void> {
    const response = await this.fetch(this.url(path), init);
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      if (body) throw parseErrorResponse(response, body);
      const text = await response.text().catch(() => 'Unknown error');
      throw new AgentError(`${failureMessage}: HTTP ${response.status}: ${text}`, 'HTTP_ERROR', {
        statusCode: response.status,
      });
    }
  }

  async listSessions(options?: ListSessionsOptions): Promise<Session[]> {
    const url = this.url('/sessions', {
      limit: options?.limit,
      offset: options?.offset,
      sortBy: options?.sortBy,
      sortDirection: options?.sortDirection,
    });

    const response = await this.fetch(url, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list sessions');
  }

  async searchSessions(request?: SearchSessionsRequest): Promise<Session[]> {
    const response = await this.fetch(this.url('/sessions/search'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request || {}),
    });
    return this.readJson(response, 'Failed to search sessions');
  }

  async getSession(sessionId: string): Promise<Session | null> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get session');
  }

  async createSession(options?: CreateSessionRequest): Promise<Session> {
    const response = await this.fetch(this.url('/sessions'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {}),
    });
    return this.readJson(response, 'Failed to create session');
  }

  async updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}`), {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return this.readJson(response, 'Failed to update session');
  }

  async deleteSession(sessionId: string): Promise<void> {
    await this.send(`/sessions/${sessionId}`, { method: 'DELETE' }, 'Failed to delete session');
  }

  async listBranches(sessionId: string): Promise<Branch[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list branches');
  }

  async getBranch(sessionId: string, branchId: string): Promise<Branch | null> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get branch');
  }

  async createBranch(sessionId: string, options: CreateBranchRequest = {}): Promise<Branch> {
    if (!options.agentId) throw new Error('createBranch() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/branches`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to create branch');
  }

  async forkBranch(sessionId: string, branchId: string, options: ForkBranchRequest): Promise<Branch> {
    if (!options.agentId) throw new Error('forkBranch() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/fork`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to fork branch');
  }

  async updateBranch(sessionId: string, branchId: string, request: UpdateBranchRequest): Promise<Branch> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}`), {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return this.readJson(response, 'Failed to update branch');
  }

  async deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    const url = this.url(`/sessions/${sessionId}/branches/${branchId}`, {
      recursive: options?.recursive ? true : undefined,
    });
    const response = await this.fetch(url, { method: 'DELETE' });
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }

  async getBranchEvents(sessionId: string, branchId: string): Promise<BranchEvent[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}/events`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (response.status === 404 && await this.getBranch(sessionId, branchId) !== null) {
      return [];
    }

    return this.readJson(response, 'Failed to get branch events');
  }

  async getBranchMessages(sessionId: string, branchId: string): Promise<BranchMessage[]> {
    const events = await this.getBranchEvents(sessionId, branchId);
    const byId = new Map<string, BranchMessage>();

    for (const event of events) {
      if (event.type === 'MESSAGE_STARTED') {
        const started = event as BranchEvent & {
          messageId?: string;
          role?: string;
          authorName?: string;
          createdAt?: string;
          timestamp?: string;
        };
        if (!started.messageId) continue;

        byId.set(started.messageId, {
          id: started.messageId,
          role: started.role ?? 'assistant',
          contents: [],
          timestamp: started.createdAt ?? started.timestamp ?? new Date().toISOString(),
          authorName: started.authorName,
        });
      } else if (event.type === 'CONTENT_ADDED') {
        const added = event as BranchEvent & {
          messageId?: string;
          content?: AIContent;
        };
        if (!added.messageId || !added.content) continue;

        const message = byId.get(added.messageId);
        if (message) message.contents.push(added.content);
      }
    }

    return [...byId.values()];
  }

  async getBranchRuns(agentId: string, sessionId: string, branchId: string): Promise<BranchRun[]> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/runs`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    return this.readJson(response, 'Failed to get branch runs');
  }

  async getActiveBranchRun(agentId: string, sessionId: string, branchId: string): Promise<BranchRun | null> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/runs/active`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    if (response.status === 404) return null;
    return this.readNullableJson(response, 'Failed to get active branch run');
  }

  async getBranchRun(
    agentId: string,
    sessionId: string,
    branchId: string,
    runtimeRunId: string,
  ): Promise<BranchRun | null> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/runs/${runtimeRunId}`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    if (response.status === 404) return null;
    return this.readNullableJson(response, 'Failed to get branch run');
  }

  async getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}/siblings`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to get siblings');
  }

  async getNextSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    const branch = await this.getBranch(sessionId, branchId);
    return branch?.nextSiblingId ? this.getBranch(sessionId, branch.nextSiblingId) : null;
  }

  async getPreviousSibling(sessionId: string, branchId: string): Promise<Branch | null> {
    const branch = await this.getBranch(sessionId, branchId);
    return branch?.previousSiblingId ? this.getBranch(sessionId, branch.previousSiblingId) : null;
  }

  async listAgents(): Promise<AgentSummaryDto[]> {
    const response = await this.fetch(this.url('/agents'), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list agents');
  }

  async getAgent(agentId: string): Promise<StoredAgentDto | null> {
    const response = await this.fetch(this.url(`/agents/${agentId}`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get agent');
  }

  async createAgent(request: CreateAgentRequest): Promise<StoredAgentDto> {
    const response = await this.fetch(this.url('/agents'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return this.readJson(response, 'Failed to create agent');
  }

  async updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto> {
    const response = await this.fetch(this.url(`/agents/${agentId}`), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (response.status === 404) throw new Error(`Agent not found: ${agentId}`);
    return this.readJson(response, 'Failed to update agent');
  }

  async deleteAgent(agentId: string): Promise<void> {
    const response = await this.fetch(this.url(`/agents/${agentId}`), { method: 'DELETE' });
    if (response.status === 404) throw new Error(`Agent not found: ${agentId}`);
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete agent: HTTP ${response.status}: ${text}`);
    }
  }

  async getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores', { evaluatorName, from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores');
  }

  async getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores/by-branch', { sessionId, branchId });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores by branch');
  }

  async writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord> {
    const response = await this.fetch(this.url('/evals/scores'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(record),
    });
    return this.readJson(response, 'Failed to write score');
  }

  async getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]> {
    const url = this.url('/evals/evaluators', { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get evaluator summary');
  }

  async getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]> {
    const url = this.url('/evals/risk-autonomy', { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get risk/autonomy distribution');
  }

  async getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend> {
    const url = this.url(`/evals/trend/${encodeURIComponent(evaluatorName)}`, { from, to, bucketSize });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get trend');
  }

  async getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult> {
    const url = this.url(`/evals/pass-rate/${encodeURIComponent(evaluatorName)}`, { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get pass rate');
  }

  async getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult> {
    const url = this.url(`/evals/failure-rate/${encodeURIComponent(evaluatorName)}`, { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get failure rate');
  }

  async getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult> {
    const url = this.url(`/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`, {
      agentNames: agentNames.join(','),
      from,
      to,
    });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get agent comparison');
  }

  async getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult> {
    const url = this.url('/evals/branch-comparison', {
      sessionId,
      branchId1,
      branchId2,
      evaluatorNames: evaluatorNames.join(','),
    });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get branch comparison');
  }

  async getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>> {
    const url = this.url('/evals/tool-usage', { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get tool usage');
  }

  async getCost(from?: string, to?: string): Promise<CostBreakdown> {
    const url = this.url('/evals/cost', { from, to });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get cost breakdown');
  }

  async getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores/by-version', { evaluatorName, version });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores by version');
  }

  async uploadContent(sessionId: string, file: File | Blob, name?: string): Promise<ContentReference> {
    const form = new FormData();
    form.append('file', file, name ?? (file instanceof File ? file.name : 'upload'));
    const response = await this.fetch(this.url(`/sessions/${sessionId}/content`), {
      method: 'POST',
      body: form,
    });
    return this.readJson(response, 'Failed to upload content');
  }
}
