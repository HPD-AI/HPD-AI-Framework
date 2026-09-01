import type {
  AgentOperationCapabilities,
  AgentOperationKind,
  ClientAppProviderDescriptor,
  ClientToolAugmentation,
  ClientToolBackgroundOperationState,
  ClientToolDefinition,
  ClientToolError,
  ClientToolOperationContract,
  ClientToolPolicy,
  ClientToolHarnessDefinition,
  ClientToolProviderContext,
  ClientToolProviderErrorMessage,
  ClientToolProviderHeartbeatMessage,
  ClientToolProviderHelloMessage,
  ClientToolProviderIdentity,
  ClientToolProviderInvokeOutcomeMessage,
  ClientToolProviderInvokeToolMessage,
  ClientToolProviderManifest,
  ClientToolProviderManifestMessage,
  ClientToolProviderReadiness,
  ClientToolProviderReleaseMessage,
  ClientToolProviderRoutes,
  ClientToolProviderToServerMessage,
  ClientToolProviderWelcomeMessage,
  ServerToClientToolProviderMessage,
  ToolResultContent,
} from '@hpd-research/hpd-agent-client';
export { hpdClientToolProviderRoutes } from '@hpd-research/hpd-agent-client';
import { hpdClientToolProviderRoutes as defaultProviderRoutes } from '@hpd-research/hpd-agent-client';

export type ProviderConnectionStatus =
  | 'idle'
  | 'resolving_endpoint'
  | 'connecting'
  | 'registering'
  | 'connected'
  | 'ready'
  | 'disconnected'
  | 'backing_off'
  | 'closed'
  | 'revoked'
  | 'unsupported';

export interface ClientToolProviderEndpoint {
  url: string;
  protocols?: string[];
  expiresAt?: string;
}

export type ClientToolProviderEndpointResolutionReason =
  | 'initial'
  | 'reconnect'
  | 'authority_refresh';

export interface ClientToolProviderConnectionOptions {
  resolveEndpoint(
    reason: ClientToolProviderEndpointResolutionReason,
  ): Promise<ClientToolProviderEndpoint>;
  retry?: {
    maxAttempts?: number;
    initialDelayMs?: number;
    maxDelayMs?: number;
    jitterRatio?: number;
  };
}

export interface ProviderConnectionStateChange {
  previous: ProviderConnectionStatus;
  current: ProviderConnectionStatus;
  attempt: number;
  reason?: string;
}

export interface ProviderBackgroundOperationAbandoned {
  clientOperationId: string;
  bindingId: string;
  reason: 'provider_disconnected' | 'provider_revoked' | 'provider_closed';
}

export type ProviderWebSocketFactory = (
  url: string,
  protocols?: string[],
) => WebSocket;

export interface ProviderContextSnapshot {
  workspaceId?: string;
  documentId?: string;
  fileId?: string;
  pageId?: string;
  sceneId?: string;
  appStateVersion?: string;
  metadata?: Record<string, unknown>;
}

export interface ClientToolProviderOptions {
  url?: string;
  baseUrl?: string;
  connection?: ClientToolProviderConnectionOptions;
  routes?: Partial<ClientToolProviderRoutes>;
  identity: ClientToolProviderIdentity;
  appProvider: ClientAppProviderDescriptor;
  context?: ClientToolProviderContext | (() => ClientToolProviderContext | Promise<ClientToolProviderContext>);
  readiness?: ClientToolProviderReadiness | (() => ClientToolProviderReadiness | Promise<ClientToolProviderReadiness>);
  metadata?: Record<string, unknown> | (() =>
    Record<string, unknown> | Promise<Record<string, unknown>>);
  concurrency?: {
    maxQueueDepth?: number;
    invocationTimeoutMs?: number;
  };
  webSocketFactory?: ProviderWebSocketFactory;
  contextSnapshot?: () => ProviderContextSnapshot | Promise<ProviderContextSnapshot>;
  subscribeContextChanges?: (onContextChanged: () => void) => (() => void);
  contextUpdateDebounceMs?: number;
  onConnectionStateChange?: (change: ProviderConnectionStateChange) => void;
  onBackgroundOperationAbandoned?: (
    operation: ProviderBackgroundOperationAbandoned,
  ) => void;
}

export interface ClientToolProviderHarnessOptions {
  description?: string;
  startCollapsed?: boolean;
  functionResult?: string;
  systemPrompt?: string;
}

export interface ClientToolProviderToolOptions {
  description: string;
  parametersSchema: Record<string, unknown>;
  policy?: ClientToolPolicy;
  metadata?: Record<string, unknown>;
  handler: ClientToolProviderToolHandler;
}

type DefaultDiscriminator<TRequest> =
  'action' extends keyof TRequest ? 'action' : Extract<keyof TRequest, string>;

type DiscriminatorAction<
  TRequest,
  TDiscriminator extends keyof TRequest,
> = Extract<TRequest[TDiscriminator], string>;

export type ClientToolProviderActionHandlers<
  TRequest,
  TDiscriminator extends keyof TRequest,
  TResult extends ClientToolProviderToolResult = ClientToolProviderToolResult,
> = {
  [TAction in DiscriminatorAction<TRequest, TDiscriminator>]:
    ClientToolProviderOperationToolHandler<
      Extract<TRequest, Record<TDiscriminator, TAction>>,
      TResult
    >;
};

