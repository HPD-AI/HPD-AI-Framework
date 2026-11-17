export interface PermissionProjection {
  readonly kind: string;
  readonly requiresPermission: boolean;
  readonly authority: string | null;
  readonly policyId: string | null;
  readonly risk: string | null;
  readonly reason: string | null;
  readonly source: string | null;
}

export interface AIFunctionActionProjection {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly schema: unknown;
  readonly permission: PermissionProjection;
  readonly invocationMode: string;
  readonly invocationHandling: string;
  readonly replayPolicy: string;
  readonly resourceToken: string | null;
}

export interface AIFunctionProjection {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly schema: unknown;
  readonly permission: PermissionProjection;
  readonly invocationMode: string;
  readonly invocationHandling: string;
  readonly replayPolicy: string;
  readonly kind: string;
  readonly returnSchema: unknown;
  readonly schemaFingerprint: string | null;
  readonly ownerHarnessIdentity: string | null;
  readonly declaringHarnessIdentity: string | null;
  readonly declarationMemberName: string | null;
  readonly implementationIdentity: string | null;
  readonly actionArgumentName: string | null;
  readonly actionDiscriminator: string | null;
  readonly parentContainerIds: readonly string[];
  readonly reveals: readonly string[];
  readonly agents: readonly string[];
  readonly variantCount: number;
  readonly isSubAgent: boolean;
  readonly executionModel: string | null;
  readonly resourceToken: string | null;
  readonly actions: readonly AIFunctionActionProjection[];
}

export interface ToolHarnessProjection {
  readonly id: string;
  readonly name: string;
  readonly description: string;
  readonly agents: readonly string[];
  readonly functions: readonly AIFunctionProjection[];
  readonly functionCount: number;
  readonly actionCount: number;
  readonly activationIdentity: string;
  readonly ownerType: string;
  readonly assembly: string;
  readonly origin: string;
  readonly availability: string;
  readonly runtimeState: string;
  readonly instanceStatus: string;
  readonly configuredFunctions: readonly string[];
  readonly availableFunctions: readonly string[];
  readonly skills: readonly string[];
  readonly subAgents: readonly string[];
  readonly mcpServers: readonly string[];
  readonly openApiSources: readonly string[];
  readonly multiAgentWorkflows: readonly string[];
  readonly inheritedHarnessIdentities: readonly string[];
  readonly collapsed: boolean;
  readonly collapsingDescription: string;
  readonly functionResult: string;
  readonly systemPrompt: string;
  readonly settings: {
    readonly available: boolean;
    readonly configurationType: string;
    readonly supportsRunOverrides: boolean;
    readonly hasValidator: boolean;
    readonly schema: unknown;
    readonly defaults: unknown;
  };
  readonly metadata: {
    readonly typed: boolean;
    readonly type: string;
  };
  readonly dependencies: readonly {
    readonly name: string;
    readonly typeName: string;
    readonly kind: string;
    readonly optional: boolean;
    readonly resolutionSource: string;
  }[];
  readonly middleware: readonly {
    readonly name: string;
    readonly type: string;
    readonly activation: string;
    readonly ownership: string;
    readonly usesHarnessConfiguration: boolean;
    readonly requiresCanonicalWorkspaceIdentity: boolean;
    readonly configurationType: string | null;
  }[];
  readonly diagnostics: readonly string[];
  readonly capabilityGraph: {
    readonly nodes: readonly {
      readonly id: string;
      readonly kind: string;
      readonly label: string;
      readonly description: string;
    }[];
    readonly edges: readonly {
      readonly source: string;
      readonly target: string;
      readonly relation: string;
    }[];
  };
  readonly resourceToken: string | null;
}

export interface AgentThreadProjection {
  readonly id: string;
  readonly sessionId: string;
  readonly parentThreadId: string | null;
  readonly defaultAgentId: string;
  readonly name: string;
  readonly kind: string;
  readonly visibility: string;
  readonly state: string;
  readonly messageCount: number;
  readonly createdAt: string;
  readonly lastActivity: string;
  readonly resourceToken: string | null;
}

