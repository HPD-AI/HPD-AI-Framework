import { AgentError, parseErrorResponse } from './errors.js';
import type {
  AgentSummaryDto,
  CreateAgentRequest,
  StoredAgentDto,
  UpdateAgentRequest,
} from './types/agent.js';
import type {
  ClientToolProviderQuery,
  ClientToolProviderSnapshot,
} from './types/client-tool-providers.js';
import type {
  AgentComparisonResult,
  ThreadComparisonResult,
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
  Thread,
  ThreadEvent,
  ThreadGraph,
  ThreadMessage,
  CreateThreadRequest,
  CreateSessionRequest,
  ForkThreadRequest,
  ListSessionsOptions,
  SearchSessionsRequest,
  Session,
  UpdateSessionRequest,
  UpdateThreadRequest,
} from './types/session.js';
import type { ThreadRun } from './types/thread-run.js';
import type { TransportRequestOptions } from './transports/options.js';
import { projectThreadEventsToMessages } from './thread-messages.js';

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

  async listThreads(sessionId: string): Promise<Thread[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/threads`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list threads');
  }

  async getThread(sessionId: string, threadId: string): Promise<Thread | null> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/threads/${threadId}`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get thread');
  }

  async createThread(sessionId: string, options: CreateThreadRequest = {}): Promise<Thread> {
    if (!options.agentId) throw new Error('createThread() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/threads`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to create thread');
  }

  async forkThread(sessionId: string, threadId: string, options: ForkThreadRequest): Promise<Thread> {
    if (!options.agentId) throw new Error('forkThread() requires agentId');
    const { agentId, ...body } = options;
    const response = await this.fetch(this.url(`/agents/${agentId}/sessions/${sessionId}/threads/${threadId}/fork`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    return this.readJson(response, 'Failed to fork thread');
  }

  async updateThread(sessionId: string, threadId: string, request: UpdateThreadRequest): Promise<Thread> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/threads/${threadId}`), {
      method: 'PATCH',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    return this.readJson(response, 'Failed to update thread');
  }

  async deleteThread(sessionId: string, threadId: string, options?: { recursive?: boolean }): Promise<void> {
    const url = this.url(`/sessions/${sessionId}/threads/${threadId}`, {
      recursive: options?.recursive ? true : undefined,
    });
    const response = await this.fetch(url, { method: 'DELETE' });
    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`Failed to delete thread: HTTP ${response.status}: ${text}`);
    }
  }

  async getThreadEvents(sessionId: string, threadId: string): Promise<ThreadEvent[]> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/threads/${threadId}/events`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (response.status === 404 && await this.getThread(sessionId, threadId) !== null) {
      return [];
    }

    return this.readJson(response, 'Failed to get thread events');
  }

  async getThreadMessages(sessionId: string, threadId: string): Promise<ThreadMessage[]> {
    const events = await this.getThreadEvents(sessionId, threadId);
    return projectThreadEventsToMessages(events);
  }

  async getThreadRuns(agentId: string, sessionId: string, threadId: string): Promise<ThreadRun[]> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/threads/${threadId}/runs`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    return this.readJson(response, 'Failed to get thread runs');
  }

  async listClientToolProviders(query?: ClientToolProviderQuery): Promise<ClientToolProviderSnapshot[]> {
    const response = await this.fetch(this.url('/client-tool-providers', {
      appProviderName: query?.appProviderName,
      appKind: query?.appKind,
      includeDisconnected: query?.includeDisconnected,
    }), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to list client tool providers');
  }

  async getClientToolProvider(clientRuntimeId: string): Promise<ClientToolProviderSnapshot | null> {
    const response = await this.fetch(this.url(`/client-tool-providers/${clientRuntimeId}`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    if (response.status === 404) return null;
    return this.readJson(response, 'Failed to get client tool provider');
  }

  async getActiveThreadRun(agentId: string, sessionId: string, threadId: string): Promise<ThreadRun | null> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/threads/${threadId}/runs/active`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    if (response.status === 404) return null;
    return this.readNullableJson(response, 'Failed to get active thread run');
  }

  async getThreadRun(
    agentId: string,
    sessionId: string,
    threadId: string,
    runtimeRunId: string,
  ): Promise<ThreadRun | null> {
    const response = await this.fetch(
      this.url(`/agents/${agentId}/sessions/${sessionId}/threads/${threadId}/runs/${runtimeRunId}`),
      {
        method: 'GET',
        headers: { 'Content-Type': 'application/json' },
      },
    );
    if (response.status === 404) return null;
    return this.readNullableJson(response, 'Failed to get thread run');
  }

  async getThreadGraph(sessionId: string): Promise<ThreadGraph> {
    const response = await this.fetch(this.url(`/sessions/${sessionId}/thread-graph`), {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });
    return this.readJson(response, 'Failed to get thread graph');
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

  async getScoresByThread(sessionId: string, threadId?: string): Promise<ScoreRecord[]> {
    const url = this.url('/evals/scores/by-thread', { sessionId, threadId });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get scores by thread');
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

  async getThreadComparison(sessionId: string, threadId1: string, threadId2: string, evaluatorNames: string[]): Promise<ThreadComparisonResult> {
    const url = this.url('/evals/thread-comparison', {
      sessionId,
      threadId1,
      threadId2,
      evaluatorNames: evaluatorNames.join(','),
    });
    const response = await this.fetch(url, { method: 'GET', headers: { 'Content-Type': 'application/json' } });
    return this.readJson(response, 'Failed to get thread comparison');
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

  async uploadContent(sessionId: string, threadId: string, file: File | Blob, name?: string): Promise<ContentReference> {
    const form = new FormData();
    form.append('file', file, name ?? (file instanceof File ? file.name : 'upload'));
    const response = await this.fetch(this.url(`/sessions/${sessionId}/threads/${threadId}/content`), {
      method: 'POST',
      body: form,
    });
    return this.readJson(response, 'Failed to upload content');
  }
}
