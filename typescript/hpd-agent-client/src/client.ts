import { AgentClient as EndpointClient } from './generated/services.js';
import type {
  HpdAgentInputEvent,
  HpdAgentResponseInput,
} from './generated/events.js';
import type {
  AIContentDataContent, AIContentTextContent, AIContentUriContent, AgentRunConfig, ContentDto,
  AgentSummaryDto, CreateAgentRequest, CreateSessionRequest, CreateThreadRequest, ForkThreadRequest,
  HPDAgentClientToolsClientToolInvokeOutcomeEvent, SearchSessionsRequest, SessionDto,
  StoredAgentDto, SubAgentDto, ThreadDto, ThreadExecutionDto, ThreadGraphDto,
  UpdateAgentRequest, UpdateSessionRequest, UpdateThreadRequest,
} from './generated/models.js';
import { isRecord } from './event-schema.js';
import { observeThread, waitForRetry, type ObserveOptions } from './observation.js';
import {
  HpdApiError,
  HpdStreamError,
  type ClientOptions,
  type ClientToolRegistry,
  type RequestOptions,
} from './runtime.js';
import { ThreadHistoryProjection, type ThreadHistory } from './projection.js';

export interface AgentClientOptions extends ClientOptions {
  baseUrl: string;
}

export class AgentClient {
  readonly api: EndpointClient;
  readonly agents: Agents;
  readonly evaluations: Evaluations;
  readonly sessions: Sessions;

  constructor({ baseUrl, ...options }: AgentClientOptions) {
    this.api = new EndpointClient(baseUrl, options);
    this.agents = new Agents(this.api);
    this.evaluations = new Evaluations(this.api);
    this.sessions = new Sessions(this.api);
  }
}

export class Agents {
  constructor(private readonly api: EndpointClient) {}

  list(options: RequestOptions = {}): Promise<AgentSummaryDto[]> {
    return this.api.agents.listAgents({}, options);
  }

  get(agentId: string, options: RequestOptions = {}): Promise<StoredAgentDto> {
    return this.api.agents.getAgent({ agentId }, options);
  }

  create(input: CreateAgentRequest, options: RequestOptions = {}): Promise<StoredAgentDto> {
    return this.api.agents.createAgent({ input }, options);
  }

  update(
    agentId: string,
    input: UpdateAgentRequest,
    options: RequestOptions = {},
  ): Promise<StoredAgentDto> {
    return this.api.agents.updateAgent({ agentId, input }, options);
  }

  async delete(agentId: string, options: RequestOptions = {}): Promise<void> {
    await this.api.agents.deleteAgent({ agentId }, options);
  }
}

export class Sessions {
  constructor(private readonly api: EndpointClient) {}

  async create(input: CreateSessionRequest = {}, options: RequestOptions = {}) {
    const dto = await this.api.sessions.createSession({ input }, options);
    return {
      session: new Session(this.api, dto),
      thread: new Thread(this.api, dto.id, 'main', input.agentId ?? undefined),
    };
  }

  async get(sessionId: string, options: RequestOptions = {}): Promise<Session> {
    return new Session(this.api, await this.api.sessions.getSession({ sessionId }, options));
  }

  async search(
    input: Partial<SearchSessionsRequest> = {},
    options: RequestOptions = {},
  ): Promise<Session[]> {
    const sessions = await this.api.sessions.searchSessions({
      input: { metadata: null, ...input },
    }, options);
    return sessions.map(dto => new Session(this.api, dto));
  }

  async update(
    sessionId: string,
    input: UpdateSessionRequest,
    options: RequestOptions = {},
  ): Promise<Session> {
    return new Session(
      this.api,
      await this.api.sessions.updateSession({ sessionId, input }, options));
  }

  async delete(sessionId: string, options: RequestOptions = {}): Promise<void> {
    await this.api.sessions.deleteSession({ sessionId }, options);
  }
}

export class Session {
  readonly threads: Threads;

  constructor(private readonly api: EndpointClient, readonly descriptor: SessionDto) {
    this.threads = new Threads(api, descriptor.id);
  }

  get id(): string { return this.descriptor.id; }

  async update(input: UpdateSessionRequest, options: RequestOptions = {}): Promise<Session> {
    return new Session(
      this.api,
      await this.api.sessions.updateSession({ sessionId: this.id, input }, options));
  }

  async delete(options: RequestOptions = {}): Promise<void> {
    await this.api.sessions.deleteSession({ sessionId: this.id }, options);
  }
}