export interface AgentSessionProjection {
  readonly id: string;
  readonly createdAt: string;
  readonly lastActivity: string;
  readonly description: string;
  readonly threads: readonly AgentThreadProjection[];
  readonly resourceToken: string | null;
}

export interface AgentExecutionProjection {
  readonly id: string;
  readonly sessionId: string;
  readonly threadId: string;
  readonly defaultAgentId: string;
  readonly state: string;
  readonly startedAt: string;
  readonly finishedAt: string;
  readonly durationMs: number;
  readonly selectedHarnessId: string;
  readonly selectedFunctionId: string;
  readonly selectedActionId: string;
  readonly permission: PermissionProjection;
  readonly resultSummary: string;
  readonly resourceToken: string | null;
}

export interface AgentThreadDetailProjection extends AgentThreadProjection {
  readonly children: readonly AgentThreadProjection[];
  readonly executions: readonly AgentExecutionProjection[];
}

export interface AgentTranscriptMessageProjection {
  readonly id: string;
  readonly role: string;
  readonly authorName: string;
  readonly createdAt: string;
  readonly text: string;
  readonly contentCount: number;
}

export interface AgentActivityEventProjection {
  readonly id: string;
  readonly type: string;
  readonly kind: string;
  readonly channel: string;
  readonly sequenceNumber: number;
  readonly occurredAt: string;
  readonly threadExecutionId: string;
  readonly traceId: string;
  readonly spanId: string;
  readonly parentSpanId: string;
}

export interface AgentPendingRequestProjection {
  readonly kind: string;
  readonly eventType: string;
  readonly requestId: string;
  readonly sourceName: string;
  readonly functionName: string;
  readonly action: string;
  readonly authority: string;
  readonly reason: string;
  readonly risk: string;
  readonly resource: string;
  readonly currentIteration: number;
  readonly maxIterations: number;
  readonly toolName: string;
  readonly callId: string;
  readonly description: string;
  readonly argumentsJson: string;
  readonly choices: readonly {
    readonly id: string;
    readonly label: string;
    readonly kind: string;
  }[];
  readonly questions: readonly {
    readonly id: string;
    readonly text: string;
    readonly header: string;
    readonly multiple: boolean;
    readonly recommendedOptionId: string;
    readonly options: readonly {
      readonly id: string;
      readonly label: string;
      readonly description: string;
    }[];
  }[];
}

export interface AgentThreadActivityProjection {
  readonly agentId: string;
  readonly sessionId: string;
  readonly threadId: string;
  readonly generation: number;
  readonly sequenceNumber: number;
  readonly lastActivity: string;
  readonly messages: readonly AgentTranscriptMessageProjection[];
  readonly events: readonly AgentActivityEventProjection[];
  readonly pendingRequests: readonly AgentPendingRequestProjection[];
  readonly activeExecutionId: string | null;
  readonly pendingRequestCount: number;
}

export interface AgentTraceSpanProjection {
  readonly id: string;
  readonly parentSpanId: string | null;
  readonly name: string;
  readonly kind: string;
  readonly status: string;
  readonly startedAt: string;
  readonly durationMs: number;
  readonly attributes: Readonly<Record<string, string>>;
  readonly spanEvents: readonly AgentTraceSpanEventProjection[];
}

export interface AgentTraceSpanEventProjection {
  readonly name: string;
  readonly timestamp: string;
  readonly attributes: Readonly<Record<string, string>>;
}

export interface AgentTraceProjection {
  readonly id: string;
  readonly name: string;
  readonly state: string;
  readonly startedAt: string;
  readonly durationMs: number;
  readonly agentId: string;
  readonly sessionId: string;
  readonly threadId: string;
  readonly executionId: string;
  readonly spanCount: number;
  readonly eventCount: number;
  readonly inputTokens: number | null;
  readonly outputTokens: number | null;
  readonly totalTokens: number | null;
  readonly error: string;
  readonly spanSource: string;
  readonly spans: readonly AgentTraceSpanProjection[];
  readonly events: readonly AgentTraceEventProjection[];
  readonly resourceToken: string | null;
}