export interface ClientToolProviderOperationToolOptions<
  TRequest,
  TDiscriminator extends keyof TRequest & string = DefaultDiscriminator<TRequest>,
  TResult extends ClientToolProviderToolResult = ClientToolProviderToolResult,
> {
  description: string;
  discriminator: TDiscriminator;
  parametersSchema: Record<string, unknown>;
  defaultPolicy?: ClientToolPolicy;
  actions: Record<DiscriminatorAction<TRequest, TDiscriminator>, ClientToolPolicy>;
  metadata?: Record<string, unknown>;
  parse: (value: unknown) => TRequest;
  handler?: ClientToolProviderOperationToolHandler<TRequest, TResult>;
  handlers?: ClientToolProviderActionHandlers<TRequest, TDiscriminator, TResult>;
}

export interface ClientToolProviderToolContext {
  invocation: ClientToolProviderInvokeToolMessage;
  requestedInvocationMode?: string;
  resolvedInvocationMode: string;
  expectedContext?: ProviderContextSnapshot;
  currentContext?: ProviderContextSnapshot;
  policy: ClientToolPolicy;
  complete: (content?: ClientToolProviderToolResult, augmentation?: ClientToolAugmentation) => void;
  reject: (error: ClientToolError) => void;
  fail: (error: ClientToolError) => void;
  acceptBackground: (
    options?: {
      content?: ClientToolProviderToolResult;
      operationKind?: AgentOperationKind;
      operationCapabilities?: AgentOperationCapabilities;
      augmentation?: ClientToolAugmentation;
    },
  ) => void;
}

export type ClientToolProviderToolResult =
  | void
  | string
  | ToolResultContent
  | ToolResultContent[]
  | { content?: ToolResultContent[] | string; augmentation?: ClientToolAugmentation };

export type ClientToolProviderToolHandler = (
  args: Record<string, unknown>,
  context: ClientToolProviderToolContext,
) => ClientToolProviderToolResult | Promise<ClientToolProviderToolResult>;

export type ClientToolProviderOperationToolHandler<TRequest, TResult = ClientToolProviderToolResult> = (
  request: TRequest,
  context: ClientToolProviderToolContext & {
    request: TRequest;
    action: string;
  },
) => TResult | Promise<TResult>;

interface RegisteredTool {
  definition: ClientToolDefinition;
  handler: ClientToolProviderToolHandler;
  parse?: (value: unknown) => unknown;
  handlers?: Record<string, ClientToolProviderOperationToolHandler<unknown>>;
}

interface RegisteredHarness {
  definition: Omit<ClientToolHarnessDefinition, 'tools'>;
  tools: Map<string, RegisteredTool>;
}

interface QueuedInvocation {
  message: ClientToolProviderInvokeToolMessage;
  resolve: () => void;
}

export interface ClientToolProviderHarnessBuilder {
  tool(name: string, options: ClientToolProviderToolOptions): ClientToolProviderHarnessBuilder;
  operationTool<
    TRequest,
    TDiscriminator extends keyof TRequest & string = DefaultDiscriminator<TRequest>,
    TResult extends ClientToolProviderToolResult = ClientToolProviderToolResult,
  >(
    name: string,
    options: ClientToolProviderOperationToolOptions<TRequest, TDiscriminator, TResult>,
  ): ClientToolProviderHarnessBuilder;
}

export function createClientToolProvider(options: ClientToolProviderOptions): ClientToolProvider {
  return new ClientToolProvider(options);
}

export class ClientToolProvider {
  private readonly harnesses = new Map<string, RegisteredHarness>();
  private readonly maxQueueDepth: number;
  private readonly invocationTimeoutMs: number;
  private readonly webSocketFactory?: ProviderWebSocketFactory;
  private readonly routes: ClientToolProviderRoutes;
  private readonly explicitUrl?: string;
  private readonly connectionOptions?: ClientToolProviderConnectionOptions;
  private socket?: WebSocket;
  private heartbeatTimer?: ReturnType<typeof setInterval>;
  private contextUpdateTimer?: ReturnType<typeof setTimeout>;
  private unsubscribeContextChanges?: () => void;
  private publishedContextFingerprint?: string;
  private clientRuntimeId?: string;
  private connectionId?: string;
  private statusValue: ProviderConnectionStatus = 'idle';
  private reconnectAttempt = 0;
  private connectionGeneration = 0;
  private shutdownRequested = false;
  private reconnectTask?: Promise<void>;
  private activeInvocation = false;
  private readonly queue: QueuedInvocation[] = [];
  private readonly backgroundOperations = new Map<string, {
    bindingId: string;
    policy: ClientToolPolicy;
  }>();

  public constructor(private readonly options: ClientToolProviderOptions) {
    this.maxQueueDepth = Math.max(0, options.concurrency?.maxQueueDepth ?? 1);
    this.invocationTimeoutMs = Math.max(1, options.concurrency?.invocationTimeoutMs ?? 60_000);
    this.webSocketFactory = options.webSocketFactory;
    this.routes = defaultProviderRoutes(options.routes);
    this.explicitUrl = options.url;
    this.connectionOptions = options.connection;
  }

  public get status(): ProviderConnectionStatus {
    return this.statusValue;
  }

  public get runtimeIds(): { clientRuntimeId?: string; connectionId?: string } {
    return {
      clientRuntimeId: this.clientRuntimeId,
      connectionId: this.connectionId,
    };
  }

