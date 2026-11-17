export interface AgentStudioClient {
  readonly principalGeneration: string;
  listAgents(signal?: AbortSignal): Promise<unknown>;
  getAgent(agentId: string, signal?: AbortSignal): Promise<unknown>;
  getAgentConfiguration(agentId: string, signal?: AbortSignal): Promise<unknown>;
  preflightAgentConfiguration(agentId: string, config: unknown, signal?: AbortSignal): Promise<unknown>;
  listAgentSessions(agentId: string, signal?: AbortSignal): Promise<unknown>;
  getThreadActivity(agentId: string, sessionId: string, threadId: string, signal?: AbortSignal): Promise<unknown>;
  listSessions(signal?: AbortSignal): Promise<unknown>;
  getSession(sessionId: string, signal?: AbortSignal): Promise<unknown>;
  listThreads(sessionId: string, signal?: AbortSignal): Promise<unknown>;
  getThread(sessionId: string, threadId: string, signal?: AbortSignal): Promise<unknown>;
  listExecutions(agentId: string, sessionId: string, threadId: string, signal?: AbortSignal): Promise<unknown>;
  getExecution(agentId: string, sessionId: string, threadId: string, executionId: string, signal?: AbortSignal): Promise<unknown>;
  getFunction(harnessId: string, functionId: string, signal?: AbortSignal): Promise<unknown>;
  getFunctionAction(harnessId: string, functionId: string, actionId: string, signal?: AbortSignal): Promise<unknown>;
  listHarnesses(signal?: AbortSignal): Promise<unknown>;
  getHarness(harnessId: string, signal?: AbortSignal): Promise<unknown>;
  observabilitySummary(signal?: AbortSignal): Promise<unknown>;
  queryMetrics(query?: Readonly<Record<string, string>>, signal?: AbortSignal): Promise<unknown>;
  listEvents(query?: Readonly<Record<string, string>>, signal?: AbortSignal): Promise<unknown>;
  getEventPayload(eventId: string, signal?: AbortSignal): Promise<unknown>;
  listTraces(query?: Readonly<Record<string, string>>, signal?: AbortSignal): Promise<unknown>;
  getTrace(traceId: string, signal?: AbortSignal): Promise<unknown>;
}

export interface AgentStudioRuntimeContext {
  readonly client: AgentStudioClient;
  readonly applicationId: string;
  readonly principalGeneration: string;
  subscribeThreadActivity(
    target: unknown,
    sessionId: string,
    threadId: string,
    signal?: AbortSignal
  ): AsyncIterable<unknown>;
  subscribeEvents(signal?: AbortSignal): AsyncIterable<unknown>;
}

let active: AgentStudioRuntimeContext | null = null;

export function installAgentStudioRuntime(value: AgentStudioRuntimeContext): () => void {
  if (active !== null) throw new Error("Agent Studio already has an active principal generation.");
  active = value;
  return () => {
    if (active === value) active = null;
  };
}

export function requireAgentStudioRuntime(): AgentStudioRuntimeContext {
  if (active === null) throw new Error("Agent Studio generated-client authority is unavailable.");
  return active;
}