export interface AgentTraceEventProjection {
  readonly id: string;
  readonly type: string;
  readonly kind: string;
  readonly channel: string;
  readonly sequenceNumber: number;
  readonly occurredAt: string;
  readonly threadExecutionId: string;
  readonly sessionId: string;
  readonly threadId: string;
  readonly traceId: string;
  readonly spanId: string;
  readonly parentSpanId: string;
  readonly agentId: string;
  readonly source: string;
  readonly generation: number;
  readonly isError: boolean;
  readonly payloadState: string;
  readonly attributes: Readonly<Record<string, string>>;
}

export interface AgentMetricProjection {
  readonly id: string;
  readonly label: string;
  readonly value: number | null;
  readonly unit: string;
  readonly available: boolean;
}

export interface AgentMetricsProjection {
  readonly source: string;
  readonly measures: readonly AgentMetricProjection[];
  readonly agents: readonly {
    readonly id: string;
    readonly traces: number;
    readonly failed: number;
    readonly averageDurationMs: number;
    readonly totalTokens: number;
  }[];
  readonly functions: readonly {
    readonly harness: string;
    readonly function: string;
    readonly calls: number;
  }[];
  readonly capabilities: {
    readonly serverFiltering: boolean;
    readonly liveTail: boolean;
    readonly payloadInspection: boolean;
    readonly timeRange: string;
    readonly truncated: boolean;
  };
}

export interface AgentEventCatalogProjection {
  readonly source: string;
  readonly count: number;
  readonly truncated: boolean;
  readonly capabilities: {
    readonly liveTail: boolean;
    readonly payloadInspection: boolean;
    readonly serverFiltering: boolean;
    readonly cursor: string;
  };
  readonly events: readonly AgentTraceEventProjection[];
}

export interface AgentEventPayloadProjection {
  readonly state: string;
  readonly eventId: string;
  readonly type: string;
  readonly format: string;
  readonly redacted: boolean;
  readonly payload: string | null;
}

export interface AgentConfigurationLayerProjection {
  readonly id: string;
  readonly label: string;
  readonly state: string;
  readonly description: string;
}

export interface AgentConfigurationHarnessProjection {
  readonly name: string;
  readonly allFunctions: boolean;
  readonly functions: readonly string[];
  readonly hasConfiguration: boolean;
  readonly hasMetadata: boolean;
  readonly middlewareConfigurationCount: number;
}

export interface AgentPreflightCheckProjection {
  readonly id: string;
  readonly label: string;
  readonly state: string;
  readonly summary: string;
}

export interface AgentConfigurationProjection {
  readonly agentId: string;
  readonly layers: readonly AgentConfigurationLayerProjection[];
  readonly name: string;
  readonly systemInstructions: string;
  readonly maxAgenticIterations: number;
  readonly maxSubAgentDepth: number;
  readonly toolHarnesses: readonly AgentConfigurationHarnessProjection[];
  readonly middlewareCount: number;
  readonly preflightState: string;
  readonly checks: readonly AgentPreflightCheckProjection[];
  readonly errors: readonly string[];
  readonly canonicalConfig: unknown;
}

export interface AgentConfigurationPreflightProjection {
  readonly state: string;
  readonly toolHarnessCount: number;
  readonly checks: readonly AgentPreflightCheckProjection[];
  readonly errors: readonly string[];
  readonly normalizedConfig: unknown;
}