export class Threads {
  constructor(private readonly api: EndpointClient, private readonly sessionId: string) {}

  async list(options: RequestOptions = {}): Promise<Thread[]> {
    const threads = await this.api.threads.listThreads({ sessionId: this.sessionId }, options);
    return threads.map(dto => this.fromDescriptor(dto));
  }

  async get(threadId: string, options: RequestOptions = {}): Promise<Thread> {
    return this.fromDescriptor(await this.api.threads.getThread(
      { sessionId: this.sessionId, threadId },
      options));
  }

  async create(
    agentId: string,
    input: Pick<CreateThreadRequest, 'threadId'> & Partial<CreateThreadRequest>,
    options: RequestOptions = {},
  ): Promise<Thread> {
    return this.fromDescriptor(await this.api.threads.createThread({
      agentId,
      sessionId: this.sessionId,
      input: { name: null, description: null, tags: null, ...input },
    }, options));
  }

  private fromDescriptor(dto: ThreadDto): Thread {
    return new Thread(this.api, dto.sessionId, dto.id, dto.defaultAgentId);
  }
}

export type MessageContent =
  | (Omit<AIContentTextContent, '$type'> & { $type: 'text' })
  | (Omit<AIContentDataContent, '$type'> & { $type: 'data' })
  | (Omit<AIContentUriContent, '$type'> & { $type: 'uri' })
  | StoredAttachment;

export interface StoredAttachment {
  $type: 'hpd:stored-content-reference';
  address: { scope: { value: string }; contentId: string; version: string };
  mediaType: string;
  name?: string;
}

export function storedAttachment(content: ContentDto, sessionId: string, name?: string): StoredAttachment {
  return {
    $type: 'hpd:stored-content-reference',
    address: { scope: { value: sessionId }, contentId: content.contentId, version: content.version },
    mediaType: content.contentType,
    name,
  };
}

export interface ThreadObserveOptions extends ObserveOptions {
  agentId?: string;
  clientTools?: {
    registry: ClientToolRegistry;
    responderId?: string;
    responderGroup?: string;
  };
}

export type SendResult = { status: 'queued'; execution: Execution } | { status: 'busy' };
export type ThreadRuntimeState = Awaited<
  ReturnType<EndpointClient['agentRuntime']['getAgentThreadRuntimeState']>
>;
export type ThreadContextUsage = Awaited<
  ReturnType<EndpointClient['agentRuntime']['estimateAgentThreadContextUsage']>
>;
export type AdmissionResult = Awaited<
  ReturnType<EndpointClient['agentRuntime']['submitAgentInput']>
>;
export type RespondResult = Awaited<
  ReturnType<EndpointClient['agentInteractions']['respondToAgentRequest']>
>;
export type ExecutionCancellationResult = Awaited<
  ReturnType<EndpointClient['executions']['cancelThreadExecution']>
>;
export type StartQueuedWorkResult = Awaited<
  ReturnType<EndpointClient['executions']['startQueuedThreadWork']>
>;

export type ScopeOptions = RequestOptions & { agentId?: string };
export type SendOptions = ScopeOptions & { runConfig?: AgentRunConfig };
export type ThreadDeleteOptions = RequestOptions & { recursive?: boolean };
export type ThreadHistoryOptions = ScopeOptions;
export type ForkOptions =
  Partial<Omit<ForkThreadRequest, 'fromMessageId'>>
  & ScopeOptions;

interface RuntimeScope {
  agentId: string;
  sessionId: string;
  threadId: string;
}

function messageInput(input: string | readonly MessageContent[], runConfig?: AgentRunConfig):
  Extract<HpdAgentInputEvent, { type: 'USER_MESSAGES_INPUT' }> & { version: '1.0' } {
  return {
    version: '1.0',
    type: 'USER_MESSAGES_INPUT',
    runConfig,
    messages: [{ role: 'user' as const, contents: typeof input === 'string' ? [{ $type: 'text', text: input }] : [...input] }],
  };
}

export class Thread {
  readonly content: ThreadContent;
  readonly executions: ThreadExecutions;

  constructor(
    private readonly api: EndpointClient,
    readonly sessionId: string,
    readonly id: string,
    private readonly defaultAgentId?: string,
  ) {
    this.content = new ThreadContent(api, sessionId, id);
    this.executions = new ThreadExecutions(
      api,
      options => this.scope(options.agentId, options));
  }

