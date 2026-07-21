import type { AgentStudioApiConfig, JsonBody } from '../types';

export function createAgentApi(config: AgentStudioApiConfig) {
  const api = createHpdApiClient(config);

  return {
    getStatus: () => api.get('/agents'),
    listAgents: () => api.get('/agents'),
    listSessions: () => api.get('/sessions'),
    listThreads: (sessionId: string) => api.get(`/sessions/${encodeURIComponent(sessionId)}/threads`),
    getThreadEvents: (sessionId: string, threadId: string) =>
      api.get(`/sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}/events`),
    listContent: (sessionId: string, threadId: string) =>
      api.get(`/sessions/${encodeURIComponent(sessionId)}/threads/${encodeURIComponent(threadId)}/content`),
    getThreadState: (agentId: string, sessionId: string, threadId: string) =>
      api.get(
        `/agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}` +
          `/threads/${encodeURIComponent(threadId)}/state`
      ),
    submitText: (agentId: string, sessionId: string, threadId: string, text: string) =>
      api.post(
        `/agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}` +
          `/threads/${encodeURIComponent(threadId)}/inputs`,
        { text }
      ),
    interrupt: async (
      agentId: string,
      sessionId: string,
      threadId: string,
      reason = 'Interrupted from HPD AI Platform.'
    ) => {
      const threadPath = `/agents/${encodeURIComponent(agentId)}` +
        `/sessions/${encodeURIComponent(sessionId)}` +
        `/threads/${encodeURIComponent(threadId)}`;
      const state = await api.get(`${threadPath}/state`) as {
        activeExecution?: { threadExecutionId?: string } | null;
      };
      const expectedThreadExecutionId = state.activeExecution?.threadExecutionId;
      if (!expectedThreadExecutionId) {
        return { status: 'no_active_execution', activeExecution: null };
      }

      return api.post(`${threadPath}/interrupt`, {
        reason,
        expectedThreadExecutionId
      });
    },
    listMultiAgentWorkflows: () => api.get('/multi-agent/workflows'),
    getMultiAgentWorkflow: (workflowId: string) =>
      api.get(`/multi-agent/workflows/${encodeURIComponent(workflowId)}`),
    startMultiAgentRun: (workflowId: string, inputText?: string) =>
      api.post(`/multi-agent/workflows/${encodeURIComponent(workflowId)}/runs`, {
        input: inputText ? { text: inputText } : null,
        mode: 'background',
        startImmediately: true,
        triggeredBy: 'hpd-ai-studio'
      }),
    listMultiAgentRuns: (workflowId: string) =>
      api.get(`/multi-agent/workflows/${encodeURIComponent(workflowId)}/runs`),
    getMultiAgentRun: (workflowId: string, runId: string) =>
      api.get(
        `/multi-agent/workflows/${encodeURIComponent(workflowId)}` +
          `/runs/${encodeURIComponent(runId)}`
      ),
    listMultiAgentSuspendedNodes: (workflowId: string, runId: string) =>
      api.get(
        `/multi-agent/workflows/${encodeURIComponent(workflowId)}` +
          `/runs/${encodeURIComponent(runId)}/suspended-nodes`
      ),
    getMultiAgentEventsUrl: (workflowId: string, runId: string) =>
      `${normalizeBasePath(config.apiBasePath)}/multi-agent/workflows/${encodeURIComponent(workflowId)}` +
      `/runs/${encodeURIComponent(runId)}/events`,
    respondToMultiAgentApproval: (
      workflowId: string,
      runId: string,
      approvalId: string,
      resumeValue: unknown = null
    ) =>
      api.post(
        `/multi-agent/workflows/${encodeURIComponent(workflowId)}` +
          `/runs/${encodeURIComponent(runId)}/approvals/${encodeURIComponent(approvalId)}`,
        { resumeValue }
      )
  };
}

function normalizeBasePath(path: string) {
  if (!path || path === '/') return '';
  return `/${path.replace(/^\/+|\/+$/g, '')}`;
}

interface RequestOptions extends Omit<RequestInit, 'body'> {
  body?: JsonBody;
}

function createHpdApiClient(config: AgentStudioApiConfig) {
  const basePath = normalizeBasePath(config.apiBasePath);

  async function request(path: string, options: RequestOptions = {}) {
    const hasBody = options.body !== undefined;
    const { body, ...requestOptions } = options;

    const response = await fetch(`${basePath}${path}`, {
      headers: {
        Accept: 'application/json',
        ...(hasBody ? { 'Content-Type': 'application/json' } : null),
        ...options.headers
      },
      ...requestOptions,
      ...(hasBody ? { body: JSON.stringify(body ?? {}) } : null)
    });

    if (!response.ok) {
      const detail = await readError(response);
      throw new Error(detail || `${response.status} ${response.statusText}`);
    }

    if (response.status === 204) return null;

    const contentType = response.headers.get('content-type') ?? '';
    if (!contentType.includes('application/json')) {
      return response.text();
    }

    return response.json();
  }

  return {
    get: (path: string) => request(path),
    post: (path: string, body?: JsonBody) => request(path, { method: 'POST', body: body ?? {} }),
    patch: (path: string, body?: JsonBody) => request(path, { method: 'PATCH', body: body ?? {} }),
    put: (path: string, body?: JsonBody) => request(path, { method: 'PUT', body: body ?? {} }),
    delete: (path: string) => request(path, { method: 'DELETE' })
  };
}

async function readError(response: Response) {
  const contentType = response.headers.get('content-type') ?? '';

  if (contentType.includes('application/json')) {
    const body = await response.json().catch(() => null);
    return body?.title ?? body?.detail ?? flattenErrors(body?.errors) ?? null;
  }

  return response.text().catch(() => null);
}

function flattenErrors(errors: unknown) {
  if (!errors) return null;

  if (typeof errors !== 'object') return null;

  return Object.entries(errors)
    .flatMap(([key, values]) => `${key}: ${Array.isArray(values) ? values.join(', ') : values}`)
    .join('; ');
}