export function readToolHarness(value: unknown): ToolHarnessProjection | null {
  const item = record(value);
  if (!item) return null;
  const id = text(item.id ?? item.harnessId);
  if (!id) return null;
  const functions = list(item.functions).flatMap(readFunction);
  const collapsing = record(item.collapsing);
  const settings = record(item.settings);
  const metadata = record(item.metadata);
  const graph = record(item.capabilityGraph);
  return {
    id,
    name: text(item.name) || id,
    description: text(item.description),
    agents: strings(item.agents),
    functions,
    functionCount: functions.length || count(item.functionCount),
    actionCount: functions.reduce((total, entry) => total + entry.actions.length, 0) || count(item.actionCount),
    activationIdentity: text(item.activationIdentity) || id,
    ownerType: text(item.ownerType),
    assembly: text(item.assembly),
    origin: text(item.origin) || 'unknown',
    availability: text(item.availability) || 'unavailable',
    runtimeState: text(item.runtimeState) || 'unknown',
    instanceStatus: text(item.instanceStatus) || 'unknown',
    configuredFunctions: strings(item.configuredFunctions),
    availableFunctions: strings(item.availableFunctions),
    skills: strings(item.skills),
    subAgents: strings(item.subAgents),
    mcpServers: strings(item.mcpServers),
    openApiSources: strings(item.openApiSources),
    multiAgentWorkflows: strings(item.multiAgentWorkflows),
    inheritedHarnessIdentities: strings(item.inheritedHarnessIdentities),
    collapsed: collapsing?.collapsed === true,
    collapsingDescription: text(collapsing?.description),
    functionResult: text(collapsing?.functionResult),
    systemPrompt: text(collapsing?.systemPrompt),
    settings: {
      available: settings?.available === true,
      configurationType: text(settings?.configurationType),
      supportsRunOverrides: settings?.supportsRunOverrides === true,
      hasValidator: settings?.hasValidator === true,
      schema: settings?.schema ?? null,
      defaults: settings?.defaults ?? null
    },
    metadata: {
      typed: metadata?.typed === true,
      type: text(metadata?.type)
    },
    dependencies: list(item.dependencies).flatMap(readDependency),
    middleware: list(item.middleware).flatMap(readMiddleware),
    diagnostics: strings(item.diagnostics),
    capabilityGraph: {
      nodes: list(graph?.nodes).flatMap(readGraphNode),
      edges: list(graph?.edges).flatMap(readGraphEdge)
    },
    resourceToken: text(item.resourceToken) || null
  };
}

export function readFunction(value: unknown): AIFunctionProjection[] {
  const item = record(value);
  if (!item) return [];
  const id = text(item.id ?? item.functionId);
  if (!id) return [];
  return [{
    id,
    name: text(item.name) || id,
    description: text(item.description),
    schema: item.schema ?? item.parameters ?? null,
    permission: permission(item.permission),
    invocationMode: text(item.invocationMode) || 'Unavailable',
    invocationHandling: text(item.invocationHandling) || 'Unavailable',
    replayPolicy: text(item.replayPolicy) || 'Unavailable',
    kind: text(item.kind) || 'Function',
    returnSchema: item.returnSchema ?? null,
    schemaFingerprint: text(item.schemaFingerprint) || null,
    ownerHarnessIdentity: text(item.ownerHarnessIdentity) || null,
    declaringHarnessIdentity: text(item.declaringHarnessIdentity) || null,
    declarationMemberName: text(item.declarationMemberName) || null,
    implementationIdentity: text(item.implementationIdentity) || null,
    actionArgumentName: text(item.actionArgumentName) || null,
    actionDiscriminator: text(item.actionDiscriminator) || null,
    parentContainerIds: strings(item.parentContainerIds),
    reveals: strings(item.reveals),
    agents: strings(item.agents),
    variantCount: count(item.variantCount) || 1,
    isSubAgent: item.isSubAgent === true,
    executionModel: text(item.executionModel) || null,
    resourceToken: text(item.resourceToken) || null,
    actions: list(item.actions).flatMap(readAction)
  }];
}

export function readAction(value: unknown): AIFunctionActionProjection[] {
  const item = record(value);
  if (!item) return [];
  const id = text(item.id ?? item.actionId);
  if (!id) return [];
  return [{
    id,
    name: text(item.name) || id,
    description: text(item.description),
    schema: item.schema ?? item.arguments ?? null,
    permission: permission(item.permission),
    invocationMode: text(item.invocationMode) || 'Unavailable',
    invocationHandling: text(item.invocationHandling) || 'Unavailable',
    replayPolicy: text(item.replayPolicy) || 'Unavailable',
    resourceToken: text(item.resourceToken) || null
  }];
}

export function readSession(value: unknown): AgentSessionProjection | null {
  const item = record(value);
  if (!item) return null;
  const id = text(item.id ?? item.sessionId);
  if (!id) return null;
  return {
    id,
    createdAt: text(item.createdAt),
    lastActivity: text(item.lastActivity),
    description: text(item.description),
    threads: list(item.threads).flatMap(readThread),
    resourceToken: text(item.resourceToken) || null
  };
}