  get(options: RequestOptions = {}): Promise<ThreadDto> {
    return this.api.threads.getThread(
      { sessionId: this.sessionId, threadId: this.id },
      options);
  }

  async state(options: ScopeOptions = {}): Promise<ThreadRuntimeState> {
    return this.api.agentRuntime.getAgentThreadRuntimeState(
      await this.scope(options.agentId, options),
      options);
  }

  async observe(options: ThreadObserveOptions = {}) {
    options.signal?.throwIfAborted();
    const scope = await this.scope(options.agentId, options);
    let cursor = options.after;
    if (cursor === undefined) {
      const state = await this.api.agentRuntime.getAgentThreadRuntimeState(scope, options);
      const generation = state.observedCursor.generation;
      if (generation == null || !Number.isSafeInteger(generation) || generation < 1) {
        throw new HpdStreamError('Runtime state did not provide a valid journal generation');
      }
      cursor = { generation, sequence: 0 };
    }
    const tools = options.clientTools;
    return observeThread(this.api.agentEvents, scope, cursor, tools === undefined ? options : {
      ...options,
      onEvent: async delivery => {
        await options.onEvent?.(delivery);
        const { event, route, phase } = delivery;
        if (event.type !== 'CLIENT_TOOL_INVOKE_REQUEST'
          || !tools.registry.canHandle(event.toolName)) return;
        const capabilities = tools.registry.capabilities;
        const target = event.target;
        if ((target?.responderId && target.responderId !== tools.responderId)
          || (target?.responderGroup && target.responderGroup !== tools.responderGroup)
          || target?.requiredCapabilities?.some(capability => !capabilities.includes(capability))) return;

        const agentId = event.metadata?.agentId
          ?? (route.origin.sessionId === scope.sessionId && route.origin.threadId === scope.threadId
            ? scope.agentId : undefined);
        if (!agentId) throw new HpdStreamError('Client tool request is missing its agent identity');
        if (phase === 'replay') {
          const state = await this.api.agentRuntime.getAgentThreadRuntimeState(
            { ...route.origin, agentId },
            options);
          if (!state.pendingRequests.some(({ request }) =>
            isRecord(request) && request.requestId === event.requestId)) return;
        }
        const result = await tools.registry.invoke({ ...event, arguments: { ...event.arguments } });
        const response = await this.api.agentInteractions.respondToAgentRequest(
          {
            ...route.origin,
            agentId,
            input: {
              ...result,
              type: 'CLIENT_TOOL_INVOKE_OUTCOME',
              version: '1.0',
              requestId: event.requestId,
              sourceName: event.sourceName,
              responderId: tools.responderId,
              responderGroup: tools.responderGroup,
              capabilities,
              augmentation: result.augmentation as
                HPDAgentClientToolsClientToolInvokeOutcomeEvent['augmentation'],
            },
          },
          options);
        if (!response.accepted) throw new HpdStreamError(response.message ?? 'Client tool response was not accepted');
      },
    });
  }

  async send(input: string | readonly MessageContent[], options: SendOptions = {}): Promise<SendResult> {
    const scope = await this.scope(options.agentId, options);
    try {
      const result = await this.api.agentRuntime.submitAgentInput(
        { ...scope, input: messageInput(input, options.runConfig) },
        options);
      if (result.disposition !== 'queued' || !result.threadExecutionId) {
        throw new HpdStreamError('Unexpected message admission result');
      }
      return { status: 'queued', execution: new Execution(this.api, scope, result.threadExecutionId) };
    } catch (error) {
      if (error instanceof HpdApiError && error.status === 409
        && typeof error.problem === 'object' && error.problem !== null
        && Object.hasOwn(error.problem, 'ThreadExecutionActive')) return { status: 'busy' };
      throw error;
    }
  }

  async respond(input: HpdAgentResponseInput, options: ScopeOptions = {}): Promise<RespondResult> {
    return this.api.agentInteractions.respondToAgentRequest(
      {
        ...await this.scope(options.agentId, options),
        input: { ...input, version: '1.0' },
      },
      options);
  }

  async contextUsage(options: ScopeOptions = {}): Promise<ThreadContextUsage> {
    return this.api.agentRuntime.estimateAgentThreadContextUsage(
      await this.scope(options.agentId, options),
      options);
  }

  graph(options: RequestOptions = {}): Promise<ThreadGraphDto> {
    return this.api.threads.getThreadGraph({ sessionId: this.sessionId }, options);
  }