  public harness(name: string, options: ClientToolProviderHarnessOptions = {}): ClientToolProviderHarnessBuilder {
    const existing = this.harnesses.get(name);
    if (existing !== undefined) {
      return new HarnessBuilder(existing);
    }

    const harness: RegisteredHarness = {
      definition: {
        name,
        description: options.description,
        functionResult: options.functionResult,
        systemPrompt: options.systemPrompt,
        startCollapsed: options.startCollapsed,
      },
      tools: new Map<string, RegisteredTool>(),
    };
    this.harnesses.set(name, harness);
    return new HarnessBuilder(harness);
  }

  public async connect(): Promise<void> {
    if (this.statusValue === 'ready') {
      return;
    }
    if (this.reconnectTask !== undefined) {
      return this.reconnectTask;
    }
    if (this.statusValue === 'closed' ||
        this.statusValue === 'revoked' ||
        this.statusValue === 'unsupported') {
      throw new Error(`Client tool provider is in terminal state '${this.statusValue}'.`);
    }

    this.shutdownRequested = false;
    this.reconnectTask = this.connectWithRetry('initial')
      .finally(() => {
        this.reconnectTask = undefined;
      });
    return this.reconnectTask;
  }

  public async disconnect(reason = 'Provider disconnected.'): Promise<void> {
    this.shutdownRequested = true;
    this.sendRelease(reason);
    this.abandonBackgroundOperations('provider_closed');
    this.cleanupConnection();
    this.setStatus('closed', reason);
    const socket = this.socket;
    this.socket = undefined;
    socket?.close();
  }

  public async updateManifest(): Promise<void> {
    const message = await this.createManifestMessage();
    this.publishedContextFingerprint = fingerprint(message.context);
    this.send(message);
  }

  public async setReadiness(readiness: ClientToolProviderReadiness): Promise<void> {
    this.options.readiness = readiness;
    await this.updateManifest();
  }

  public finishBackgroundOperation(
    clientOperationId: string,
    state: ClientToolBackgroundOperationState,
    options: {
      content?: ClientToolProviderToolResult;
      augmentation?: ClientToolAugmentation;
      error?: ClientToolError | null;
      cancellationReason?: string | null;
      metadata?: Record<string, string> | null;
    } = {},
  ): void {
    const operation = this.backgroundOperations.get(clientOperationId);
    if (operation === undefined) {
      throw new Error(`Background operation '${clientOperationId}' is not active.`);
    }

    this.backgroundOperations.delete(clientOperationId);
    this.send({
      type: 'provider.backgroundOperationOutcome',
      bindingId: operation.bindingId,
      clientOperationId,
      state,
      content: normalizeContent(options.content),
      augmentation: options.augmentation,
      error: options.error,
      cancellationReason: options.cancellationReason,
      metadata: options.metadata,
    });
  }

  public completeBackgroundOperation(
    clientOperationId: string,
    content?: ClientToolProviderToolResult,
    options: {
      augmentation?: ClientToolAugmentation;
      metadata?: Record<string, string> | null;
    } = {},
  ): void {
    this.finishBackgroundOperation(clientOperationId, 'Completed', {
      content,
      augmentation: options.augmentation,
      metadata: options.metadata,
    });
  }

  public failBackgroundOperation(
    clientOperationId: string,
    error: ClientToolError,
    options: {
      metadata?: Record<string, string> | null;
    } = {},
  ): void {
    this.finishBackgroundOperation(clientOperationId, 'Faulted', {
      error,
      metadata: options.metadata,
    });
  }

  public cancelBackgroundOperation(
    clientOperationId: string,
    cancellationReason?: string | null,
    metadata?: Record<string, string> | null,
  ): void {
    this.finishBackgroundOperation(clientOperationId, 'Cancelled', {
      cancellationReason,
      metadata,
    });
  }

  private async connectWithRetry(
    initialReason: ClientToolProviderEndpointResolutionReason,
  ): Promise<void> {
    let reason = initialReason;
    while (!this.shutdownRequested) {
      try {
        await this.connectOnce(reason);
        this.reconnectAttempt = 0;
        return;
      } catch (error) {
        if (this.isTerminal()) {
          throw error;
        }

        this.reconnectAttempt += 1;
        const maximumAttempts =
          this.connectionOptions?.retry?.maxAttempts ?? Number.POSITIVE_INFINITY;
        if (this.reconnectAttempt >= maximumAttempts) {
          this.setStatus('closed', errorMessage(error));
          throw error;
        }

        this.setStatus('backing_off', errorMessage(error));
        await delay(this.retryDelayMs(this.reconnectAttempt));
        reason = this.connectionOptions === undefined
          ? 'reconnect'
          : 'authority_refresh';
      }
    }

    throw new Error('Client tool provider connection was closed.');
  }