export function readThread(value: unknown): AgentThreadProjection[] {
  const item = record(value);
  if (!item) return [];
  const id = text(item.id ?? item.threadId);
  if (!id) return [];
  return [{
    id,
    sessionId: text(item.sessionId),
    parentThreadId: text(item.parentThreadId) || null,
    defaultAgentId: text(item.defaultAgentId),
    name: text(item.name) || id,
    kind: text(item.kind) || 'Thread',
    visibility: text(item.visibility) || 'Unavailable',
    state: text(item.state ?? item.status) || 'Unavailable',
    messageCount: count(item.messageCount),
    createdAt: text(item.createdAt),
    lastActivity: text(item.lastActivity),
    resourceToken: text(item.resourceToken) || null
  }];
}

export function readThreadDetail(value: unknown): AgentThreadDetailProjection | null {
  const item = record(value);
  const thread = readThread(record(item?.thread) ?? item)[0];
  if (!item || !thread) return null;
  return {
    ...thread,
    children: list(item.childThreads).flatMap(readThread),
    executions: list(item.executions).flatMap(readExecution)
  };
}

export function readThreadActivity(value: unknown): AgentThreadActivityProjection | null {
  const item = record(value);
  const agentId = text(item?.agentId);
  const sessionId = text(item?.sessionId);
  const threadId = text(item?.threadId);
  if (!item || !agentId || !sessionId || !threadId) return null;
  return {
    agentId,
    sessionId,
    threadId,
    generation: count(item.generation),
    sequenceNumber: count(item.sequenceNumber),
    lastActivity: text(item.lastActivity),
    messages: list(item.messages).flatMap(readTranscriptMessage),
    events: list(item.events).flatMap(readActivityEvent),
    pendingRequests: list(item.pendingRequests).flatMap(readPendingRequest),
    activeExecutionId: text(item.activeExecutionId) || null,
    pendingRequestCount: count(item.pendingRequestCount)
  };
}

export function readExecution(value: unknown): AgentExecutionProjection[] {
  const item = record(value);
  if (!item) return [];
  const id = text(item.id ?? item.executionId ?? item.threadExecutionId);
  if (!id) return [];
  return [{
    id,
    sessionId: text(item.sessionId),
    threadId: text(item.threadId),
    defaultAgentId: text(item.defaultAgentId ?? item.agentId),
    state: text(item.state ?? item.status) || 'Unavailable',
    startedAt: text(item.startedAt),
    finishedAt: text(item.finishedAt),
    durationMs: number(item.durationMs),
    selectedHarnessId: text(item.selectedHarnessId),
    selectedFunctionId: text(item.selectedFunctionId),
    selectedActionId: text(item.selectedActionId),
    permission: permission(item.permission),
    resultSummary: text(item.resultSummary),
    resourceToken: text(item.resourceToken) || null
  }];
}

export function readTrace(value: unknown): AgentTraceProjection | null {
  const item = record(value);
  if (!item) return null;
  const id = text(item.id ?? item.traceId);
  if (!id) return null;
  const scope = record(item.scope);
  return {
    id,
    name: text(item.name) || id,
    state: text(item.state) || 'Unavailable',
    startedAt: text(item.startedAt),
    durationMs: number(item.durationMs),
    agentId: text(item.agentId ?? scope?.agentId),
    sessionId: text(item.sessionId ?? scope?.sessionId),
    threadId: text(item.threadId ?? scope?.threadId),
    executionId: text(item.executionId ?? scope?.executionId),
    spanCount: count(item.spanCount),
    eventCount: count(item.eventCount),
    inputTokens: nullableCount(item.inputTokens),
    outputTokens: nullableCount(item.outputTokens),
    totalTokens: nullableCount(item.totalTokens),
    error: text(item.error),
    spanSource: text(item.spanSource) || 'Unavailable',
    spans: list(item.spans).flatMap(readSpan),
    events: list(item.events).flatMap(readTraceEvent),
    resourceToken: text(item.resourceToken) || null
  };
}

