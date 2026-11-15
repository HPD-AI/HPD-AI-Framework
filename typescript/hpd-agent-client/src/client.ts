import { AgentClient as EndpointClient } from './generated/services.js';
import type {
  HpdAgentInputEvent,
  HpdAgentResponseInput,
} from './generated/events.js';
import type {
  AIContentDataContent, AIContentTextContent, AIContentUriContent, AgentRunConfig, ContentDto,
  CreateSessionRequest, CreateThreadRequest, ForkThreadRequest,
  HPDAgentClientToolsClientToolInvokeOutcomeEvent, SearchSessionsRequest, SessionDto,
  ThreadDto, ThreadExecutionDto,
} from './generated/models.js';
import { isRecord } from './event-schema.js';
import { observeThread, waitForRetry, type ObserveOptions } from './observation.js';
import { HpdApiError, HpdStreamError, type ClientOptions, type ClientToolRegistry } from './runtime.js';

export interface AgentClientOptions extends ClientOptions {
  baseUrl: string;
}

export class AgentClient {
  readonly api: EndpointClient;
  readonly sessions: Sessions;

  constructor({ baseUrl, ...options }: AgentClientOptions) {
    this.api = new EndpointClient(baseUrl, options);
    this.sessions = new Sessions(this.api);
  }
}

export class Sessions {
  constructor(private readonly api: EndpointClient) {}

  async create(input: CreateSessionRequest = {}) {
    const dto = await this.api.sessions.createSession({ input });
    return {
      session: new Session(this.api, dto),
      thread: new Thread(this.api, dto.id, 'main', input.agentId ?? undefined),
    };
  }

  async get(sessionId: string): Promise<Session> {
    return new Session(this.api, await this.api.sessions.getSession({ sessionId }));
  }

  async search(input: Partial<SearchSessionsRequest> = {}): Promise<Session[]> {
    const sessions = await this.api.sessions.searchSessions({ input: { metadata: null, ...input } });
    return sessions.map(dto => new Session(this.api, dto));
  }
}

export class Session {
  readonly threads: Threads;

  constructor(api: EndpointClient, readonly descriptor: SessionDto) {
    this.threads = new Threads(api, descriptor.id);
  }

  get id(): string { return this.descriptor.id; }
}

export class Threads {
  constructor(private readonly api: EndpointClient, private readonly sessionId: string) {}

  async list(): Promise<Thread[]> {
    const threads = await this.api.threads.listThreads({ sessionId: this.sessionId });
    return threads.map(dto => this.fromDescriptor(dto));
  }

  async get(threadId: string): Promise<Thread> {
    return this.fromDescriptor(await this.api.threads.getThread({ sessionId: this.sessionId, threadId }));
  }

  async create(agentId: string, input: Pick<CreateThreadRequest, 'threadId'> & Partial<CreateThreadRequest>): Promise<Thread> {
    return this.fromDescriptor(await this.api.threads.createThread({
      agentId,
      sessionId: this.sessionId,
      input: { name: null, description: null, tags: null, ...input },
    }));
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
  constructor(
    private readonly api: EndpointClient,
    readonly sessionId: string,
    readonly id: string,
    private readonly defaultAgentId?: string,
  ) {}

  get(): Promise<ThreadDto> {
    return this.api.threads.getThread({ sessionId: this.sessionId, threadId: this.id });
  }

  async state(options: { agentId?: string } = {}) {
    return this.api.agentRuntime.getAgentThreadRuntimeState(await this.scope(options.agentId));
  }

  async observe(options: ThreadObserveOptions = {}) {
    options.signal?.throwIfAborted();
    const scope = await this.scope(options.agentId);
    let cursor = options.after;
    if (cursor === undefined) {
      const state = await this.api.agentRuntime.getAgentThreadRuntimeState(scope);
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
          const state = await this.api.agentRuntime.getAgentThreadRuntimeState({ ...route.origin, agentId });
          if (!state.pendingRequests.some(({ request }) =>
            isRecord(request) && request.requestId === event.requestId)) return;
        }
        const result = await tools.registry.invoke({ ...event, arguments: { ...event.arguments } });
        const response = await this.api.agentInteractions.respondToAgentRequest({
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
        });
        if (!response.accepted) throw new HpdStreamError(response.message ?? 'Client tool response was not accepted');
      },
    });
  }

  async send(input: string | readonly MessageContent[], options: { agentId?: string; runConfig?: AgentRunConfig } = {}): Promise<SendResult> {
    const scope = await this.scope(options.agentId);
    try {
      const result = await this.api.agentRuntime.submitAgentInput({ ...scope, input: messageInput(input, options.runConfig) });
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

  async respond(input: HpdAgentResponseInput, options: { agentId?: string } = {}) {
    return this.api.agentInteractions.respondToAgentRequest({
      ...await this.scope(options.agentId),
      input: { ...input, version: '1.0' },
    });
  }

  upload(file: Blob, name?: string) {
    const input = new FormData();
    if (name === undefined) input.append('file', file);
    else input.append('file', file, name);
    return this.api.content.uploadContent({ sessionId: this.sessionId, threadId: this.id, input });
  }

  async fork(fromMessageId: string, options: Partial<Omit<ForkThreadRequest, 'fromMessageId'>> & { agentId?: string } = {}) {
    const { agentId, ...input } = options;
    const { target } = await this.api.threads.forkThread({
      ...await this.scope(agentId),
      input: { newThreadId: null, name: null, description: null, tags: null, ...input, fromMessageId },
    });
    return new Thread(this.api, target.sessionId, target.id, target.defaultAgentId);
  }

  private async scope(agentId?: string): Promise<RuntimeScope> {
    const selectedAgent = agentId ?? this.defaultAgentId ?? (await this.get()).defaultAgentId;
    return { agentId: selectedAgent, sessionId: this.sessionId, threadId: this.id };
  }
}

export class Execution {
  constructor(private readonly api: EndpointClient, private readonly scope: RuntimeScope, readonly id: string) {}

  get(): Promise<ThreadExecutionDto> {
    return this.api.executions.getThreadExecution({ ...this.scope, threadExecutionId: this.id });
  }

  async wait({ signal }: { signal?: AbortSignal } = {}): Promise<ThreadExecutionDto> {
    while (true) {
      signal?.throwIfAborted();
      try {
        const state = await this.get();
        signal?.throwIfAborted();
        if (state.finishedAt != null || state.status.toLowerCase() === 'interrupted') return state;
      } catch (error) {
        // Admission can precede the first committed execution event.
        if (!(error instanceof HpdApiError) || error.status !== 404) throw error;
      }
      await waitForRetry(500, signal);
    }
  }

  steer(input: string | readonly MessageContent[]) {
    return this.api.agentRuntime.submitAgentInput({
      ...this.scope,
      input: { ...messageInput(input), delivery: 'Steer', threadExecutionId: this.id },
    });
  }

  cancel() {
    return this.api.executions.cancelThreadExecution({ ...this.scope, threadExecutionId: this.id });
  }
}
