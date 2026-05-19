// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/types/events.js
var EventTypes = {
  USER_TEXT_INPUT: "USER_TEXT_INPUT",
  USER_MESSAGES_INPUT: "USER_MESSAGES_INPUT",
  MESSAGE_TURN_STARTED: "MESSAGE_TURN_STARTED",
  MESSAGE_TURN_FINISHED: "MESSAGE_TURN_FINISHED",
  MESSAGE_TURN_ERROR: "MESSAGE_TURN_ERROR",
  AGENT_TURN_STARTED: "AGENT_TURN_STARTED",
  AGENT_TURN_FINISHED: "AGENT_TURN_FINISHED",
  STATE_SNAPSHOT: "STATE_SNAPSHOT",
  TEXT_MESSAGE_START: "TEXT_MESSAGE_START",
  TEXT_DELTA: "TEXT_DELTA",
  TEXT_MESSAGE_END: "TEXT_MESSAGE_END",
  REASONING_MESSAGE_START: "REASONING_MESSAGE_START",
  REASONING_DELTA: "REASONING_DELTA",
  REASONING_MESSAGE_END: "REASONING_MESSAGE_END",
  TOOL_CALL_START: "TOOL_CALL_START",
  TOOL_CALL_ARGS: "TOOL_CALL_ARGS",
  TOOL_CALL_END: "TOOL_CALL_END",
  TOOL_CALL_RESULT: "TOOL_CALL_RESULT",
  PERMISSION_REQUEST: "PERMISSION_REQUEST",
  PERMISSION_RESPONSE: "PERMISSION_RESPONSE",
  PERMISSION_APPROVED: "PERMISSION_APPROVED",
  PERMISSION_DENIED: "PERMISSION_DENIED",
  CONTINUATION_REQUEST: "CONTINUATION_REQUEST",
  CONTINUATION_RESPONSE: "CONTINUATION_RESPONSE",
  CLARIFICATION_REQUEST: "CLARIFICATION_REQUEST",
  CLARIFICATION_RESPONSE: "CLARIFICATION_RESPONSE",
  MIDDLEWARE_PROGRESS: "MIDDLEWARE_PROGRESS",
  MIDDLEWARE_ERROR: "MIDDLEWARE_ERROR",
  CLIENT_TOOL_INVOKE_REQUEST: "CLIENT_TOOL_INVOKE_REQUEST",
  CLIENT_TOOL_INVOKE_RESPONSE: "CLIENT_TOOL_INVOKE_RESPONSE",
  CLIENT_TOOL_GROUPS_REGISTERED: "CLIENT_TOOL_GROUPS_REGISTERED",
  COLLAPSED_TOOLS_VISIBLE: "COLLAPSED_TOOLS_VISIBLE",
  CONTAINER_EXPANDED: "CONTAINER_EXPANDED",
  MIDDLEWARE_PIPELINE_START: "MIDDLEWARE_PIPELINE_START",
  MIDDLEWARE_PIPELINE_END: "MIDDLEWARE_PIPELINE_END",
  PERMISSION_CHECK: "PERMISSION_CHECK",
  ITERATION_START: "ITERATION_START",
  CIRCUIT_BREAKER_TRIGGERED: "CIRCUIT_BREAKER_TRIGGERED",
  HISTORY_REDUCTION_CACHE: "HISTORY_REDUCTION_CACHE",
  CHECKPOINT: "CHECKPOINT",
  INTERNAL_PARALLEL_TOOL_EXECUTION: "INTERNAL_PARALLEL_TOOL_EXECUTION",
  INTERNAL_RETRY: "INTERNAL_RETRY",
  FUNCTION_RETRY: "FUNCTION_RETRY",
  DELTA_SENDING_ACTIVATED: "DELTA_SENDING_ACTIVATED",
  PLAN_MODE_ACTIVATED: "PLAN_MODE_ACTIVATED",
  NESTED_AGENT_INVOKED: "NESTED_AGENT_INVOKED",
  DOCUMENT_PROCESSED: "DOCUMENT_PROCESSED",
  INTERNAL_MESSAGE_PREPARED: "INTERNAL_MESSAGE_PREPARED",
  BIDIRECTIONAL_EVENT_PROCESSED: "BIDIRECTIONAL_EVENT_PROCESSED",
  AGENT_DECISION: "AGENT_DECISION",
  AGENT_COMPLETION: "AGENT_COMPLETION",
  ITERATION_CONTEXT_SNAPSHOT: "ITERATION_CONTEXT_SNAPSHOT",
  MIDDLEWARE_STATE_SNAPSHOT: "MIDDLEWARE_STATE_SNAPSHOT",
  MIDDLEWARE_STATE_CHANGED: "MIDDLEWARE_STATE_CHANGED",
  SCHEMA_CHANGED: "SCHEMA_CHANGED",
  COLLAPSING_STATE: "COLLAPSING_STATE",
  SYNTHESIS_STARTED: "SYNTHESIS_STARTED",
  AUDIO_CHUNK: "AUDIO_CHUNK",
  SYNTHESIS_COMPLETED: "SYNTHESIS_COMPLETED",
  TRANSCRIPTION_DELTA: "TRANSCRIPTION_DELTA",
  TRANSCRIPTION_COMPLETED: "TRANSCRIPTION_COMPLETED",
  INTERRUPTION_REQUEST: "INTERRUPTION_REQUEST",
  USER_INTERRUPTED: "USER_INTERRUPTED",
  SPEECH_PAUSED: "SPEECH_PAUSED",
  SPEECH_RESUMED: "SPEECH_RESUMED",
  PREEMPTIVE_GENERATION_STARTED: "PREEMPTIVE_GENERATION_STARTED",
  PREEMPTIVE_GENERATION_DISCARDED: "PREEMPTIVE_GENERATION_DISCARDED",
  VAD_START_OF_SPEECH: "VAD_START_OF_SPEECH",
  VAD_END_OF_SPEECH: "VAD_END_OF_SPEECH",
  AUDIO_PIPELINE_METRICS: "AUDIO_PIPELINE_METRICS",
  TURN_DETECTED: "TURN_DETECTED",
  FILLER_AUDIO_PLAYED: "FILLER_AUDIO_PLAYED"
};

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/parser.js
class SseParser {
  decoder = new TextDecoder("utf-8", { fatal: false });
  buffer = "";
  processChunk(chunk) {
    const text = this.decoder.decode(chunk, { stream: true });
    this.buffer += text;
    const events = [];
    const parts = this.buffer.split(`

`);
    this.buffer = parts.pop() || "";
    for (const part of parts) {
      const event = this.parseEvent(part);
      if (event) {
        events.push(event);
      }
    }
    return events;
  }
  flush() {
    if (!this.buffer.trim())
      return [];
    this.buffer += this.decoder.decode();
    const event = this.parseEvent(this.buffer);
    this.buffer = "";
    return event ? [event] : [];
  }
  reset() {
    this.buffer = "";
    this.decoder = new TextDecoder("utf-8", { fatal: false });
  }
  parseEvent(eventText) {
    const lines = eventText.split(`
`);
    const dataLines = [];
    for (const line of lines) {
      if (line.startsWith("data: ")) {
        dataLines.push(line.slice(6));
      } else if (line.startsWith("data:")) {
        dataLines.push(line.slice(5));
      }
    }
    if (dataLines.length === 0)
      return null;
    try {
      const json = dataLines.join(`
`);
      return JSON.parse(json);
    } catch {
      return null;
    }
  }
}

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/errors.js
class AgentError extends Error {
  code;
  statusCode;
  details;
  cause;
  constructor(message, code, options) {
    super(message);
    this.name = "AgentError";
    this.code = code;
    this.statusCode = options?.statusCode;
    this.details = options?.details;
    this.cause = options?.cause;
    if ("captureStackTrace" in Error) {
      Error.captureStackTrace(this, AgentError);
    }
  }
  getUserMessage() {
    if (!this.details) {
      return this.message;
    }
    const detailMessages = Object.entries(this.details).flatMap(([field, messages]) => messages.map((msg) => `${field}: ${msg}`)).join("; ");
    return `${this.message}. ${detailMessages}`;
  }
  is(code) {
    return this.code === code;
  }
  isConflict() {
    return this.code === "CONFLICT";
  }
  isBadRequest() {
    return this.code === "BAD_REQUEST";
  }
  isNotFound() {
    return this.code === "NOT_FOUND";
  }
  isUnauthorized() {
    return this.code === "UNAUTHORIZED";
  }
  isForbidden() {
    return this.code === "FORBIDDEN";
  }
  toJSON() {
    return {
      name: this.name,
      message: this.message,
      code: this.code,
      statusCode: this.statusCode,
      details: this.details,
      stack: this.stack
    };
  }
}
function parseErrorResponse(response, body) {
  const title = body?.title || body?.error || body?.message;
  const details = body?.errors;
  let code = "UNKNOWN";
  let message = title || `HTTP ${response.status}`;
  switch (response.status) {
    case 400:
      code = "BAD_REQUEST";
      message = title || "Bad Request";
      break;
    case 401:
      code = "UNAUTHORIZED";
      message = title || "Unauthorized";
      break;
    case 403:
      code = "FORBIDDEN";
      message = title || "Forbidden";
      break;
    case 404:
      code = "NOT_FOUND";
      message = title || "Resource not found";
      break;
    case 409:
      code = "CONFLICT";
      message = title || "Conflict";
      break;
    case 422:
      code = "VALIDATION_ERROR";
      message = title || "Validation failed";
      break;
    case 429:
      code = "RATE_LIMITED";
      message = title || "Too many requests";
      break;
    case 500:
      code = "INTERNAL_SERVER_ERROR";
      message = title || "Internal server error";
      break;
    case 502:
      code = "BAD_GATEWAY";
      message = title || "Bad gateway";
      break;
    case 503:
      code = "SERVICE_UNAVAILABLE";
      message = title || "Service unavailable";
      break;
    case 504:
      code = "GATEWAY_TIMEOUT";
      message = title || "Gateway timeout";
      break;
  }
  return new AgentError(message, code, {
    statusCode: response.status,
    details
  });
}

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/transports/sse.js
class SseTransport {
  baseUrl;
  requestOptions;
  agentId;
  sessionId;
  branchId;
  abortController;
  eventHandler;
  errorHandler;
  closeHandler;
  _connected = false;
  constructor(baseUrl, requestOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, "");
    this.requestOptions = requestOptions;
  }
  fetch(input, init = {}) {
    const headers = {
      ...this.requestOptions.headers ?? {},
      ...init.headers ?? {}
    };
    return globalThis.fetch(input, {
      ...init,
      credentials: this.requestOptions.credentials,
      headers
    });
  }
  url(path) {
    const base = /^[a-z][a-z\d+.-]*:\/\//i.test(this.baseUrl) ? this.baseUrl : `${globalThis.location?.origin ?? "http://localhost"}${this.baseUrl.startsWith("/") ? "" : "/"}${this.baseUrl}`;
    return new URL(`${base}${path}`);
  }
  get connected() {
    return this._connected;
  }
  async connect(scope) {
    if (this._connected) {
      throw new Error("Already connected. Call disconnect() first.");
    }
    this.sessionId = scope?.sessionId;
    this.branchId = scope?.branchId || "main";
    this.agentId = scope?.agentId;
  }
  async run(input, options) {
    const sessionId = "sessionId" in input ? input.sessionId : undefined;
    const branchId = "branchId" in input ? input.branchId : undefined;
    const agentId = "agentId" in input ? input.agentId : undefined;
    this.sessionId = sessionId ?? this.sessionId;
    this.branchId = branchId ?? this.branchId ?? "main";
    this.agentId = agentId ?? this.agentId;
    if (this.isMiddlewareResponse(input)) {
      await this.postMiddlewareResponse(input);
      return;
    }
    if (this._connected) {
      throw new Error("Already connected. Call disconnect() first.");
    }
    if (!this.sessionId) {
      throw new Error("Input event must include sessionId for SSE run()");
    }
    if (!this.agentId) {
      throw new Error("Input event must include agentId for SSE run()");
    }
    this.abortController = new AbortController;
    const signal = options?.signal ? this.combineSignals(options.signal, this.abortController.signal) : this.abortController.signal;
    const isTextInput = input.type === EventTypes.USER_TEXT_INPUT;
    const url = isTextInput ? `${this.baseUrl}/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/stream` : `${this.baseUrl}/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/events/stream`;
    const response = await this.fetch(url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream"
      },
      body: JSON.stringify(isTextInput ? { text: input.text, runConfig: input.runConfig } : input),
      signal
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`HTTP ${response.status}: ${text}`);
    }
    if (!response.body) {
      throw new Error("No response body");
    }
    this._connected = true;
    await this.processStream(response.body);
  }
  async processStream(body) {
    const reader = body.getReader();
    const parser = new SseParser;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) {
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
      if (error?.name !== "AbortError") {
        this.errorHandler?.(error);
      }
    } finally {
      reader.releaseLock();
      this._connected = false;
      this.closeHandler?.();
    }
  }
  isMiddlewareResponse(input) {
    return input.type === EventTypes.PERMISSION_RESPONSE || input.type === EventTypes.CONTINUATION_RESPONSE || input.type === EventTypes.CLARIFICATION_RESPONSE || input.type === EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
  }
  async postMiddlewareResponse(message) {
    if (!this.agentId || !this.sessionId || !this.branchId) {
      throw new Error("Not connected");
    }
    const endpoint = this.getEndpointForMessage(message);
    const response = await this.fetch(`${this.baseUrl}${endpoint}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(message)
    });
    if (!response.ok) {
      if (response.status === 409) {
        throw new AgentError("Response was not accepted because the request is no longer pending", "STALE_RESPONSE", { statusCode: response.status });
      }
      const body = await response.json().catch(() => null);
      throw parseErrorResponse(response, body);
    }
  }
  getEndpointForMessage(message) {
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
        throw new Error(`Unknown message type: ${message.type}`);
    }
  }
  onEvent(handler) {
    this.eventHandler = handler;
  }
  onError(handler) {
    this.errorHandler = handler;
  }
  onClose(handler) {
    this.closeHandler = handler;
  }
  disconnect() {
    this.abortController?.abort();
    this._connected = false;
  }
  combineSignals(...signals) {
    const controller = new AbortController;
    for (const signal of signals) {
      if (signal.aborted) {
        controller.abort(signal.reason);
        return controller.signal;
      }
      signal.addEventListener("abort", () => controller.abort(signal.reason), { once: true });
    }
    return controller.signal;
  }
  async listSessions(options) {
    const url = this.url(`/sessions`);
    if (options?.limit)
      url.searchParams.set("limit", options.limit.toString());
    if (options?.offset)
      url.searchParams.set("offset", options.offset.toString());
    if (options?.sortBy)
      url.searchParams.set("sortBy", options.sortBy);
    if (options?.sortDirection)
      url.searchParams.set("sortDirection", options.sortDirection);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list sessions: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getSession(sessionId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createSession(options) {
    const response = await this.fetch(`${this.baseUrl}/sessions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(options || {})
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async updateSession(sessionId, request) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to update session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteSession(sessionId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}`, {
      method: "DELETE"
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete session: HTTP ${response.status}: ${text}`);
    }
  }
  async listBranches(sessionId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list branches: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranch(sessionId, branchId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createBranch(sessionId, options) {
    const agentId = options?.agentId ?? this.agentId;
    if (!agentId) {
      throw new Error("createBranch() requires agentId");
    }
    const { agentId: _agentId, ...body } = options ?? {};
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}/sessions/${sessionId}/branches`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async forkBranch(sessionId, branchId, options) {
    const agentId = options.agentId ?? this.agentId;
    if (!agentId) {
      throw new Error("forkBranch() requires agentId");
    }
    const { agentId: _agentId, ...body } = options;
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/fork`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to fork branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteBranch(sessionId, branchId, options) {
    const url = this.url(`/sessions/${sessionId}/branches/${branchId}`);
    if (options?.recursive)
      url.searchParams.set("recursive", "true");
    const response = await this.fetch(url.toString(), {
      method: "DELETE"
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }
  async getBranchMessages(sessionId, branchId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/messages`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch messages: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranchSiblings(sessionId, branchId) {
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/branches/${branchId}/siblings`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get siblings: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getNextSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.nextSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.nextSiblingId);
  }
  async getPreviousSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.previousSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.previousSiblingId);
  }
  async listAgents() {
    const response = await this.fetch(`${this.baseUrl}/agents`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list agents: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getAgent(agentId) {
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404)
      return null;
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createAgent(request) {
    const response = await this.fetch(`${this.baseUrl}/agents`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async updateAgent(agentId, request) {
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to update agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteAgent(agentId) {
    const response = await this.fetch(`${this.baseUrl}/agents/${agentId}`, {
      method: "DELETE"
    });
    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete agent: HTTP ${response.status}: ${text}`);
    }
  }
  async getScores(evaluatorName, from, to) {
    const url = this.url(`/evals/scores`);
    url.searchParams.set("evaluatorName", evaluatorName);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getScoresByBranch(sessionId, branchId) {
    const url = this.url(`/evals/scores/by-branch`);
    url.searchParams.set("sessionId", sessionId);
    if (branchId)
      url.searchParams.set("branchId", branchId);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores by branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async writeScore(record) {
    const response = await this.fetch(`${this.baseUrl}/evals/scores`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(record)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to write score: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getEvaluatorSummary(from, to) {
    const url = this.url(`/evals/evaluators`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get evaluator summary: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getRiskAutonomyDistribution(from, to) {
    const url = this.url(`/evals/risk-autonomy`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get risk/autonomy distribution: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getTrend(evaluatorName, from, to, bucketSize) {
    const url = this.url(`/evals/trend/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set("from", from);
    url.searchParams.set("to", to);
    if (bucketSize)
      url.searchParams.set("bucketSize", bucketSize);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get trend: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getPassRate(evaluatorName, from, to) {
    const url = this.url(`/evals/pass-rate/${encodeURIComponent(evaluatorName)}`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get pass rate: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getFailureRate(evaluatorName, from, to) {
    const url = this.url(`/evals/failure-rate/${encodeURIComponent(evaluatorName)}`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get failure rate: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getAgentComparison(evaluatorName, agentNames, from, to) {
    const url = this.url(`/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set("agentNames", agentNames.join(","));
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get agent comparison: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames) {
    const url = this.url(`/evals/branch-comparison`);
    url.searchParams.set("sessionId", sessionId);
    url.searchParams.set("branchId1", branchId1);
    url.searchParams.set("branchId2", branchId2);
    url.searchParams.set("evaluatorNames", evaluatorNames.join(","));
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch comparison: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getToolUsage(from, to) {
    const url = this.url(`/evals/tool-usage`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get tool usage: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getCost(from, to) {
    const url = this.url(`/evals/cost`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get cost breakdown: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getScoresByVersion(evaluatorName, version) {
    const url = this.url(`/evals/scores/by-version`);
    url.searchParams.set("evaluatorName", evaluatorName);
    url.searchParams.set("version", version);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores by version: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async uploadAsset(sessionId, file, name) {
    const form = new FormData;
    form.append("file", file, name ?? (file instanceof File ? file.name : "upload"));
    const response = await this.fetch(`${this.baseUrl}/sessions/${sessionId}/assets`, {
      method: "POST",
      body: form
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to upload asset: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
}

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/transports/websocket.js
class WebSocketTransport {
  baseUrl;
  httpBaseUrl;
  requestOptions;
  ws;
  scope;
  eventHandler;
  errorHandler;
  closeHandler;
  constructor(baseUrl, requestOptions = {}) {
    this.requestOptions = requestOptions;
    this.httpBaseUrl = baseUrl.replace(/^ws:/, "http:").replace(/^wss:/, "https:").replace(/\/$/, "");
    this.baseUrl = baseUrl.replace(/^http:/, "ws:").replace(/^https:/, "wss:").replace(/\/$/, "");
  }
  fetch(input, init = {}) {
    const headers = {
      ...this.requestOptions.headers ?? {},
      ...init.headers ?? {}
    };
    return globalThis.fetch(input, {
      ...init,
      credentials: this.requestOptions.credentials,
      headers
    });
  }
  url(path) {
    const base = /^[a-z][a-z\d+.-]*:\/\//i.test(this.httpBaseUrl) ? this.httpBaseUrl : `${globalThis.location?.origin ?? "http://localhost"}${this.httpBaseUrl.startsWith("/") ? "" : "/"}${this.httpBaseUrl}`;
    return new URL(`${base}${path}`);
  }
  get connected() {
    return this.ws?.readyState === WebSocket.OPEN;
  }
  connect(scope) {
    if (this.ws?.readyState === WebSocket.OPEN || this.ws?.readyState === WebSocket.CONNECTING) {
      return Promise.reject(new Error("Already connected. Call disconnect() first."));
    }
    return new Promise((resolve, reject) => {
      if (!scope?.sessionId) {
        reject(new Error("WebSocket connect() requires sessionId"));
        return;
      }
      if (!scope.agentId) {
        reject(new Error("WebSocket connect() requires agentId"));
        return;
      }
      this.scope = scope;
      const sessionId = scope.sessionId;
      const branchId = scope.branchId || "main";
      const url = `${this.baseUrl}/agents/${scope.agentId}/sessions/${sessionId}/branches/${branchId}/ws`;
      try {
        this.ws = new WebSocket(url);
      } catch (error) {
        reject(new Error(`Failed to create WebSocket: ${error}`));
        return;
      }
      const cleanup = () => {
        scope.signal?.removeEventListener("abort", onAbort);
      };
      const onAbort = () => {
        cleanup();
        this.ws?.close();
        reject(new DOMException("Aborted", "AbortError"));
      };
      if (scope.signal?.aborted) {
        reject(new DOMException("Aborted", "AbortError"));
        return;
      }
      scope.signal?.addEventListener("abort", onAbort, { once: true });
      this.ws.onopen = () => {
        cleanup();
        resolve();
      };
      this.ws.onmessage = (event) => {
        try {
          const agentEvent = JSON.parse(event.data);
          this.eventHandler?.(agentEvent);
        } catch {}
      };
      this.ws.onerror = () => {
        cleanup();
        const error = new Error("WebSocket error");
        this.errorHandler?.(error);
        reject(error);
      };
      this.ws.onclose = () => {
        cleanup();
        this.closeHandler?.();
      };
    });
  }
  async run(input) {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      throw new Error("WebSocket not connected");
    }
    this.ws.send(JSON.stringify({
      ...input,
      sessionId: "sessionId" in input ? input.sessionId ?? this.scope?.sessionId : this.scope?.sessionId,
      branchId: "branchId" in input ? input.branchId ?? this.scope?.branchId ?? "main" : this.scope?.branchId ?? "main",
      agentId: "agentId" in input ? input.agentId ?? this.scope?.agentId : this.scope?.agentId
    }));
  }
  onEvent(handler) {
    this.eventHandler = handler;
  }
  onError(handler) {
    this.errorHandler = handler;
  }
  onClose(handler) {
    this.closeHandler = handler;
  }
  disconnect() {
    this.ws?.close();
  }
  async listSessions(options) {
    const url = this.url(`/sessions`);
    if (options?.limit)
      url.searchParams.set("limit", options.limit.toString());
    if (options?.offset)
      url.searchParams.set("offset", options.offset.toString());
    if (options?.sortBy)
      url.searchParams.set("sortBy", options.sortBy);
    if (options?.sortDirection)
      url.searchParams.set("sortDirection", options.sortDirection);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list sessions: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getSession(sessionId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createSession(options) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(options || {})
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async updateSession(sessionId, request) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to update session: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteSession(sessionId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}`, {
      method: "DELETE"
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete session: HTTP ${response.status}: ${text}`);
    }
  }
  async listBranches(sessionId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}/branches`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list branches: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranch(sessionId, branchId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}/branches/${branchId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createBranch(sessionId, options) {
    const agentId = options?.agentId ?? this.scope?.agentId;
    if (!agentId) {
      throw new Error("createBranch() requires agentId");
    }
    const { agentId: _agentId, ...body } = options ?? {};
    const response = await this.fetch(`${this.httpBaseUrl}/agents/${agentId}/sessions/${sessionId}/branches`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async forkBranch(sessionId, branchId, options) {
    const agentId = options.agentId ?? this.scope?.agentId;
    if (!agentId) {
      throw new Error("forkBranch() requires agentId");
    }
    const { agentId: _agentId, ...body } = options;
    const response = await this.fetch(`${this.httpBaseUrl}/agents/${agentId}/sessions/${sessionId}/branches/${branchId}/fork`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to fork branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteBranch(sessionId, branchId, options) {
    const url = this.url(`/sessions/${sessionId}/branches/${branchId}`);
    if (options?.recursive)
      url.searchParams.set("recursive", "true");
    const response = await this.fetch(url.toString(), {
      method: "DELETE"
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete branch: HTTP ${response.status}: ${text}`);
    }
  }
  async getBranchMessages(sessionId, branchId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}/branches/${branchId}/messages`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch messages: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranchSiblings(sessionId, branchId) {
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}/branches/${branchId}/siblings`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get siblings: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getNextSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.nextSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.nextSiblingId);
  }
  async getPreviousSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.previousSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.previousSiblingId);
  }
  async listAgents() {
    const response = await this.fetch(`${this.httpBaseUrl}/agents`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to list agents: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getAgent(agentId) {
    const response = await this.fetch(`${this.httpBaseUrl}/agents/${agentId}`, {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (response.status === 404)
      return null;
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async createAgent(request) {
    const response = await this.fetch(`${this.httpBaseUrl}/agents`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to create agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async updateAgent(agentId, request) {
    const response = await this.fetch(`${this.httpBaseUrl}/agents/${agentId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to update agent: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async deleteAgent(agentId) {
    const response = await this.fetch(`${this.httpBaseUrl}/agents/${agentId}`, {
      method: "DELETE"
    });
    if (response.status === 404) {
      throw new Error(`Agent not found: ${agentId}`);
    }
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to delete agent: HTTP ${response.status}: ${text}`);
    }
  }
  async getScores(evaluatorName, from, to) {
    const url = this.url(`/evals/scores`);
    url.searchParams.set("evaluatorName", evaluatorName);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getScoresByBranch(sessionId, branchId) {
    const url = this.url(`/evals/scores/by-branch`);
    url.searchParams.set("sessionId", sessionId);
    if (branchId)
      url.searchParams.set("branchId", branchId);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores by branch: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async writeScore(record) {
    const response = await this.fetch(`${this.httpBaseUrl}/evals/scores`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(record)
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to write score: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getEvaluatorSummary(from, to) {
    const url = this.url(`/evals/evaluators`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get evaluator summary: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getRiskAutonomyDistribution(from, to) {
    const url = this.url(`/evals/risk-autonomy`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get risk/autonomy distribution: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getTrend(evaluatorName, from, to, bucketSize) {
    const url = this.url(`/evals/trend/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set("from", from);
    url.searchParams.set("to", to);
    if (bucketSize)
      url.searchParams.set("bucketSize", bucketSize);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get trend: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getPassRate(evaluatorName, from, to) {
    const url = this.url(`/evals/pass-rate/${encodeURIComponent(evaluatorName)}`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get pass rate: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getFailureRate(evaluatorName, from, to) {
    const url = this.url(`/evals/failure-rate/${encodeURIComponent(evaluatorName)}`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get failure rate: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getAgentComparison(evaluatorName, agentNames, from, to) {
    const url = this.url(`/evals/agent-comparison/${encodeURIComponent(evaluatorName)}`);
    url.searchParams.set("agentNames", agentNames.join(","));
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get agent comparison: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames) {
    const url = this.url(`/evals/branch-comparison`);
    url.searchParams.set("sessionId", sessionId);
    url.searchParams.set("branchId1", branchId1);
    url.searchParams.set("branchId2", branchId2);
    url.searchParams.set("evaluatorNames", evaluatorNames.join(","));
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get branch comparison: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getToolUsage(from, to) {
    const url = this.url(`/evals/tool-usage`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get tool usage: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getCost(from, to) {
    const url = this.url(`/evals/cost`);
    if (from)
      url.searchParams.set("from", from);
    if (to)
      url.searchParams.set("to", to);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get cost breakdown: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async getScoresByVersion(evaluatorName, version) {
    const url = this.url(`/evals/scores/by-version`);
    url.searchParams.set("evaluatorName", evaluatorName);
    url.searchParams.set("version", version);
    const response = await this.fetch(url.toString(), {
      method: "GET",
      headers: { "Content-Type": "application/json" }
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to get scores by version: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
  async uploadAsset(sessionId, file, name) {
    const form = new FormData;
    form.append("file", file, name ?? (file instanceof File ? file.name : "upload"));
    const response = await this.fetch(`${this.httpBaseUrl}/sessions/${sessionId}/assets`, {
      method: "POST",
      body: form
    });
    if (!response.ok) {
      const text = await response.text().catch(() => "Unknown error");
      throw new Error(`Failed to upload asset: HTTP ${response.status}: ${text}`);
    }
    return response.json();
  }
}

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/transports/maui.js
class MauiTransport {
  eventHandler;
  errorHandler;
  closeHandler;
  _connected = false;
  currentStreamId;
  currentSessionId;
  messageListener;
  get connected() {
    return this._connected;
  }
  async connect(_scope) {}
  async run(input) {
    if (!window.HybridWebView) {
      throw new Error("MAUI HybridWebView not available");
    }
    if (input.type !== EventTypes.USER_TEXT_INPUT) {
      await this.sendInputEvent(input);
      return;
    }
    this.cleanup();
    this.messageListener = (event) => {
      const customEvent = event;
      const message = customEvent.detail.message;
      const [type, streamId, ...jsonParts] = message.split(":");
      if (type === "agent_event" && streamId === this.currentStreamId) {
        try {
          const eventJson = jsonParts.join(":");
          const agentEvent = JSON.parse(eventJson);
          this.eventHandler?.(agentEvent);
        } catch (error) {
          this.errorHandler?.(new Error(`Failed to parse event: ${error}`));
        }
      } else if (type === "agent_complete" && streamId === this.currentStreamId) {
        this._connected = false;
        this.closeHandler?.();
      } else if (type === "agent_error" && streamId === this.currentStreamId) {
        const errorMessage = jsonParts.join(":");
        this.errorHandler?.(new Error(errorMessage));
        this._connected = false;
        this.closeHandler?.();
      }
    };
    window.addEventListener("HybridWebViewMessageReceived", this.messageListener);
    try {
      this.currentSessionId = input.sessionId;
      this.currentStreamId = await window.HybridWebView.InvokeDotNet("StartStream", [
        input.text,
        input.sessionId,
        input.branchId || "main",
        input.runConfig ? JSON.stringify(input.runConfig) : undefined,
        input.agentId
      ]);
      this._connected = true;
    } catch (error) {
      this.cleanup();
      throw new Error(`Failed to start stream: ${error}`);
    }
  }
  async sendInputEvent(message) {
    if (!window.HybridWebView) {
      throw new Error("MAUI HybridWebView not available");
    }
    switch (message.type) {
      case EventTypes.PERMISSION_RESPONSE:
        {
          const request = {
            SessionId: this.currentSessionId,
            PermissionId: message.permissionId,
            SourceName: message.sourceName,
            Approved: message.approved,
            Reason: message.reason,
            Choice: message.choice
          };
          await window.HybridWebView.InvokeDotNet("RespondToPermission", [
            JSON.stringify(request)
          ]);
        }
        break;
      case EventTypes.CLIENT_TOOL_INVOKE_RESPONSE:
        {
          const request = {
            SessionId: this.currentSessionId,
            RequestId: message.requestId,
            Success: message.success,
            Content: message.content,
            ErrorMessage: message.errorMessage
          };
          await window.HybridWebView.InvokeDotNet("RespondToClientTool", [
            JSON.stringify(request)
          ]);
        }
        break;
      default:
        throw new Error(`Unsupported message type: ${message.type}`);
    }
  }
  onEvent(handler) {
    this.eventHandler = handler;
  }
  onError(handler) {
    this.errorHandler = handler;
  }
  onClose(handler) {
    this.closeHandler = handler;
  }
  disconnect() {
    if (this.currentStreamId && window.HybridWebView) {
      try {
        window.HybridWebView.InvokeDotNet("StopStream", [this.currentStreamId]);
      } catch {}
    }
    this.cleanup();
  }
  cleanup() {
    if (this.messageListener) {
      window.removeEventListener("HybridWebViewMessageReceived", this.messageListener);
      this.messageListener = undefined;
    }
    this._connected = false;
    this.currentStreamId = undefined;
    this.currentSessionId = undefined;
  }
  async listSessions(options) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const request = options ? JSON.stringify({ offset: options.offset, limit: options.limit }) : undefined;
    const json = await window.HybridWebView.InvokeDotNet("SearchSessions", [request]);
    return JSON.parse(json);
  }
  async getSession(sessionId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    try {
      const json = await window.HybridWebView.InvokeDotNet("GetSession", [sessionId]);
      return JSON.parse(json);
    } catch {
      return null;
    }
  }
  async createSession(options) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("CreateSession", [
      options?.sessionId,
      options?.metadata ? JSON.stringify(options.metadata) : undefined
    ]);
    return JSON.parse(json);
  }
  async updateSession(sessionId, request) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("UpdateSession", [
      sessionId,
      request.metadata ? JSON.stringify(request.metadata) : undefined
    ]);
    return JSON.parse(json);
  }
  async deleteSession(sessionId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    await window.HybridWebView.InvokeDotNet("DeleteSession", [sessionId]);
  }
  async listBranches(sessionId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("ListBranches", [sessionId]);
    return JSON.parse(json);
  }
  async getBranch(sessionId, branchId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    try {
      const json = await window.HybridWebView.InvokeDotNet("GetBranch", [sessionId, branchId]);
      return JSON.parse(json);
    } catch {
      return null;
    }
  }
  async createBranch(sessionId, options) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("CreateBranch", [
      sessionId,
      options?.branchId,
      options?.name,
      options?.description
    ]);
    return JSON.parse(json);
  }
  async forkBranch(sessionId, branchId, options) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("ForkBranch", [
      sessionId,
      branchId,
      options.newBranchId,
      options.fromMessageIndex,
      options.name,
      options.description
    ]);
    return JSON.parse(json);
  }
  async deleteBranch(sessionId, branchId, options) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    await window.HybridWebView.InvokeDotNet("DeleteBranch", [sessionId, branchId, options?.recursive ?? false]);
  }
  async getBranchMessages(sessionId, branchId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("GetBranchMessages", [sessionId, branchId]);
    return JSON.parse(json);
  }
  async getBranchSiblings(sessionId, branchId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("GetBranchSiblings", [sessionId, branchId]);
    return JSON.parse(json);
  }
  async getNextSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.nextSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.nextSiblingId);
  }
  async getPreviousSibling(sessionId, branchId) {
    const branch = await this.getBranch(sessionId, branchId);
    if (!branch?.previousSiblingId) {
      return null;
    }
    return this.getBranch(sessionId, branch.previousSiblingId);
  }
  listAgents() {
    return Promise.reject(new Error("Agent CRUD is not supported in MauiTransport"));
  }
  getAgent(_agentId) {
    return Promise.reject(new Error("Agent CRUD is not supported in MauiTransport"));
  }
  createAgent(_request) {
    return Promise.reject(new Error("Agent CRUD is not supported in MauiTransport"));
  }
  updateAgent(_agentId, _request) {
    return Promise.reject(new Error("Agent CRUD is not supported in MauiTransport"));
  }
  deleteAgent(_agentId) {
    return Promise.reject(new Error("Agent CRUD is not supported in MauiTransport"));
  }
  getScores(_evaluatorName, _from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getScoresByBranch(_sessionId, _branchId) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  writeScore(_record) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getEvaluatorSummary(_from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getRiskAutonomyDistribution(_from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getTrend(_evaluatorName, _from, _to, _bucketSize) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getPassRate(_evaluatorName, _from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getFailureRate(_evaluatorName, _from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getAgentComparison(_evaluatorName, _agentNames, _from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getBranchComparison(_sessionId, _branchId1, _branchId2, _evaluatorNames) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getToolUsage(_from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getCost(_from, _to) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  getScoresByVersion(_evaluatorName, _version) {
    return Promise.reject(new Error("Eval queries are not supported in MauiTransport"));
  }
  async uploadAsset(sessionId, file, name) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const fileName = name ?? (file instanceof File ? file.name : "upload");
    const contentType = file.type || "application/octet-stream";
    const buffer = await file.arrayBuffer();
    const base64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
    const json = await window.HybridWebView.InvokeDotNet("UploadAsset", [
      sessionId,
      fileName,
      contentType,
      base64
    ]);
    return JSON.parse(json);
  }
  async listAssets(sessionId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    const json = await window.HybridWebView.InvokeDotNet("ListAssets", [sessionId]);
    return JSON.parse(json);
  }
  async deleteAsset(sessionId, assetId) {
    if (!window.HybridWebView)
      throw new Error("MAUI HybridWebView not available");
    await window.HybridWebView.InvokeDotNet("DeleteAsset", [sessionId, assetId]);
  }
}

// ../../HPD-AI-Framework/typescript/hpd-agent-client/dist/client.js
class AgentClient {
  config;
  transport;
  typedHandlers = new Map;
  anyHandlers = new Set;
  errorHandlers = new Set;
  outputDispatchQueue = Promise.resolve();
  constructor(config) {
    this.config = typeof config === "string" ? { baseUrl: config } : config;
    this.transport = this.createTransport();
    this.transport.onEvent((event) => {
      this.outputDispatchQueue = this.outputDispatchQueue.then(() => this.dispatchOutputEvent(event));
    });
    this.transport.onError((error) => {
      this.dispatchError(error);
    });
  }
  createTransport() {
    const type = this.config.transport ?? "sse";
    const requestOptions = {
      headers: this.config.headers,
      credentials: this.config.credentials
    };
    switch (type) {
      case "websocket":
        return new WebSocketTransport(this.config.baseUrl, requestOptions);
      case "maui":
        return new MauiTransport;
      case "sse":
      default:
        return new SseTransport(this.config.baseUrl, requestOptions);
    }
  }
  async start(scope) {
    await this.transport.connect(scope);
  }
  async stop() {
    this.transport.disconnect();
  }
  async run(input, options) {
    await this.transport.run(input, options);
    await this.outputDispatchQueue;
  }
  on(type, handler) {
    const handlers = this.typedHandlers.get(type) ?? new Set;
    const stored = handler;
    handlers.add(stored);
    this.typedHandlers.set(type, handlers);
    return {
      dispose: () => {
        handlers.delete(stored);
        if (handlers.size === 0) {
          this.typedHandlers.delete(type);
        }
      }
    };
  }
  onAny(handler) {
    this.anyHandlers.add(handler);
    return {
      dispose: () => {
        this.anyHandlers.delete(handler);
      }
    };
  }
  onError(handler) {
    this.errorHandlers.add(handler);
    return {
      dispose: () => {
        this.errorHandlers.delete(handler);
      }
    };
  }
  async dispatchOutputEvent(event) {
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
        augmentation: toolResponse.augmentation
      });
    }
  }
  async dispatchError(error) {
    for (const handler of this.errorHandlers) {
      await handler(error);
    }
  }
  abort() {
    this.transport.disconnect();
  }
  get streaming() {
    return this.transport.connected;
  }
  listSessions(options) {
    return this.transport.listSessions(options);
  }
  getSession(sessionId) {
    return this.transport.getSession(sessionId);
  }
  createSession(options) {
    return this.transport.createSession(options);
  }
  updateSession(sessionId, request) {
    return this.transport.updateSession(sessionId, request);
  }
  deleteSession(sessionId) {
    return this.transport.deleteSession(sessionId);
  }
  listBranches(sessionId) {
    return this.transport.listBranches(sessionId);
  }
  getBranch(sessionId, branchId) {
    return this.transport.getBranch(sessionId, branchId);
  }
  createBranch(sessionId, options) {
    return this.transport.createBranch(sessionId, options);
  }
  forkBranch(sessionId, branchId, options) {
    return this.transport.forkBranch(sessionId, branchId, options);
  }
  deleteBranch(sessionId, branchId, options) {
    return this.transport.deleteBranch(sessionId, branchId, options);
  }
  getBranchMessages(sessionId, branchId) {
    return this.transport.getBranchMessages(sessionId, branchId);
  }
  getBranchSiblings(sessionId, branchId) {
    return this.transport.getBranchSiblings(sessionId, branchId);
  }
  getNextSibling(sessionId, branchId) {
    return this.transport.getNextSibling(sessionId, branchId);
  }
  getPreviousSibling(sessionId, branchId) {
    return this.transport.getPreviousSibling(sessionId, branchId);
  }
  listAgents() {
    return this.transport.listAgents();
  }
  getAgent(agentId) {
    return this.transport.getAgent(agentId);
  }
  createAgent(request) {
    return this.transport.createAgent(request);
  }
  updateAgent(agentId, request) {
    return this.transport.updateAgent(agentId, request);
  }
  deleteAgent(agentId) {
    return this.transport.deleteAgent(agentId);
  }
  getScores(evaluatorName, from, to) {
    return this.transport.getScores(evaluatorName, from, to);
  }
  getScoresByBranch(sessionId, branchId) {
    return this.transport.getScoresByBranch(sessionId, branchId);
  }
  writeScore(record) {
    return this.transport.writeScore(record);
  }
  getEvaluatorSummary(from, to) {
    return this.transport.getEvaluatorSummary(from, to);
  }
  getRiskAutonomyDistribution(from, to) {
    return this.transport.getRiskAutonomyDistribution(from, to);
  }
  getTrend(evaluatorName, from, to, bucketSize) {
    return this.transport.getTrend(evaluatorName, from, to, bucketSize);
  }
  getPassRate(evaluatorName, from, to) {
    return this.transport.getPassRate(evaluatorName, from, to);
  }
  getFailureRate(evaluatorName, from, to) {
    return this.transport.getFailureRate(evaluatorName, from, to);
  }
  getAgentComparison(evaluatorName, agentNames, from, to) {
    return this.transport.getAgentComparison(evaluatorName, agentNames, from, to);
  }
  getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames) {
    return this.transport.getBranchComparison(sessionId, branchId1, branchId2, evaluatorNames);
  }
  getToolUsage(from, to) {
    return this.transport.getToolUsage(from, to);
  }
  getCost(from, to) {
    return this.transport.getCost(from, to);
  }
  getScoresByVersion(evaluatorName, version) {
    return this.transport.getScoresByVersion(evaluatorName, version);
  }
  uploadAsset(sessionId, file, name) {
    return this.transport.uploadAsset(sessionId, file, name);
  }
}
// wwwroot/src/app.ts
var $ = (id) => document.getElementById(id);
var agentId = "hpdos-agent";
var branchId = "main";
var browserHarness = {
  name: "hpdos.browser",
  description: "Tools for inspecting the current HPD-OS browser shell and creating artifacts in the UI.",
  startCollapsed: false,
  tools: [
    {
      name: "get_active_view",
      description: "Return the active HPD-OS view and selected browser-shell context.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    },
    {
      name: "create_artifact",
      description: "Create or replace a browser-side artifact and show it inline in the chat.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string", description: "Optional stable artifact id. A generated id is used when omitted." },
          title: { type: "string", description: "Short title shown in the artifact card." },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"], description: "Artifact rendering type." },
          content: { type: "string", description: "Artifact content." },
          language: { type: "string", description: "Optional code language label." },
          open: { type: "boolean", description: "Whether to focus the artifact card immediately." }
        },
        required: ["title", "type", "content"],
        additionalProperties: false
      }
    },
    {
      name: "update_artifact",
      description: "Update an existing browser-side artifact.",
      parametersSchema: {
        type: "object",
        properties: {
          id: { type: "string" },
          title: { type: "string" },
          type: { type: "string", enum: ["text", "markdown", "code", "html", "json"] },
          content: { type: "string" },
          language: { type: "string" },
          open: { type: "boolean" }
        },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "open_artifact",
      description: "Open an existing browser-side artifact by id.",
      parametersSchema: {
        type: "object",
        properties: { id: { type: "string" } },
        required: ["id"],
        additionalProperties: false
      }
    },
    {
      name: "list_artifacts",
      description: "List browser-side artifacts currently available in the shell.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    },
    {
      name: "close_artifact",
      description: "Unfocus the current inline artifact.",
      parametersSchema: {
        type: "object",
        properties: {},
        additionalProperties: false
      }
    }
  ]
};
var client = new AgentClient({
  baseUrl: "/api/hpd-agent",
  credentials: "include",
  onClientToolInvoke: handleClientToolInvoke
});
var chatState = {
  sessionId: localStorage.getItem("hpdos.sessionId") || "",
  toolNodes: new Map,
  artifacts: new Map,
  openArtifactId: null,
  assistant: null
};
marked.setOptions({ gfm: true, breaks: true });
client.on(EventTypes.TEXT_DELTA, (event) => {
  if (typeof event.text === "string") {
    renderMarkdownDelta(ensureAssistant(), event.text);
  }
});
client.on(EventTypes.MESSAGE_TURN_ERROR, (event) => {
  showChatError(new Error(event.message || "Message turn failed."));
});
client.on(EventTypes.TOOL_CALL_START, (event) => renderTool(event, "started"));
client.on(EventTypes.TOOL_CALL_ARGS, (event) => renderToolBlock(event.callId, "Args", jsonish(event.argsJson)));
client.on(EventTypes.TOOL_CALL_RESULT, (event) => {
  renderToolBlock(event.callId, "Result", event.result?.text || JSON.stringify(event.result || {}, null, 2));
});
client.onError(showChatError);
async function handleClientToolInvoke(request) {
  const toolName = cleanClientToolName(request.toolName);
  if (toolName === "get_active_view") {
    return {
      requestId: request.requestId,
      success: true,
      content: [{
        type: "json",
        value: currentClientContext()
      }]
    };
  }
  if (toolName === "create_artifact") {
    const artifact = applyArtifactFunctionCall(toolName, request.arguments);
    if (!artifact)
      return errorToolResponse(request.requestId, "Failed to create artifact.");
    renderArtifactCard(artifact);
    if (request.arguments.open !== false)
      openArtifact(artifact.id);
    return jsonToolResponse(request.requestId, { artifact, opened: chatState.openArtifactId === artifact.id });
  }
  if (toolName === "update_artifact") {
    const id = stringArg(request.arguments, "id");
    if (!id || !chatState.artifacts.has(id))
      return errorToolResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
    const artifact = applyArtifactFunctionCall(toolName, request.arguments);
    if (!artifact)
      return errorToolResponse(request.requestId, `Artifact not found: ${id}`);
    renderArtifactCard(artifact);
    if (request.arguments.open === true || chatState.openArtifactId === artifact.id)
      openArtifact(artifact.id);
    return jsonToolResponse(request.requestId, { artifact, opened: chatState.openArtifactId === artifact.id });
  }
  if (toolName === "open_artifact") {
    const id = stringArg(request.arguments, "id");
    if (!id || !chatState.artifacts.has(id))
      return errorToolResponse(request.requestId, `Artifact not found: ${id || "(missing id)"}`);
    openArtifact(id);
    return jsonToolResponse(request.requestId, { id, opened: true });
  }
  if (toolName === "list_artifacts") {
    return jsonToolResponse(request.requestId, {
      openArtifactId: chatState.openArtifactId,
      artifacts: Array.from(chatState.artifacts.values()).map(({ id, title, type, language, updatedAt }) => ({ id, title, type, language, updatedAt }))
    });
  }
  if (toolName === "close_artifact") {
    closeArtifact();
    return jsonToolResponse(request.requestId, { opened: false });
  }
  return errorToolResponse(request.requestId, `Unknown client tool: ${request.toolName}`);
}
document.body.addEventListener("click", (event) => {
  const target = event.target;
  const nav = target?.closest(".nav");
  if (nav) {
    document.querySelectorAll(".nav").forEach((node) => node.removeAttribute("aria-current"));
    nav.setAttribute("aria-current", "page");
  }
  if (target?.closest("[data-format-graph]"))
    formatGraphJson();
  const artifactViewButton = target?.closest("[data-artifact-view]");
  if (artifactViewButton) {
    const card = artifactViewButton.closest("[data-artifact-card]");
    const artifact = chatState.artifacts.get(card?.dataset.artifactCard || "");
    const view = artifactViewButton.dataset.artifactView === "code" ? "code" : "preview";
    if (artifact)
      renderArtifactCard(artifact, false, view);
    return;
  }
  const artifactButton = target?.closest("[data-artifact-id]");
  if (artifactButton) {
    openArtifact(artifactButton.dataset.artifactId || "");
  }
  if (target?.closest("[data-show-handlers]")) {
    $("handlerList")?.classList.toggle("hidden");
    $("graphPreview")?.classList.toggle("hidden");
  }
});
document.body.addEventListener("htmx:afterSwap", (event) => {
  if (event.detail.target.id === "view")
    wireChat();
  renderGraphPreview();
  autoHideToast();
});
document.body.addEventListener("input", (event) => {
  const target = event.target;
  if (target?.id === "graphJson")
    debounceGraphPreview();
  if (target?.id === "text") {
    target.style.height = "auto";
    target.style.height = `${target.scrollHeight}px`;
  }
});
function wireChat() {
  const composer = $("composer");
  if (!composer || composer.dataset.wired)
    return;
  composer.dataset.wired = "true";
  composer.addEventListener("submit", submitChat);
  $("newSession")?.addEventListener("click", newSession);
  resetTurnState();
  loadSessions();
  hydrateSession();
}
async function newSession() {
  const session2 = await client.createSession();
  chatState.sessionId = session2.id;
  localStorage.setItem("hpdos.sessionId", session2.id);
  $("chatStack")?.replaceChildren();
  clearArtifacts();
  await loadSessions();
}
async function ensureSession() {
  if (!chatState.sessionId)
    await newSession();
}
async function loadSessions() {
  const sessionsNode = $("sessions");
  if (!sessionsNode)
    return;
  try {
    const sessions = await client.listSessions();
    const sessionCount = $("sessionCount");
    if (sessionCount)
      sessionCount.textContent = String(sessions.length);
    sessionsNode.innerHTML = sessions.sort((a, b) => new Date(b.lastActivity || b.createdAt || 0).getTime() - new Date(a.lastActivity || a.createdAt || 0).getTime()).map((session2) => `
      <button class="mb-1 grid w-full rounded-hpd border px-3 py-2 text-left ${session2.id === chatState.sessionId ? "border-blue-200 bg-blue-50" : "border-transparent hover:border-hpd-line hover:bg-white"}" data-session="${escapeHtml(session2.id)}" type="button">
        <span class="truncate text-sm font-black">${escapeHtml(session2.metadata?.title || `Chat ${String(session2.id).slice(0, 6).toUpperCase()}`)}</span>
        <span class="mt-1 truncate text-xs font-semibold text-hpd-muted">${formatDate(session2.lastActivity || session2.createdAt)}</span>
      </button>`).join("") || '<div class="rounded-hpd border border-dashed border-hpd-line bg-white/70 p-3 text-sm text-hpd-muted">No recent sessions.</div>';
    sessionsNode.querySelectorAll("[data-session]").forEach((button) => button.addEventListener("click", () => switchSession(button.dataset.session || "")));
  } catch (error) {
    sessionsNode.innerHTML = `<div class="rounded-hpd border border-red-200 bg-red-50 p-3 text-sm text-red-700">${escapeHtml(messageOf(error))}</div>`;
  }
}
async function switchSession(id) {
  chatState.sessionId = id;
  localStorage.setItem("hpdos.sessionId", id);
  $("chatStack")?.replaceChildren();
  resetTurnState();
  clearArtifacts();
  await hydrateSession();
  await loadSessions();
}
async function hydrateSession() {
  if (!chatState.sessionId || !$("chatStack"))
    return;
  setBusy(true);
  try {
    const messages = await client.getBranchMessages(chatState.sessionId, branchId);
    $("chatStack")?.replaceChildren();
    clearArtifacts();
    for (const message of messages || [])
      hydrateMessage(message);
  } catch (error) {
    showChatError(error);
  } finally {
    setBusy(false);
  }
}
async function submitChat(event) {
  event.preventDefault();
  const textInput = $("text");
  const text = textInput?.value.trim() || "";
  if (!text)
    return;
  appendMessage(text, "user");
  if (textInput)
    textInput.value = "";
  resetTurnState();
  setBusy(true);
  try {
    await sendChat(text);
  } catch (error) {
    showChatError(error);
  } finally {
    setBusy(false);
    await loadSessions();
  }
}
async function sendChat(text) {
  const providerKey = $("provider")?.value.trim();
  const modelId = $("model")?.value.trim();
  if (!providerKey || !modelId)
    throw new Error("Provider and model are required.");
  await ensureSession();
  await client.run({
    type: EventTypes.USER_TEXT_INPUT,
    agentId,
    sessionId: chatState.sessionId,
    branchId,
    text,
    runConfig: {
      providerKey,
      modelId,
      clientToolInput: {
        clientHarnesses: [browserHarness],
        context: [{
          key: "hpdos.activeView",
          description: "The current HPD-OS shell view.",
          value: currentClientContext()
        }]
      }
    }
  });
  if (!chatState.assistant)
    appendMessage("(no text output)", "assistant");
}
function currentClientContext() {
  const activeNav = document.querySelector(".nav[aria-current='page']");
  let graphId;
  try {
    const graph = JSON.parse($("graphJson")?.value || "{}");
    graphId = typeof graph.graphId === "string" ? graph.graphId : undefined;
  } catch {
    graphId = undefined;
  }
  return {
    activeView: activeNav?.getAttribute("hx-get")?.includes("workflows") ? "workflows" : "chat",
    sessionId: chatState.sessionId || undefined,
    graphId,
    openArtifactId: chatState.openArtifactId,
    artifactCount: chatState.artifacts.size
  };
}
function hydrateMessage(message) {
  const role = String(message.role || "").toLowerCase();
  const text = (message.contents || []).filter(isTextContent).map((content) => content.text || "").filter(Boolean).join(`
`);
  if (text && (role === "user" || role === "assistant")) {
    const node = appendMessage(role === "user" ? text : "", role);
    if (role === "assistant")
      renderMarkdownDelta(node, text);
  }
  for (const content of message.contents || []) {
    if (isFunctionCallContent(content)) {
      hydrateFunctionCall(content, message.timestamp);
    } else if (isFunctionResultContent(content)) {
      renderToolBlock(content.callId, "Result", content.result || "");
    }
  }
}
function isTextContent(content) {
  return content.$type === "text";
}
function isFunctionCallContent(content) {
  return content.$type === "functionCall";
}
function isFunctionResultContent(content) {
  return content.$type === "functionResult";
}
function hydrateFunctionCall(content, timestamp) {
  const toolName = cleanClientToolName(content.name);
  if (isArtifactToolName(toolName)) {
    try {
      const artifact = applyArtifactFunctionCall(toolName, content.arguments || {}, timestamp);
      if (artifact)
        renderArtifactCard(artifact, false);
      updateArtifactCards();
    } catch {
      renderTool({ type: EventTypes.TOOL_CALL_START, callId: content.callId, name: content.name }, "started");
      renderToolBlock(content.callId, "Args", content.arguments || {});
    }
    return;
  }
  renderTool({ type: EventTypes.TOOL_CALL_START, callId: content.callId, name: content.name }, "started");
  if (content.arguments && Object.keys(content.arguments).length)
    renderToolBlock(content.callId, "Args", content.arguments);
}
function isArtifactToolName(toolName) {
  return toolName === "create_artifact" || toolName === "update_artifact" || toolName === "open_artifact" || toolName === "close_artifact" || toolName === "list_artifacts";
}
function applyArtifactFunctionCall(toolName, args, timestamp = new Date().toISOString()) {
  if (toolName === "create_artifact") {
    const artifact = upsertArtifact(args, true, timestamp);
    if (args.open !== false)
      chatState.openArtifactId = artifact.id;
    return artifact;
  }
  if (toolName === "update_artifact") {
    const artifact = upsertArtifact(args, false, timestamp);
    if (args.open === true || chatState.openArtifactId === artifact.id)
      chatState.openArtifactId = artifact.id;
    return artifact;
  }
  if (toolName === "open_artifact") {
    const id = stringArg(args, "id");
    if (id && chatState.artifacts.has(id)) {
      chatState.openArtifactId = id;
      return chatState.artifacts.get(id) || null;
    }
  }
  if (toolName === "close_artifact") {
    chatState.openArtifactId = null;
  }
  return null;
}
function upsertArtifact(args, create, timestamp = new Date().toISOString()) {
  const id = stringArg(args, "id") || `artifact-${crypto.randomUUID().slice(0, 8)}`;
  const previous = chatState.artifacts.get(id);
  const artifact = {
    id,
    title: stringArg(args, "title") || previous?.title || "Untitled artifact",
    type: artifactTypeArg(args, "type") || previous?.type || "text",
    content: stringArg(args, "content") ?? previous?.content ?? "",
    language: stringArg(args, "language") || previous?.language,
    createdAt: previous?.createdAt || timestamp,
    updatedAt: timestamp
  };
  if (!create && !previous)
    throw new Error(`Artifact not found: ${id}`);
  chatState.artifacts.set(id, artifact);
  return artifact;
}
function renderArtifactCard(artifact, shouldScroll = true, view) {
  const stack = $("chatStack");
  if (!stack)
    return;
  let card = document.querySelector(`[data-artifact-card="${cssEscape(artifact.id)}"]`);
  const selectedView = view || (card?.dataset.artifactView === "code" ? "code" : "preview");
  if (!card) {
    const wrap = document.createElement("article");
    wrap.className = "flex justify-center";
    card = document.createElement("section");
    card.className = "artifact-card w-full max-w-4xl";
    card.dataset.artifactCard = artifact.id;
    card.dataset.artifactId = artifact.id;
    wrap.appendChild(card);
    stack.appendChild(wrap);
  }
  card.dataset.open = String(chatState.openArtifactId === artifact.id);
  card.dataset.artifactView = selectedView;
  card.innerHTML = `
    <div class="artifact-card-header">
      <div class="min-w-0">
        <div class="flex items-center gap-2">
          <span class="hpd-badge font-mono">${escapeHtml(artifactIcon(artifact.type))}</span>
          <h3 class="truncate text-sm font-black" data-artifact-title>${escapeHtml(artifact.title)}</h3>
        </div>
        <p class="mt-1 truncate text-xs font-semibold text-hpd-muted">${escapeHtml(artifact.type)}${artifact.language ? ` / ${escapeHtml(artifact.language)}` : ""}</p>
      </div>
      <div class="flex shrink-0 items-center gap-2">
        <div class="flex rounded-full border border-hpd-line bg-hpd-soft p-0.5">
          <button class="artifact-tab" data-artifact-view="preview" aria-current="${selectedView === "preview"}" type="button">Preview</button>
          <button class="artifact-tab" data-artifact-view="code" aria-current="${selectedView === "code"}" type="button">Code</button>
        </div>
        <span class="hpd-badge">${formatDate(artifact.updatedAt)}</span>
      </div>
    </div>
    <div class="artifact-card-body" data-artifact-content></div>
  `;
  const content = card.querySelector("[data-artifact-content]");
  if (content)
    renderArtifactContent(content, artifact, selectedView);
  if (shouldScroll)
    scrollChat();
}
function updateArtifactCards() {
  document.querySelectorAll("[data-artifact-card]").forEach((node) => {
    node.dataset.open = String(chatState.openArtifactId === node.dataset.artifactCard);
  });
}
function openArtifact(id) {
  const artifact = chatState.artifacts.get(id);
  if (!artifact)
    return;
  chatState.openArtifactId = id;
  renderArtifactCard(artifact, false);
  updateArtifactCards();
  document.querySelector(`[data-artifact-card="${cssEscape(id)}"]`)?.scrollIntoView({ block: "nearest", behavior: "smooth" });
}
function closeArtifact() {
  chatState.openArtifactId = null;
  updateArtifactCards();
}
function clearArtifacts() {
  chatState.artifacts.clear();
  closeArtifact();
  document.querySelectorAll("[data-artifact-card]").forEach((node) => node.closest("article")?.remove());
}
function renderArtifactContent(target, artifact, view = "preview") {
  target.replaceChildren();
  const wrap = document.createElement("div");
  wrap.className = "artifact-render";
  if (view === "code") {
    const pre = document.createElement("pre");
    pre.textContent = artifact.type === "json" ? jsonish(artifact.content) : artifact.content;
    wrap.appendChild(pre);
  } else if (artifact.type === "markdown") {
    wrap.innerHTML = DOMPurify.sanitize(marked.parse(artifact.content));
  } else if (artifact.type === "html") {
    const frame = document.createElement("iframe");
    frame.className = "artifact-frame";
    frame.setAttribute("sandbox", "allow-scripts");
    frame.srcdoc = artifact.content;
    target.appendChild(frame);
    return;
  } else if (artifact.type === "json") {
    const pre = document.createElement("pre");
    pre.textContent = jsonish(artifact.content);
    wrap.appendChild(pre);
  } else if (artifact.type === "code") {
    const pre = document.createElement("pre");
    pre.textContent = artifact.content;
    wrap.appendChild(pre);
  } else {
    wrap.textContent = artifact.content;
  }
  target.appendChild(wrap);
}
function jsonToolResponse(requestId, value) {
  return { requestId, success: true, content: [{ type: "json", value }] };
}
function errorToolResponse(requestId, errorMessage) {
  return { requestId, success: false, content: [], errorMessage };
}
function stringArg(args, key) {
  const value = args[key];
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}
function artifactTypeArg(args, key) {
  const value = stringArg(args, key);
  return value === "text" || value === "markdown" || value === "code" || value === "html" || value === "json" ? value : undefined;
}
function artifactIcon(type) {
  if (type === "code")
    return "{}";
  if (type === "markdown")
    return "MD";
  if (type === "html")
    return "<>";
  if (type === "json")
    return "[]";
  return "T";
}
function cleanClientToolName(value) {
  return value.split(".").pop() || value;
}
function cssEscape(value) {
  return "CSS" in window && typeof CSS.escape === "function" ? CSS.escape(value) : value.replaceAll('"', "\\\"");
}
function appendMessage(content, role) {
  const stack = $("chatStack");
  if (!stack)
    throw new Error("Chat stack is not mounted.");
  const wrap = document.createElement("article");
  wrap.className = `flex ${role === "user" ? "justify-end" : "justify-start"}`;
  const node = document.createElement("div");
  node.className = role === "user" ? "max-w-[78%] rounded-2xl rounded-tr-md bg-hpd-blue px-4 py-3 text-sm leading-6 text-white shadow-sm" : "message-markdown max-w-[82%] rounded-2xl rounded-tl-md border border-hpd-line bg-white px-4 py-3 text-sm leading-6 shadow-sm";
  node.textContent = content;
  node.dataset.markdown = "";
  wrap.appendChild(node);
  stack.appendChild(wrap);
  scrollChat();
  return node;
}
function ensureAssistant() {
  if (!chatState.assistant)
    chatState.assistant = appendMessage("", "assistant");
  return chatState.assistant;
}
function renderMarkdownDelta(node, delta) {
  node.dataset.markdown = (node.dataset.markdown || "") + delta;
  node.innerHTML = DOMPurify.sanitize(marked.parse(node.dataset.markdown));
  scrollChat();
}
function renderTool(event, suffix) {
  const stack = $("chatStack");
  if (!stack)
    return;
  const id = event.callId || crypto.randomUUID();
  const node = document.createElement("details");
  node.className = "rounded-hpd border border-hpd-line bg-white shadow-sm";
  node.innerHTML = `<summary class="cursor-pointer px-4 py-3 text-sm font-black">${escapeHtml(cleanName(event.functionName || event.name || "tool"))} ${suffix}</summary><div class="grid gap-2 border-t border-hpd-line p-3" data-body></div>`;
  stack.appendChild(node);
  chatState.toolNodes.set(id, node);
}
function renderToolBlock(id, label, value) {
  const toolId = id || crypto.randomUUID();
  if (!chatState.toolNodes.has(toolId)) {
    renderTool({ type: EventTypes.TOOL_CALL_START, callId: toolId, name: "tool" }, "event");
  }
  const body = chatState.toolNodes.get(toolId)?.querySelector("[data-body]");
  if (!body)
    return;
  const pre = document.createElement("pre");
  pre.className = "json-box max-h-56";
  pre.textContent = `${label}
${String(value || "").slice(0, 12000)}`;
  body.appendChild(pre);
}
function resetTurnState() {
  chatState.toolNodes.clear();
  chatState.assistant = null;
}
function setBusy(busy) {
  const send = $("send");
  const newSessionButton = $("newSession");
  if (send)
    send.disabled = busy;
  if (newSessionButton)
    newSessionButton.disabled = busy;
}
function showChatError(error) {
  const node = appendMessage("", "assistant");
  node.classList.add("border-red-200", "bg-red-50", "text-red-800");
  node.textContent = messageOf(error);
}
var graphTimer;
function debounceGraphPreview() {
  clearTimeout(graphTimer);
  graphTimer = window.setTimeout(renderGraphPreview, 150);
}
function formatGraphJson() {
  const graphJson = $("graphJson");
  if (!graphJson)
    return;
  try {
    graphJson.value = JSON.stringify(JSON.parse(graphJson.value || "{}"), null, 2);
    renderGraphPreview();
  } catch (error) {
    toast(messageOf(error));
  }
}
function renderGraphPreview() {
  const preview = $("graphPreview");
  const source = $("graphJson");
  if (!preview || !source)
    return;
  let graph;
  try {
    graph = JSON.parse(source.value || "{}");
  } catch {
    return;
  }
  preview.innerHTML = "";
  const ids = ["START", ...Object.keys(graph.nodes || {}), "END"];
  const nodes = { START: { id: "START", name: "Start", type: "Start" }, ...graph.nodes || {}, END: { id: "END", name: "End", type: "End" } };
  const positions = {};
  ids.forEach((id, i) => positions[id] = { x: 32 + i * 190, y: 140 + i % 2 * 100 });
  preview.style.minWidth = `${Math.max(640, ids.length * 210)}px`;
  for (const edge of graph.edges || [])
    drawEdge(preview, positions[edge.from], positions[edge.to]);
  for (const id of ids) {
    const node = nodes[id];
    const pos = positions[id];
    const div = document.createElement("div");
    div.className = "absolute grid w-40 gap-1 rounded-hpd border border-hpd-line bg-white p-3 shadow-hpd";
    div.style.left = `${pos.x}px`;
    div.style.top = `${pos.y}px`;
    div.innerHTML = `<strong class="truncate text-sm">${escapeHtml(node.name || node.id)}</strong><span class="hpd-badge">${escapeHtml(node.type || "Handler")}</span><code class="truncate text-xs text-hpd-muted">${escapeHtml(node.handlerName || node.id)}</code>`;
    preview.appendChild(div);
  }
}
function drawEdge(parent, from, to) {
  if (!from || !to)
    return;
  const start = { x: from.x + 160, y: from.y + 36 };
  const end = { x: to.x, y: to.y + 36 };
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const line = document.createElement("div");
  line.className = "absolute h-0.5 origin-left bg-slate-400";
  line.style.left = `${start.x}px`;
  line.style.top = `${start.y}px`;
  line.style.width = `${Math.max(24, Math.hypot(dx, dy))}px`;
  line.style.transform = `rotate(${Math.atan2(dy, dx) * 180 / Math.PI}deg)`;
  parent.appendChild(line);
}
function toast(message) {
  const toastNode = $("toast");
  if (!toastNode)
    return;
  toastNode.textContent = message;
  toastNode.classList.remove("hidden");
  autoHideToast();
}
function autoHideToast() {
  const toastNode = $("toast");
  if (!toastNode || !toastNode.textContent?.trim())
    return;
  toastNode.classList.remove("hidden");
  clearTimeout(autoHideToast.timer);
  autoHideToast.timer = window.setTimeout(() => toastNode.classList.add("hidden"), 3200);
}
autoHideToast.timer = 0;
function scrollChat() {
  requestAnimationFrame(() => $("chat")?.scrollTo({ top: $("chat")?.scrollHeight || 0, behavior: "smooth" }));
}
function escapeHtml(value) {
  return String(value ?? "").replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
}
function formatDate(value) {
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? "" : date.toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
}
function jsonish(value) {
  try {
    return JSON.stringify(JSON.parse(String(value)), null, 2);
  } catch {
    return String(value || "");
  }
}
function cleanName(value) {
  return String(value || "unknown").split(".").pop()?.replace(/^tool_/, "").replace(/_[A-Za-z0-9-]{8,}$/, "") || "tool";
}
function messageOf(error) {
  return error instanceof Error ? error.message : String(error);
}