export function readMetrics(value: unknown): AgentMetricsProjection | null {
  const item = record(value);
  if (!item || text(item.source) !== 'canonical-thread-journal') return null;
  const capabilities = record(item.capabilities);
  return {
    source: text(item.source),
    measures: list(item.measures).flatMap(readMetric),
    agents: list(item.agents).flatMap(entry => {
      const agent = record(entry);
      const id = text(agent?.id);
      return id ? [{
        id,
        traces: count(agent?.traces),
        failed: count(agent?.failed),
        averageDurationMs: number(agent?.averageDurationMs),
        totalTokens: count(agent?.totalTokens)
      }] : [];
    }),
    functions: list(item.functions).flatMap(entry => {
      const fn = record(entry);
      const name = text(fn?.function);
      return name ? [{
        harness: text(fn?.harness),
        function: name,
        calls: count(fn?.calls)
      }] : [];
    }),
    capabilities: {
      serverFiltering: capabilities?.serverFiltering === true,
      liveTail: capabilities?.liveTail === true,
      payloadInspection: capabilities?.payloadInspection === true,
      timeRange: text(capabilities?.timeRange) || 'Unavailable',
      truncated: capabilities?.truncated === true
    }
  };
}

export function readEventCatalog(value: unknown): AgentEventCatalogProjection | null {
  const item = record(value);
  if (!item || text(item.source) !== 'canonical-thread-journal') return null;
  const capabilities = record(item.capabilities);
  return {
    source: text(item.source),
    count: count(item.count),
    truncated: item.truncated === true,
    capabilities: {
      liveTail: capabilities?.liveTail === true,
      payloadInspection: capabilities?.payloadInspection === true,
      serverFiltering: capabilities?.serverFiltering === true,
      cursor: text(capabilities?.cursor)
    },
    events: list(item.items).flatMap(readTraceEvent)
  };
}

export function readLiveEvent(value: unknown): AgentTraceEventProjection | null {
  return readTraceEvent(value)[0] ?? null;
}

export function readEventPayload(value: unknown): AgentEventPayloadProjection | null {
  const item = record(value);
  const eventId = text(item?.eventId);
  if (!item || !eventId) return null;
  return {
    state: text(item.state) || 'unavailable',
    eventId,
    type: text(item.type),
    format: text(item.payloadFormat),
    redacted: item.redacted === true,
    payload: text(item.payload) || null
  };
}

export function readConfiguration(value: unknown): AgentConfigurationProjection | null {
  const item = record(value);
  if (!item) return null;
  const agentId = text(item.agentId);
  if (!agentId) return null;
  const effective = record(item.effective);
  const preflight = record(item.preflight);
  return {
    agentId,
    layers: list(item.layers).flatMap(readConfigurationLayer),
    name: text(effective?.name),
    systemInstructions: text(effective?.systemInstructions),
    maxAgenticIterations: count(effective?.maxAgenticIterations),
    maxSubAgentDepth: count(effective?.maxSubAgentDepth),
    toolHarnesses: list(effective?.toolHarnesses).flatMap(readConfigurationHarness),
    middlewareCount: count(effective?.middlewareCount),
    preflightState: text(preflight?.state) || 'unavailable',
    checks: list(preflight?.checks).flatMap(readPreflightCheck),
    errors: strings(preflight?.errors),
    canonicalConfig: item.canonicalConfig ?? null
  };
}

export function readConfigurationPreflight(value: unknown): AgentConfigurationPreflightProjection | null {
  const item = record(value);
  if (!item) return null;
  const state = text(item.state);
  if (state !== 'valid' && state !== 'invalid') return null;
  return {
    state,
    toolHarnessCount: count(item.toolHarnessCount),
    checks: list(item.checks).flatMap(readPreflightCheck),
    errors: strings(item.errors),
    normalizedConfig: item.normalizedConfig ?? null
  };
}

function readConfigurationLayer(value: unknown): AgentConfigurationLayerProjection[] {
  const item = record(value);
  const id = text(item?.id);
  return id ? [{ id, label: text(item?.label) || id, state: text(item?.state) || 'unavailable',
    description: text(item?.description) }] : [];
}