  private async connectOnce(
    reason: ClientToolProviderEndpointResolutionReason,
  ): Promise<void> {
    const generation = ++this.connectionGeneration;
    this.setStatus('resolving_endpoint');
    const endpoint = this.connectionOptions === undefined
      ? this.resolveFixedEndpoint()
      : await this.resolveEndpoint(reason);
    if (this.shutdownRequested || generation !== this.connectionGeneration) {
      throw new Error('Client tool provider connection attempt was superseded.');
    }

    this.setStatus('connecting');
    const socket = this.createSocket(endpoint);
    this.socket = socket;

    await new Promise<void>((resolve, reject) => {
      let ready = false;
      let settled = false;
      const rejectOnce = (error: Error) => {
        if (!settled) {
          settled = true;
          reject(error);
        }
      };

      socket.onopen = () => {
        if (generation !== this.connectionGeneration || this.shutdownRequested) {
          socket.close();
          rejectOnce(new Error('Client tool provider connection attempt was superseded.'));
          return;
        }

        this.setStatus('registering');
        this.send({
          type: 'provider.hello',
          protocolVersion: '2',
          identity: this.options.identity,
        } satisfies ClientToolProviderHelloMessage);
      };
      socket.onmessage = event => {
        void this.handleMessage(String(event.data))
          .then(() => {
            if (!ready && this.statusValue === 'ready') {
              ready = true;
              settled = true;
              resolve();
            }
          })
          .catch(error => {
            rejectOnce(error instanceof Error ? error : new Error(String(error)));
            socket.close();
          });
      };
      socket.onerror = () => {
        rejectOnce(new Error('Client tool provider websocket failed to connect.'));
      };
      socket.onclose = () => {
        if (generation !== this.connectionGeneration) {
          return;
        }

        this.handleSocketClosed(ready);
        if (!ready) {
          rejectOnce(new Error(
            'Client tool provider websocket closed before registration completed.',
          ));
        }
      };
    });
  }

  private createSocket(endpoint: ClientToolProviderEndpoint): WebSocket {
    const url = this.toWebSocketUrl(endpoint.url);
    if (this.webSocketFactory !== undefined) {
      return this.webSocketFactory(url, endpoint.protocols);
    }

    if (typeof WebSocket === 'undefined') {
      throw new Error('No WebSocket implementation is available. Pass webSocketFactory in this runtime.');
    }

    return endpoint.protocols === undefined
      ? new WebSocket(url)
      : new WebSocket(url, endpoint.protocols);
  }

  private async resolveEndpoint(
    reason: ClientToolProviderEndpointResolutionReason,
  ): Promise<ClientToolProviderEndpoint> {
    const endpoint = await this.connectionOptions!.resolveEndpoint(reason);
    if (endpoint.url.trim().length === 0) {
      throw new Error('Client tool provider endpoint resolver returned an empty URL.');
    }
    if (endpoint.expiresAt !== undefined &&
        Date.parse(endpoint.expiresAt) <= Date.now()) {
      throw new Error('Client tool provider endpoint resolver returned expired authority.');
    }
    return endpoint;
  }

  private resolveFixedEndpoint(): ClientToolProviderEndpoint {
    if (this.explicitUrl !== undefined) {
      return { url: this.explicitUrl };
    }

    const baseUrl = this.options.baseUrl ?? '';
    return { url: joinUrl(baseUrl, this.routes.connect) };
  }

  private retryDelayMs(attempt: number): number {
    const retry = this.connectionOptions?.retry;
    const initial = Math.max(1, retry?.initialDelayMs ?? 250);
    const maximum = Math.max(initial, retry?.maxDelayMs ?? 10_000);
    const jitterRatio = Math.min(1, Math.max(0, retry?.jitterRatio ?? 0.2));
    const unjittered = Math.min(maximum, initial * (2 ** Math.max(0, attempt - 1)));
    const jitter = unjittered * jitterRatio;
    return Math.max(0, Math.round(unjittered - jitter + Math.random() * jitter * 2));
  }

  private toWebSocketUrl(url: string): string {
    if (url.startsWith('ws://') || url.startsWith('wss://')) {
      return url;
    }

    if (url.startsWith('http://')) {
      return `ws://${url.slice('http://'.length)}`;
    }

    if (url.startsWith('https://')) {
      return `wss://${url.slice('https://'.length)}`;
    }

    if (typeof window !== 'undefined' && window.location !== undefined) {
      const base = new URL(url, window.location.href);
      base.protocol = base.protocol === 'https:' ? 'wss:' : 'ws:';
      return base.toString();
    }

    return url;
  }

  private async handleMessage(text: string): Promise<void> {
    const message = JSON.parse(text) as ServerToClientToolProviderMessage;
    switch (message.type) {
      case 'provider.welcome':
        await this.handleWelcome(message);
        return;

      case 'provider.invoke':
        this.enqueueInvocation(message);
        return;

      case 'provider.error':
        this.handleError(message);
        return;

      default:
        throw new Error(`Unsupported provider message type '${(message as { type?: string }).type ?? '<missing>'}'.`);
    }
  }

  private async handleWelcome(message: ClientToolProviderWelcomeMessage): Promise<void> {
    this.clientRuntimeId = message.clientRuntimeId;
    this.connectionId = message.connectionId;
    this.setStatus('connected');
    this.startHeartbeat(message.heartbeatIntervalMs);
    await this.updateManifest();
    this.setStatus('ready');
    this.startContextChangeSubscription();
  }

  private handleError(message: ClientToolProviderErrorMessage): void {
    if (message.code === 'authority_revoked' ||
        message.code === 'launch_revoked' ||
        message.code === 'provider_revoked') {
      this.shutdownRequested = true;
      this.abandonBackgroundOperations('provider_revoked');
      this.setStatus('revoked', message.message);
    } else if (message.code === 'unsupported_protocol') {
      this.shutdownRequested = true;
      this.abandonBackgroundOperations('provider_closed');
      this.setStatus('unsupported', message.message);
    }
    throw new Error(`HPD provider error ${message.code}: ${message.message}`);
  }