  subagents(options: RequestOptions = {}): Promise<SubAgentDto[]> {
    return this.api.threads.listThreadSubAgents(
      { sessionId: this.sessionId, threadId: this.id },
      options);
  }

  async update(input: UpdateThreadRequest, options: RequestOptions = {}): Promise<Thread> {
    const descriptor = await this.api.threads.updateThread(
      { sessionId: this.sessionId, threadId: this.id, input },
      options);
    return new Thread(this.api, descriptor.sessionId, descriptor.id, descriptor.defaultAgentId);
  }

  async delete(options: ThreadDeleteOptions = {}): Promise<void> {
    await this.api.threads.deleteThread(
      {
        sessionId: this.sessionId,
        threadId: this.id,
        recursive: options.recursive,
      },
      options);
  }

  async history(options: ThreadHistoryOptions = {}): Promise<ThreadHistory> {
    const scope = await this.scope(options.agentId, options);
    const state = await this.api.agentRuntime.getAgentThreadRuntimeState(scope, options);
    const generation = state.observedCursor.generation;
    const sequence = state.observedCursor.sequenceNumber;
    if (generation == null || !Number.isSafeInteger(generation) || generation < 1
      || sequence == null || !Number.isSafeInteger(sequence) || sequence < 0) {
      throw new HpdStreamError('Runtime state did not provide a valid journal head');
    }
    const head = {
      generation,
      sequence,
    };
    if (head.sequence === 0) {
      return { messages: [], appliedCursor: Object.freeze({ ...head }) };
    }

    const projection = new ThreadHistoryProjection();
    let settle!: () => void;
    let fail!: (error: unknown) => void;
    const reachedHead = new Promise<void>((resolve, reject) => {
      settle = resolve;
      fail = reject;
    });
    const observation = await this.observe({
      ...options,
      agentId: scope.agentId,
      after: { generation: head.generation, sequence: 0 },
      onEvent: delivery => {
        projection.project(delivery.event);
        const cursor = delivery.cursor;
        if (cursor?.generation === head.generation
          && cursor.sequence >= head.sequence) {
          settle();
        }
      },
      onRebase: cursor => {
        fail(new HpdStreamError(
          `Journal generation changed from ${head.generation} to ${cursor.generation} while reading history`));
      },
    });
    try {
      await Promise.race([
        reachedHead,
        observation.done.then(() => {
          throw new HpdStreamError('Observation ended before the history head was reached');
        }),
      ]);
    } finally {
      await observation.close();
    }
    return {
      messages: projection.snapshot(),
      appliedCursor: Object.freeze({ ...head }),
    };
  }

  async fork(fromMessageId: string, options: ForkOptions = {}): Promise<Thread> {
    const { agentId, signal, timeoutMs, headers, ...input } = options;
    const requestOptions = { signal, timeoutMs, headers };
    const { target } = await this.api.threads.forkThread(
      {
        ...await this.scope(agentId, requestOptions),
        input: { newThreadId: null, name: null, description: null, tags: null, ...input, fromMessageId },
      },
      requestOptions);
    return new Thread(this.api, target.sessionId, target.id, target.defaultAgentId);
  }

  private async scope(agentId?: string, options: RequestOptions = {}): Promise<RuntimeScope> {
    const selectedAgent = agentId ?? this.defaultAgentId ?? (await this.get(options)).defaultAgentId;
    return { agentId: selectedAgent, sessionId: this.sessionId, threadId: this.id };
  }
}

export type ContentHandle = ContentDto & {
  toMessageContent(name?: string): StoredAttachment;
};

export class ThreadContent {
  constructor(
    private readonly api: EndpointClient,
    private readonly sessionId: string,
    private readonly threadId: string,
  ) {}

  async upload(file: Blob, name?: string, options: RequestOptions = {}): Promise<ContentHandle> {
    const input = new FormData();
    if (name === undefined) input.append('file', file);
    else input.append('file', file, name);
    return this.handle(await this.api.content.uploadContent(
      { sessionId: this.sessionId, threadId: this.threadId, input },
      options));
  }

  async list(options: RequestOptions = {}): Promise<ContentHandle[]> {
    const content = await this.api.content.listContent(
      { sessionId: this.sessionId, threadId: this.threadId },
      options);
    return content.map(item => this.handle(item));
  }

  download(contentId: string, options: RequestOptions = {}): Promise<ArrayBuffer> {
    return this.api.content.downloadContent(
      { sessionId: this.sessionId, threadId: this.threadId, contentId },
      options);
  }