function readTranscriptMessage(value: unknown): AgentTranscriptMessageProjection[] {
  const item = record(value);
  const id = text(item?.id);
  if (!item || !id) return [];
  return [{
    id,
    role: text(item.role) || 'unknown',
    authorName: text(item.authorName),
    createdAt: text(item.createdAt),
    text: text(item.text),
    contentCount: count(item.contentCount)
  }];
}

function readActivityEvent(value: unknown): AgentActivityEventProjection[] {
  const item = record(value);
  const id = text(item?.id);
  if (!item || !id) return [];
  return [{
    id,
    type: text(item.type) || 'Event',
    kind: text(item.kind),
    channel: text(item.channel),
    sequenceNumber: count(item.sequenceNumber),
    occurredAt: text(item.occurredAt),
    threadExecutionId: text(item.threadExecutionId),
    traceId: text(item.traceId),
    spanId: text(item.spanId),
    parentSpanId: text(item.parentSpanId)
  }];
}

function readPendingRequest(value: unknown): AgentPendingRequestProjection[] {
  const item = record(value);
  const kind = text(item?.kind);
  const requestId = text(item?.requestId);
  if (!item || !kind || !requestId) return [];
  return [{
    kind,
    eventType: text(item.eventType),
    requestId,
    sourceName: text(item.sourceName),
    functionName: text(item.functionName),
    action: text(item.action),
    authority: text(item.authority),
    reason: text(item.reason),
    risk: text(item.risk),
    resource: text(item.resource),
    currentIteration: count(item.currentIteration),
    maxIterations: count(item.maxIterations),
    toolName: text(item.toolName),
    callId: text(item.callId),
    description: text(item.description),
    argumentsJson: text(item.argumentsJson),
    choices: list(item.choices).flatMap(entry => {
      const choice = record(entry);
      const id = text(choice?.id);
      return id ? [{ id, label: text(choice?.label) || id, kind: text(choice?.kind) }] : [];
    }),
    questions: list(item.questions).flatMap(entry => {
      const question = record(entry);
      const id = text(question?.id);
      if (!question || !id) return [];
      return [{
        id,
        text: text(question.text),
        header: text(question.header),
        multiple: question.multiple === true,
        recommendedOptionId: text(question.recommendedOptionId),
        options: list(question.options).flatMap(optionValue => {
          const option = record(optionValue);
          const optionId = text(option?.id);
          return optionId
            ? [{ id: optionId, label: text(option?.label) || optionId, description: text(option?.description) }]
            : [];
        })
      }];
    })
  }];
}

function readConfigurationHarness(value: unknown): AgentConfigurationHarnessProjection[] {
  const item = record(value);
  const name = text(item?.name);
  return name ? [{
    name,
    allFunctions: item?.allFunctions === true,
    functions: strings(item?.functions),
    hasConfiguration: item?.hasConfiguration === true,
    hasMetadata: item?.hasMetadata === true,
    middlewareConfigurationCount: count(item?.middlewareConfigurationCount)
  }] : [];
}

function readPreflightCheck(value: unknown): AgentPreflightCheckProjection[] {
  const item = record(value);
  const id = text(item?.id);
  return id ? [{ id, label: text(item?.label) || id, state: text(item?.state) || 'unavailable',
    summary: text(item?.summary) }] : [];
}

function readSpan(value: unknown): AgentTraceSpanProjection[] {
  const item = record(value);
  if (!item) return [];
  const id = text(item.id ?? item.spanId);
  if (!id) return [];
  const attributes = record(item.attributes);
  return [{
    id,
    parentSpanId: text(item.parentSpanId) || null,
    name: text(item.name) || id,
    kind: text(item.kind) || 'Internal',
    status: text(item.status) || 'Unavailable',
    startedAt: text(item.startedAt),
    durationMs: number(item.durationMs),
    attributes: Object.freeze(Object.fromEntries(Object.entries(attributes ?? {})
      .flatMap(([key, member]) => typeof member === 'string' ? [[key, member]] : []))),
    spanEvents: list(item.spanEvents).flatMap(readSpanEvent)
  }];
}