  private enqueueInvocation(message: ClientToolProviderInvokeToolMessage): void {
    if (this.queue.length >= this.maxQueueDepth && this.activeInvocation) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: toolError('provider_not_ready', 'Provider invocation queue is full.', true),
      });
      return;
    }

    this.queue.push({
      message,
      resolve: () => undefined,
    });
    this.drainQueue();
  }

  private drainQueue(): void {
    if (this.activeInvocation) {
      return;
    }

    const next = this.queue.shift();
    if (next === undefined) {
      return;
    }

    this.activeInvocation = true;
    void this.runInvocation(next.message)
      .catch(() => undefined)
      .finally(() => {
        this.activeInvocation = false;
        next.resolve();
        this.drainQueue();
      });
  }

  private async runInvocation(message: ClientToolProviderInvokeToolMessage): Promise<void> {
    const harness = [...this.harnesses.values()]
      .find(candidate => candidate.tools.has(message.toolName));
    const tool = harness?.tools.get(message.toolName);
    if (tool === undefined) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: toolError('unsupported_operation', `Provider tool '${message.toolName}' is not registered.`),
      });
      return;
    }

    let responded = false;
    const currentContext = await this.options.contextSnapshot?.();
    let operation: ReturnType<typeof resolveLocalOperation>;
    try {
      operation = resolveLocalOperation(tool.definition, message.arguments);
    } catch (error) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: toolError('unknown_action', errorMessage(error)),
      });
      return;
    }
    if ((message.operation === undefined) !== (operation === undefined) ||
        (message.operation !== undefined &&
          !resolvedOperationsEqual(message.operation, operation))) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: toolError('unsupported_operation', 'Server and provider operation policies do not match.'),
      });
      return;
    }

    const policy = operation?.policy ?? resolvePolicy(tool.definition.defaultPolicy);
    let requestedInvocationMode: 'Synchronous' | 'Background' | undefined;
    let resolvedInvocationMode: 'Synchronous' | 'Background';
    try {
      requestedInvocationMode =
        message.requestedInvocationMode === undefined
          ? undefined
          : normalizeResolvedInvocationMode(message.requestedInvocationMode);
      resolvedInvocationMode =
        normalizeResolvedInvocationMode(message.resolvedInvocationMode);
      validateInvocationMode(policy, resolvedInvocationMode);
    } catch (error) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: toolError('unsupported_operation', errorMessage(error)),
      });
      return;
    }

    if (policy.requiresFreshContext === true &&
        !contextsMatch(message.expectedContext, currentContext)) {
      this.sendOutcome(message, {
        outcome: 'Rejected',
        error: {
          kind: 'stale_context',
          message: 'Provider context changed before the operation could execute.',
          retryable: true,
          currentContext,
        },
      });
      return;
    }

    const reply = (outcome: Omit<ClientToolProviderInvokeOutcomeMessage, 'type' | 'bindingId' | 'invocationId' | 'requestId'>) => {
      if (responded) {
        throw new Error(`Provider tool '${message.toolName}' attempted to send multiple immediate outcomes.`);
      }

      responded = true;
      this.sendOutcome(message, outcome);
    };

    const context: ClientToolProviderToolContext = {
      invocation: message,
      requestedInvocationMode,
      resolvedInvocationMode,
      expectedContext: message.expectedContext,
      currentContext,
      policy,
      complete: (content, augmentation) => reply({
        outcome: 'Completed',
        content: normalizeContent(content),
        augmentation,
      }),
      reject: error => reply({
        outcome: 'Rejected',
        error,
      }),
      fail: error => reply({
        outcome: 'Failed',
        error,
      }),
      acceptBackground: options => {
        if (resolvedInvocationMode !== 'Background' ||
            message.clientOperationId === undefined) {
          throw new Error(
            `Provider tool '${message.toolName}' cannot accept background work without an HPD-assigned operation id.`,
          );
        }
        reply({
          outcome: 'AcceptedBackground',
          clientOperationId: message.clientOperationId,
          content: normalizeContent(options?.content),
          operationKind: options?.operationKind,
          operationCapabilities: options?.operationCapabilities,
          augmentation: options?.augmentation,
        });
      },
    };

    try {
      let request: unknown = message.arguments;
      if (tool.parse !== undefined) {
        try {
          request = tool.parse(message.arguments);
        } catch (error) {
          reply({
            outcome: 'Rejected',
            error: toolError('invalid_arguments', errorMessage(error)),
          });
          return;
        }
      }

      const action = operation?.action;
      const handler = action === undefined
        ? tool.handler
        : tool.handlers?.[action] ?? tool.handler;
      const result = await withTimeout(
        handler(request as Record<string, unknown>, {
          ...context,
          request,
          action: action ?? '',
        }),
        this.invocationTimeoutMs,
        `Provider tool '${message.toolName}' timed out.`,
      );

      if (!responded) {
        const normalized = normalizeResult(result);
        reply({
          outcome: 'Completed',
          content: normalized.content,
          augmentation: normalized.augmentation,
        });
      }
    } catch (error) {
      if (!responded) {
        reply({
          outcome: 'Failed',
          error: toolError('provider_failure', errorMessage(error)),
        });
      }
    }
  }

  private sendOutcome(
    invocation: ClientToolProviderInvokeToolMessage,
    outcome: Omit<ClientToolProviderInvokeOutcomeMessage, 'type' | 'bindingId' | 'invocationId' | 'requestId'>,
  ): void {
    this.send({
      type: 'provider.invokeOutcome',
      bindingId: invocation.bindingId,
      invocationId: invocation.invocationId,
      requestId: invocation.requestId,
      ...outcome,
    });

    if (outcome.outcome === 'AcceptedBackground' && outcome.clientOperationId !== undefined) {
      this.backgroundOperations.set(outcome.clientOperationId, {
        bindingId: invocation.bindingId,
        policy: invocation.operation?.policy ?? {},
      });
    }
  }

  private async createManifestMessage(): Promise<ClientToolProviderManifestMessage> {
    const manifest = await this.createManifest();
    return {
      type: 'provider.manifest',
      protocolVersion: manifest.protocolVersion,
      appProvider: manifest.appProvider,
      context: manifest.context,
      readiness: manifest.readiness,
      clientToolHarnesses: manifest.clientToolHarnesses,
      metadata: manifest.metadata,
    };
  }

  private async createManifest(): Promise<ClientToolProviderManifest> {
    return {
      protocolVersion: '2',
      identity: this.options.identity,
      appProvider: this.options.appProvider,
      context: await resolveValue(this.options.context),
      readiness: await resolveValue(this.options.readiness) ?? 'Ready',
      clientToolHarnesses: [...this.harnesses.values()].map(harness => ({
        ...harness.definition,
        tools: [...harness.tools.values()].map(tool => tool.definition),
      })),
      metadata: await resolveValue(this.options.metadata),
    };
  }

  private startHeartbeat(intervalMs: number): void {
    if (this.heartbeatTimer !== undefined) {
      clearInterval(this.heartbeatTimer);
    }

    this.heartbeatTimer = setInterval(() => {
      this.send({
        type: 'provider.heartbeat',
      } satisfies ClientToolProviderHeartbeatMessage);
    }, Math.max(1_000, intervalMs));
  }

  private startContextChangeSubscription(): void {
    this.stopContextChangeSubscription();
    if (this.options.subscribeContextChanges === undefined) {
      return;
    }

    this.unsubscribeContextChanges = this.options.subscribeContextChanges(
      () => this.scheduleContextManifestUpdate(),
    );
    this.scheduleContextManifestUpdate();
  }

  private stopContextChangeSubscription(): void {
    if (this.contextUpdateTimer !== undefined) {
      clearTimeout(this.contextUpdateTimer);
      this.contextUpdateTimer = undefined;
    }
    this.unsubscribeContextChanges?.();
    this.unsubscribeContextChanges = undefined;
  }

  private scheduleContextManifestUpdate(): void {
    if (this.statusValue !== 'ready') {
      return;
    }
    if (this.contextUpdateTimer !== undefined) {
      clearTimeout(this.contextUpdateTimer);
    }

    this.contextUpdateTimer = setTimeout(() => {
      this.contextUpdateTimer = undefined;
      void this.publishManifestWhenContextChanged();
    }, Math.max(0, this.options.contextUpdateDebounceMs ?? 50));
  }

  private async publishManifestWhenContextChanged(): Promise<void> {
    if (this.statusValue !== 'ready') {
      return;
    }

    const context = await resolveValue(this.options.context);
    if (fingerprint(context) === this.publishedContextFingerprint) {
      return;
    }
    await this.updateManifest();
  }

  private sendRelease(reason: string): void {
    this.send({
      type: 'provider.release',
      reason,
    } satisfies ClientToolProviderReleaseMessage);
  }

  private send(message: ClientToolProviderToServerMessage): void {
    if (this.socket?.readyState !== 1) {
      if (message.type === 'provider.release') {
        return;
      }

      throw new Error('Client tool provider websocket is not open.');
    }

    this.socket.send(JSON.stringify(message));
  }

  private handleSocketClosed(wasReady: boolean): void {
    this.cleanupConnection();
    this.socket = undefined;
    if (this.isTerminal()) {
      return;
    }

    this.abandonBackgroundOperations('provider_disconnected');
    this.queue.length = 0;
    this.setStatus('disconnected', 'Provider websocket disconnected.');
    if (!wasReady || this.shutdownRequested || this.reconnectTask !== undefined) {
      return;
    }

    this.reconnectTask = this.connectWithRetry('reconnect')
      .catch(() => undefined)
      .finally(() => {
        this.reconnectTask = undefined;
      });
  }

  private cleanupConnection(): void {
    this.stopContextChangeSubscription();
    if (this.heartbeatTimer !== undefined) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = undefined;
    }
  }

  private abandonBackgroundOperations(
    reason: ProviderBackgroundOperationAbandoned['reason'],
  ): void {
    for (const [clientOperationId, operation] of this.backgroundOperations) {
      this.options.onBackgroundOperationAbandoned?.({
        clientOperationId,
        bindingId: operation.bindingId,
        reason,
      });
    }
    this.backgroundOperations.clear();
  }

  private isTerminal(): boolean {
    return this.shutdownRequested ||
      this.statusValue === 'closed' ||
      this.statusValue === 'revoked' ||
      this.statusValue === 'unsupported';
  }

  private setStatus(status: ProviderConnectionStatus, reason?: string): void {
    if (this.statusValue === status) {
      return;
    }

    const previous = this.statusValue;
    this.statusValue = status;
    this.options.onConnectionStateChange?.({
      previous,
      current: status,
      attempt: this.reconnectAttempt,
      reason,
    });
  }
}