  async delete(contentId: string, options: RequestOptions = {}): Promise<void> {
    await this.api.content.deleteContent(
      { sessionId: this.sessionId, threadId: this.threadId, contentId },
      options);
  }

  private handle(content: ContentDto): ContentHandle {
    return Object.assign(content, {
      toMessageContent: (name?: string) => storedAttachment(content, this.sessionId, name),
    });
  }
}

export class ThreadExecutions {
  constructor(
    private readonly api: EndpointClient,
    private readonly resolveScope: (options: ScopeOptions) => Promise<RuntimeScope>,
  ) {}

  async list(options: ScopeOptions = {}): Promise<ThreadExecutionDto[]> {
    return this.api.executions.listThreadExecutions(
      await this.resolveScope(options),
      options);
  }

  async startQueuedWork(options: ScopeOptions = {}): Promise<StartQueuedWorkResult> {
    return this.api.executions.startQueuedThreadWork(
      await this.resolveScope(options),
      options);
  }

  async get(threadExecutionId: string, options: ScopeOptions = {}): Promise<Execution> {
    return new Execution(
      this.api,
      await this.resolveScope(options),
      threadExecutionId);
  }
}

export class Execution {
  constructor(private readonly api: EndpointClient, private readonly scope: RuntimeScope, readonly id: string) {}

  get(options: RequestOptions = {}): Promise<ThreadExecutionDto> {
    return this.api.executions.getThreadExecution(
      { ...this.scope, threadExecutionId: this.id },
      options);
  }

  async wait(options: RequestOptions = {}): Promise<ThreadExecutionDto> {
    while (true) {
      options.signal?.throwIfAborted();
      try {
        const state = await this.get(options);
        options.signal?.throwIfAborted();
        if (state.finishedAt != null || state.status.toLowerCase() === 'interrupted') return state;
      } catch (error) {
        // Admission can precede the first committed execution event.
        if (!(error instanceof HpdApiError) || error.status !== 404) throw error;
      }
      await waitForRetry(500, options.signal);
    }
  }

  steer(input: string | readonly MessageContent[], options: RequestOptions = {}): Promise<AdmissionResult> {
    return this.api.agentRuntime.submitAgentInput(
      {
        ...this.scope,
        input: { ...messageInput(input), delivery: 'Steer', threadExecutionId: this.id },
      },
      options);
  }

  cancel(options: RequestOptions = {}): Promise<ExecutionCancellationResult> {
    return this.api.executions.cancelThreadExecution(
      { ...this.scope, threadExecutionId: this.id },
      options);
  }
}

export interface EvaluationRange {
  from?: string;
  to?: string;
}

export class Evaluations {
  constructor(private readonly api: EndpointClient) {}

  catalog(options: RequestOptions = {}) {
    return this.api.evaluations.getEvaluatorCatalog({}, options);
  }

  summary(range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getEvaluatorSummary(range, options);
  }

  scores(evaluatorName: string, range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getScores({ evaluatorName, ...range }, options);
  }

  runs(
    query: { executionName?: string; iterationName?: string; scenarioName?: string } = {},
    options: RequestOptions = {},
  ) {
    return this.api.evaluations.getEvaluationRuns(query, options);
  }

  passRate(evaluatorName: string, range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getPassRate({ evaluatorName, ...range }, options);
  }

  failureRate(evaluatorName: string, range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getFailureRate({ evaluatorName, ...range }, options);
  }

  trend(
    evaluatorName: string,
    range: Required<Pick<EvaluationRange, 'from' | 'to'>>,
    bucketSize?: string,
    options: RequestOptions = {},
  ) {
    return this.api.evaluations.getTrend({ evaluatorName, ...range, bucketSize }, options);
  }

  cost(range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getCost(range, options);
  }

  compareAgents(
    evaluatorName: string,
    agentNames: string,
    range: EvaluationRange = {},
    options: RequestOptions = {},
  ) {
    return this.api.evaluations.getAgentComparison({ evaluatorName, agentNames, ...range }, options);
  }

  compareThreads(
    evaluatorNames: string,
    sessionId: string,
    threadId1: string,
    threadId2: string,
    options: RequestOptions = {},
  ) {
    return this.api.evaluations.getThreadComparison(
      { evaluatorNames, sessionId, threadId1, threadId2 },
      options);
  }

  toolUsage(range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getToolUsage(range, options);
  }

  safety(range: EvaluationRange = {}, options: RequestOptions = {}) {
    return this.api.evaluations.getSafetyAnalytics(range, options);
  }
}