function readSpanEvent(value: unknown): AgentTraceSpanEventProjection[] {
  const item = record(value);
  const name = text(item?.name);
  if (!item || !name) return [];
  const attributes = record(item.attributes);
  return [{
    name,
    timestamp: text(item.timestamp),
    attributes: Object.freeze(Object.fromEntries(Object.entries(attributes ?? {})
      .flatMap(([key, member]) => typeof member === 'string' ? [[key, member]] : [])))
  }];
}

function readTraceEvent(value: unknown): AgentTraceEventProjection[] {
  const item = record(value);
  const id = text(item?.id);
  if (!item || !id) return [];
  return [{
    id,
    type: text(item.type) || 'AgentEvent',
    kind: text(item.kind),
    channel: text(item.channel),
    sequenceNumber: count(item.sequenceNumber),
    occurredAt: text(item.occurredAt),
    threadExecutionId: text(item.threadExecutionId),
    sessionId: text(item.sessionId),
    threadId: text(item.threadId),
    traceId: text(item.traceId),
    spanId: text(item.spanId),
    parentSpanId: text(item.parentSpanId),
    agentId: text(item.agentId),
    source: text(item.source),
    generation: count(item.generation),
    isError: item.isError === true,
    payloadState: text(item.payloadState),
    attributes: Object.fromEntries(Object.entries(record(item.attributes) ?? {}).flatMap(([key, value]) => {
      const textValue = text(value);
      return textValue ? [[key, textValue] as const] : [];
    }))
  }];
}

function readMetric(value: unknown): AgentMetricProjection[] {
  const item = record(value);
  const id = text(item?.id);
  if (!item || !id) return [];
  return [{
    id,
    label: text(item.label) || id,
    value: nullableNumber(item.value),
    unit: text(item.unit),
    available: item.available === true
  }];
}

function readDependency(value: unknown): ToolHarnessProjection['dependencies'] {
  const item = record(value);
  const name = text(item?.name);
  if (!item || !name) return [];
  return [{
    name,
    typeName: text(item.typeName),
    kind: text(item.kind) || 'Unknown',
    optional: item.optional === true,
    resolutionSource: text(item.resolutionSource) || 'unknown'
  }];
}

function readMiddleware(value: unknown): ToolHarnessProjection['middleware'] {
  const item = record(value);
  const name = text(item?.name);
  if (!item || !name) return [];
  return [{
    name,
    type: text(item.type),
    activation: text(item.activation) || 'unknown',
    ownership: text(item.ownership) || 'unknown',
    usesHarnessConfiguration: item.usesHarnessConfiguration === true,
    requiresCanonicalWorkspaceIdentity: item.requiresCanonicalWorkspaceIdentity === true,
    configurationType: text(item.configurationType) || null
  }];
}

function readGraphNode(value: unknown): ToolHarnessProjection['capabilityGraph']['nodes'] {
  const item = record(value);
  const id = text(item?.id);
  if (!item || !id) return [];
  return [{
    id,
    kind: text(item.kind) || 'Capability',
    label: text(item.label) || id,
    description: text(item.description)
  }];
}

function readGraphEdge(value: unknown): ToolHarnessProjection['capabilityGraph']['edges'] {
  const item = record(value);
  const source = text(item?.source);
  const target = text(item?.target);
  if (!item || !source || !target) return [];
  return [{
    source,
    target,
    relation: text(item.relation) || 'references'
  }];
}

function permission(value: unknown): PermissionProjection {
  const item = record(value);
  return {
    kind: text(item?.kind) || 'Unavailable',
    requiresPermission: item?.requiresPermission === true,
    authority: text(item?.authority) || null,
    policyId: text(item?.policyId) || null,
    risk: text(item?.risk) || null,
    reason: text(item?.reason) || null,
    source: text(item?.source) || null
  };
}

function record(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function list(value: unknown): readonly unknown[] {
  return Array.isArray(value) ? value : [];
}

function strings(value: unknown): readonly string[] {
  return list(value).flatMap(item => {
    const value = text(item);
    return value ? [value] : [];
  });
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function count(value: unknown): number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : 0;
}

function number(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? value : 0;
}

function nullableCount(value: unknown): number | null {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : null;
}

function nullableNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}