function fingerprint(value: unknown): string {
  return JSON.stringify(canonicalize(value));
}

function delay(milliseconds: number): Promise<void> {
  return new Promise(resolve => {
    setTimeout(resolve, milliseconds);
  });
}

function canonicalize(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(canonicalize);
  }
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value as Record<string, unknown>)
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([key, entry]) => [key, canonicalize(entry)]),
    );
  }
  return value;
}

class HarnessBuilder implements ClientToolProviderHarnessBuilder {
  public constructor(private readonly harness: RegisteredHarness) {}

  public tool(name: string, options: ClientToolProviderToolOptions): ClientToolProviderHarnessBuilder {
    this.harness.tools.set(name, {
      definition: {
        name,
        description: options.description,
        parametersSchema: options.parametersSchema,
        defaultPolicy: options.policy,
        metadata: options.metadata,
      },
      handler: options.handler,
    });
    return this;
  }

  public operationTool<
    TRequest,
    TDiscriminator extends keyof TRequest & string = DefaultDiscriminator<TRequest>,
    TResult extends ClientToolProviderToolResult = ClientToolProviderToolResult,
  >(
    name: string,
    options: ClientToolProviderOperationToolOptions<TRequest, TDiscriminator, TResult>,
  ): ClientToolProviderHarnessBuilder {
    const operationContract: ClientToolOperationContract = {
      discriminator: options.discriminator,
      actions: options.actions,
    };
    validateCompoundDefinition(options.parametersSchema, operationContract, options.handlers);
    if (options.handler === undefined && options.handlers === undefined) {
      throw new Error(`Compound tool '${name}' requires either handler or handlers.`);
    }

    this.harness.tools.set(name, {
      definition: {
        name,
        description: options.description,
        parametersSchema: options.parametersSchema,
        defaultPolicy: options.defaultPolicy,
        operationContract,
        metadata: options.metadata,
      },
      parse: options.parse as (value: unknown) => unknown,
      handler: (options.handler ?? (() => {
        throw new Error(`No handler is registered for compound tool '${name}'.`);
      })) as unknown as ClientToolProviderToolHandler,
      handlers: options.handlers as Record<string, ClientToolProviderOperationToolHandler<unknown>> | undefined,
    });
    return this;
  }
}

