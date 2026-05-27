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
  AssetReference,
  Branch,
  BranchEvent,
  CreateBranchRequest,
  CreateSessionRequest,
  ForkBranchRequest,
  ListSessionsOptions,
  SearchSessionsRequest,
  Session,
  SiblingBranch,
  UpdateSessionRequest,
} from './types/session.js';
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

  private url(path: string): URL {
    const base = /^[a-z][a-z\d+.-]*:\/\//i.test(this.baseUrl)
      ? this.baseUrl
      : `${globalThis.location?.origin ?? 'http://localhost'}${this.baseUrl.startsWith('/') ? '' : '/'}${this.baseUrl}`;

    return new URL(`${base}${path}`);
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

  private async send(path: string, init: RequestInit, failureMessage: string): Promise<void> {
    const response = await this.fetch(this.url(path).toString(), init);
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
    const url = this.url('/sessions');
    if (options?.limit) url.searchParams.set('limit', options.limit.toString());
    if (options?.offset) url.searchParams.set('offset', options.offset.toString());
    if (options?.sortBy) url.searchParams.set('sortBy', options.sortBy);
    if (options?.sortDirection) url.searchParams.set('sortDirection', options.sortDirection);

    const response = await this.fetch(url.toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list sessions');
  }

  async searchSessions(request?: SearchSessionsRequest): Promise<Session[]> {
    const response = await this.fetch(this.url('/sessions/search').toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request || {}),
    });
    return this.readJson(response, 'Failed to search sessions');
  }

  async getSession(sessionId: string): Promise<Session | null> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}`).toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get session');
  }

  async createSession(options?: CreateSessionRequest): Promise<Session> {
    const response = await this.fetch(this.url('/sessions').toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(options || {}),
    });
    return this.readJson(response, 'Failed to create session');
  }

  async updateSession(sessionId: string, request: UpdateSessionRequest): Promise<Session> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}`).toString(), {
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
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches`).toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list branches');
  }

  async getBranch(sessionId: string, branchId: string): Promise<Branch | null> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}`).toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get branch');
  }

  async createBranch(sessionId: string, options: CreateBranchRequest = {}): Promise<Branch> {
    if (!options.agentId) throw new Error('createBranch() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/branches`).toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to create branch');
  }

  async forkBranch(sessionId: string, branchId: string, options: ForkBranchRequest): Promise<Branch> {
    if (!options.agentId) throw new Error('forkBranch() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/fork`).toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to fork branch');
  }

  async deleteBranch(sessionId: string, branchId: string, options?: { recursive?: boolean }): Promise<void> {
    const url = this.url(`/sessions/${sessionId}/branches/${branchId}`);
    if (options?.recursive) url.searchParams.set('recursive', 'true');
    const response = await this.fetch(url.toString(), { method: 'DELETE' });
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }

  async getBranchEvents(sessionId: string, branchId: string): Promise<BranchEvent[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}/events`).toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to get branch events');
  }

  async getBranchSiblings(sessionId: string, branchId: string): Promise<SiblingBranch[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/branches/${branchId}/siblings`).toString(), {
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
    const response = await this.fetch(this.url('/agents').toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list agents');
  }

  async getAgent(agentId: string): Promise<StoredAgentDto | null> {
    const response = await this.fetch(this.url(`/agents/${agentId}`).toString(), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get agent');
  }

  async createAgent(request: CreateAgentRequest): Promise<StoredAgentDto> {
    const response = await this.fetch(this.url('/agents').toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return this.readJson(response, 'Failed to create agent');
  }

  async updateAgent(agentId: string, request: UpdateAgentRequest): Promise<StoredAgentDto> {
    const response = await this.fetch(this.url(`/agents/${agentId}`).toString(), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (response.status === 404) throw new Error(`Agent not found: ${agentId}`);
    return this.readJson(response, 'Failed to update agent');
  }

  async deleteAgent(agentId: string): Promise<void> {
    const response = await this.fetch(this.url(`/agents/${agentId}`).toString(), { method: 'DELETE' });
    if (response.status === 404) throw new Error(`Agent not found: ${agentId}`);
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete agent: HTTP ${response.status}: ${text}`);
    }
  }

  async getScores(evaluatorName: string, from?: string, to?: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores');
    url.searchParams.set('evaluatorName', evaluatorName);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores');
  }

  async getScoresByBranch(sessionId: string, branchId?: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores/by-branch');
    url.searchParams.set('sessionId', sessionId);
    if (branchId) url.searchParams.set('branchId', branchId);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores by branch');
  }

  async writeScore(record: Omit<ScoreRecord, 'id'>): Promise<ScoreRecord> {
    const response = await this.fetch(this.url('/evals/scores').toString(), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(record),
    });
    return this.readJson(response, 'Failed to write score');
  }

  async getEvaluatorSummary(from?: string, to?: string): Promise<EvaluatorSummary[]> {
    const url = this.url('/evals/evaluators');
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get evaluator summary');
  }

  async getRiskAutonomyDistribution(from?: string, to?: string): Promise<RiskAutonomyDataPoint[]> {
    const url = this.url('/evals/risk-autonomy');
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get risk/autonomy distribution');
  }

  async getTrend(evaluatorName: string, from: string, to: string, bucketSize?: string): Promise<ScoreTrend> {
    const url = this.url(`/evals/trend/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('from', from);
    url.searchParams.set('to', to);
    if (bucketSize) url.searchParams.set('bucketSize', bucketSize);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get trend');
  }

  async getPassRate(evaluatorName: string, from?: string, to?: string): Promise<PassRateResult> {
    const url = this.url(`/evals/pass-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get pass rate');
  }

  async getFailureRate(evaluatorName: string, from?: string, to?: string): Promise<FailureRateResult> {
    const url = this.url(`/evals/failure-rate/${encodeURIComponent(evaluatorName)}`);
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get failure rate');
  }

  async getAgentComparison(evaluatorName: string, agentNames: string[], from?: string, to?: string): Promise<AgentComparisonResult> {
    const url = this.url(`/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set('agentNames', agentNames.join(','));
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get agent comparison');
  }

  async getBranchComparison(sessionId: string, branchId1: string, branchId2: string, evaluatorNames: string[]): Promise<BranchComparisonResult> {
    const url = this.url('/evals/branch-comparison');
    url.searchParams.set('sessionId', sessionId);
    url.searchParams.set('branchId1', branchId1);
    url.searchParams.set('branchId2', branchId2);
    url.searchParams.set('evaluatorNames', evaluatorNames.join(','));
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get branch comparison');
  }

  async getToolUsage(from?: string, to?: string): Promise<Record<string, ToolUsageSummary>> {
    const url = this.url('/evals/tool-usage');
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get tool usage');
  }

  async getCost(from?: string, to?: string): Promise<CostBreakdown> {
    const url = this.url('/evals/cost');
    if (from) url.searchParams.set('from', from);
    if (to) url.searchParams.set('to', to);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get cost breakdown');
  }

  async getScoresByVersion(evaluatorName: string, version: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores/by-version');
    url.searchParams.set('evaluatorName', evaluatorName);
    url.searchParams.set('version', version);
    const response = await this.fetch(url.toString(), { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores by version');
  }

  async uploadAsset(sessionId: string, file: File | Blob, name?: string): Promise<AssetReference> {
    const form = new FormData();
    form.append('file', file, name ?? (file instanceof File ? file.name : 'upload'));
    const response = await this.fetch(this.url(`/sessions/${sessionId}/assets`).toString(), {
      method: 'POST',
      body: form,
    });
    return this.readJson(response, 'Failed to upload asset');
  }
}