const defaultPolicy: Required<Pick<ClientToolPolicy,
  'requiresPermission' | 'mutatesState' | 'requiresFreshContext' |
  'destructive' | 'idempotent' | 'invocationModePolicy' |
  'operationNotification'>> = {
  requiresPermission: false,
  mutatesState: false,
  requiresFreshContext: false,
  destructive: false,
  idempotent: false,
  invocationModePolicy: 'SynchronousOnly',
  operationNotification: {
    includeProgress: false,
    includeTerminal: true,
    minimumInterval: '00:00:00',
  },
};

function resolvePolicy(
  base?: ClientToolPolicy,
  override?: ClientToolPolicy,
): ClientToolPolicy {
  const policy = {
    ...defaultPolicy,
    ...base,
    ...override,
  };
  return {
    ...policy,
    invocationModePolicy: normalizeInvocationModePolicy(policy.invocationModePolicy),
  };
}

function resolveLocalOperation(
  definition: ClientToolDefinition,
  args: Record<string, unknown>,
): { discriminator: string; action: string; policy: ClientToolPolicy } | undefined {
  const contract = definition.operationContract;
  if (contract === undefined) {
    return undefined;
  }

  const value = args[contract.discriminator];
  if (typeof value !== 'string' || value.length === 0) {
    throw new Error(`Compound tool requires string discriminator '${contract.discriminator}'.`);
  }

  const actionPolicy = contract.actions[value];
  if (actionPolicy === undefined) {
    throw new Error(`Unknown compound tool action '${value}'.`);
  }

  return {
    discriminator: contract.discriminator,
    action: value,
    policy: resolvePolicy(definition.defaultPolicy, actionPolicy),
  };
}

function resolvedOperationsEqual(
  server: { discriminator: string; action: string; policy: ClientToolPolicy },
  local: { discriminator: string; action: string; policy: ClientToolPolicy } | undefined,
): boolean {
  return local !== undefined &&
    server.discriminator === local.discriminator &&
    server.action === local.action &&
    JSON.stringify(resolvePolicy(server.policy)) === JSON.stringify(resolvePolicy(local.policy));
}

function validateInvocationMode(policy: ClientToolPolicy, resolved: string): void {
  const mode = normalizeInvocationModePolicy(policy.invocationModePolicy);
  if (mode === 'SynchronousOnly' && resolved !== 'Synchronous') {
    throw new Error('This operation only supports synchronous invocation.');
  }
  if (mode === 'BackgroundOnly' && resolved !== 'Background') {
    throw new Error('This operation requires background invocation.');
  }
  if (resolved !== 'Synchronous' && resolved !== 'Background') {
    throw new Error(`Unsupported resolved invocation mode '${resolved}'.`);
  }
}

function normalizeResolvedInvocationMode(
  mode: string,
): 'Synchronous' | 'Background' {
  switch (mode.toLowerCase()) {
    case 'synchronous':
      return 'Synchronous';
    case 'background':
      return 'Background';
    default:
      throw new Error(`Unsupported resolved invocation mode '${mode}'.`);
  }
}

function normalizeInvocationModePolicy(
  value?: ClientToolPolicy['invocationModePolicy'],
): ClientToolPolicy['invocationModePolicy'] {
  switch (value?.toLowerCase()) {
    case 'backgroundonly':
      return 'BackgroundOnly';
    case 'modelchoice':
      return 'ModelChoice';
    default:
      return 'SynchronousOnly';
  }
}

function contextsMatch(
  expected?: ProviderContextSnapshot,
  current?: ProviderContextSnapshot,
): boolean {
  if (expected === undefined || current === undefined) {
    return false;
  }

  const keys: (keyof ProviderContextSnapshot)[] = [
    'workspaceId',
    'documentId',
    'fileId',
    'pageId',
    'sceneId',
    'appStateVersion',
  ];
  return keys.every(key =>
    expected[key] === undefined || expected[key] === current[key]);
}

function validateCompoundDefinition(
  schema: Record<string, unknown>,
  contract: ClientToolOperationContract,
  handlers?: Record<string, unknown>,
): void {
  if (contract.discriminator.trim().length === 0) {
    throw new Error('Compound tool discriminator is required.');
  }

  const branches = schema['oneOf'];
  if (!Array.isArray(branches) || branches.length === 0) {
    throw new Error('Compound tool schema must contain a non-empty oneOf.');
  }

  const schemaActions = new Set<string>();
  for (const branch of branches) {
    if (!isRecord(branch)) {
      throw new Error('Every compound tool oneOf branch must be an object schema.');
    }
    const properties = branch['properties'];
    const required = branch['required'];
    const discriminatorSchema = isRecord(properties)
      ? properties[contract.discriminator]
      : undefined;
    const action = isRecord(discriminatorSchema)
      ? discriminatorSchema['const']
      : undefined;
    if (!Array.isArray(required) || !required.includes(contract.discriminator) ||
        typeof action !== 'string' || action.length === 0) {
      throw new Error(
        `Every compound tool branch must require '${contract.discriminator}' with one string const value.`,
      );
    }
    if (schemaActions.has(action)) {
      throw new Error(`Duplicate compound tool action '${action}'.`);
    }
    schemaActions.add(action);
  }

  const policyActions = new Set(Object.keys(contract.actions));
  if (!setsEqual(schemaActions, policyActions)) {
    throw new Error('Compound schema action set must exactly match the operation policy action set.');
  }
  if (handlers !== undefined && !setsEqual(schemaActions, new Set(Object.keys(handlers)))) {
    throw new Error('Compound handler action set must exactly match the schema action set.');
  }

  for (const [action, policy] of Object.entries(contract.actions)) {
    if (policy.destructive === true && policy.requiresPermission !== true) {
      throw new Error(`Destructive action '${action}' must require permission.`);
    }
    if (policy.requiresPermission === true && !policy.permissionScope?.trim()) {
      throw new Error(`Permissioned action '${action}' requires permissionScope.`);
    }
    if (policy.mutatesState === true &&
        (policy.requiresPermission === undefined || policy.requiresFreshContext === undefined)) {
      throw new Error(
        `Mutating action '${action}' must explicitly declare requiresPermission and requiresFreshContext.`,
      );
    }
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function setsEqual(left: Set<string>, right: Set<string>): boolean {
  return left.size === right.size && [...left].every(value => right.has(value));
}

function toolError(kind: string, message: string, retryable?: boolean): ClientToolError {
  return { kind, message, retryable };
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function normalizeResult(result: ClientToolProviderToolResult): {
  content?: ToolResultContent[];
  augmentation?: ClientToolAugmentation;
} {
  if (isStructuredResult(result)) {
    return {
      content: normalizeContent(result.content),
      augmentation: result.augmentation,
    };
  }

  return {
    content: normalizeContent(result),
  };
}

function normalizeContent(content: ClientToolProviderToolResult): ToolResultContent[] | undefined {
  if (content === undefined) {
    return undefined;
  }

  if (typeof content === 'string') {
    return [{ type: 'text', text: content }];
  }

  if (Array.isArray(content)) {
    return content;
  }

  if (isToolResultContent(content)) {
    return [content];
  }

  throw new Error('Client tool handler returned an unsupported result value.');
}

function isStructuredResult(value: ClientToolProviderToolResult): value is {
  content?: ToolResultContent[] | string;
  augmentation?: ClientToolAugmentation;
} {
  return typeof value === 'object' &&
    value !== null &&
    !Array.isArray(value) &&
    !isToolResultContent(value) &&
    ('content' in value || 'augmentation' in value);
}

function isToolResultContent(value: unknown): value is ToolResultContent {
  return typeof value === 'object' &&
    value !== null &&
    'type' in value &&
    ((value as { type: unknown }).type === 'text' ||
      (value as { type: unknown }).type === 'json' ||
      (value as { type: unknown }).type === 'binary');
}

async function resolveValue<T>(value: T | (() => T | Promise<T>) | undefined): Promise<T | undefined> {
  if (typeof value === 'function') {
    return await (value as () => T | Promise<T>)();
  }

  return value;
}

async function withTimeout<T>(promise: T | Promise<T>, timeoutMs: number, message: string): Promise<T> {
  let timeout: ReturnType<typeof setTimeout> | undefined;
  try {
    return await Promise.race([
      Promise.resolve(promise),
      new Promise<never>((_, reject) => {
        timeout = setTimeout(() => reject(new Error(message)), timeoutMs);
      }),
    ]);
  } finally {
    if (timeout !== undefined) {
      clearTimeout(timeout);
    }
  }
}

function joinUrl(baseUrl: string, path: string): string {
  if (baseUrl.length === 0) {
    return path;
  }

  return `${baseUrl.replace(/\/+$/u, '')}/${path.replace(/^\/+/u, '')}`;
}
