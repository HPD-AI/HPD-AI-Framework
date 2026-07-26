import type {
  BackgroundHandleKind,
  BackgroundHandleOperation,
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
  | 'connecting'
  | 'connected'
  | 'ready'
  | 'closed';

export type ProviderWebSocketFactory = (url: string) => WebSocket;

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
  routes?: Partial<ClientToolProviderRoutes>;
  identity: ClientToolProviderIdentity;
  appProvider: ClientAppProviderDescriptor;
  context?: ClientToolProviderContext | (() => ClientToolProviderContext | Promise<ClientToolProviderContext>);
  readiness?: ClientToolProviderReadiness | (() => ClientToolProviderReadiness | Promise<ClientToolProviderReadiness>);
  metadata?: Record<string, unknown>;
  concurrency?: {
    maxQueueDepth?: number;
    invocationTimeoutMs?: number;
  };
  webSocketFactory?: ProviderWebSocketFactory;
  contextSnapshot?: () => ProviderContextSnapshot | Promise<ProviderContextSnapshot>;
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

export interface ClientToolProviderOperationToolOptions<TRequest, TResult = ClientToolProviderToolResult> {
  description: string;
  discriminator: string;
  parametersSchema: Record<string, unknown>;
  defaultPolicy?: ClientToolPolicy;
  actions: Record<string, ClientToolPolicy>;
  metadata?: Record<string, unknown>;
  parse: (value: unknown) => TRequest;
  handler?: ClientToolProviderOperationToolHandler<TRequest, TResult>;
  handlers?: Record<string, ClientToolProviderOperationToolHandler<TRequest, TResult>>;
}

export interface ClientToolProviderToolContext {
  invocation: ClientToolProviderInvokeToolMessage;
  requestedInvocationMode?: string;
  expectedContext?: ProviderContextSnapshot;
  currentContext?: ProviderContextSnapshot;
  policy: ClientToolPolicy;
  complete: (content?: ClientToolProviderToolResult, augmentation?: ClientToolAugmentation) => void;
  reject: (error: ClientToolError) => void;
  fail: (error: ClientToolError) => void;
  acceptBackground: (
    clientOperationId: string,
    options?: {
      content?: ClientToolProviderToolResult;
      handleKind?: BackgroundHandleKind;
      supportedOperations?: BackgroundHandleOperation;
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
  operationTool<TRequest, TResult = ClientToolProviderToolResult>(
    name: string,
    options: ClientToolProviderOperationToolOptions<TRequest, TResult>,
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
  private socket?: WebSocket;
  private heartbeatTimer?: ReturnType<typeof setInterval>;
  private clientRuntimeId?: string;
  private connectionId?: string;
  private statusValue: ProviderConnectionStatus = 'idle';
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
    if (this.statusValue === 'connecting' || this.statusValue === 'connected' || this.statusValue === 'ready') {
      return;
    }

    this.statusValue = 'connecting';
    const socket = this.createSocket();
    this.socket = socket;

    const ready = new Promise<void>((resolve, reject) => {
      socket.onmessage = async event => {
        try {
          await this.handleMessage(String(event.data));
          if (this.statusValue === 'ready') {
            resolve();
          }
        } catch (error) {
          reject(error instanceof Error ? error : new Error(String(error)));
        }
      };
      socket.onclose = () => {
        this.markClosed();
        reject(new Error('Client tool provider websocket closed before welcome.'));
      };
    });

    await new Promise<void>((resolve, reject) => {
      socket.onopen = () => {
        this.send({
          type: 'provider.hello',
          protocolVersion: '2',
          identity: this.options.identity,
        } satisfies ClientToolProviderHelloMessage);
        resolve();
      };
      socket.onerror = () => reject(new Error('Client tool provider websocket failed to connect.'));
    });

    await ready;
  }

  public async disconnect(reason = 'Provider disconnected.'): Promise<void> {
    this.sendRelease(reason);
    this.markClosed();
    this.socket?.close();
  }

  public async updateManifest(): Promise<void> {
    this.send(await this.createManifestMessage());
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

  private createSocket(): WebSocket {
    const url = this.resolveWebSocketUrl();
    if (this.webSocketFactory !== undefined) {
      return this.webSocketFactory(url);
    }

    if (typeof WebSocket === 'undefined') {
      throw new Error('No WebSocket implementation is available. Pass webSocketFactory in this runtime.');
    }

    return new WebSocket(url);
  }

  private resolveWebSocketUrl(): string {
    if (this.explicitUrl !== undefined) {
      return this.toWebSocketUrl(this.explicitUrl);
    }

    const baseUrl = this.options.baseUrl ?? '';
    return this.toWebSocketUrl(joinUrl(baseUrl, this.routes.connect));
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
    this.statusValue = 'connected';
    this.startHeartbeat(message.heartbeatIntervalMs);
    await this.updateManifest();
    this.statusValue = 'ready';
  }

  private handleError(message: ClientToolProviderErrorMessage): void {
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
    try {
      validateInvocationMode(policy, message.requestedInvocationMode);
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
      requestedInvocationMode: message.requestedInvocationMode,
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
      acceptBackground: (clientOperationId, options) => reply({
        outcome: 'AcceptedBackground',
        clientOperationId,
        content: normalizeContent(options?.content),
        handleKind: options?.handleKind,
        supportedOperations: options?.supportedOperations,
        augmentation: options?.augmentation,
      }),
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
      metadata: this.options.metadata,
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

  private markClosed(): void {
    this.statusValue = 'closed';
    if (this.heartbeatTimer !== undefined) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = undefined;
    }
  }
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

  public operationTool<TRequest, TResult = ClientToolProviderToolResult>(
    name: string,
    options: ClientToolProviderOperationToolOptions<TRequest, TResult>,
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
  'backgroundNotification'>> = {
  requiresPermission: false,
  mutatesState: false,
  requiresFreshContext: false,
  destructive: false,
  idempotent: false,
  invocationModePolicy: 'SynchronousOnly',
  backgroundNotification: {
    kind: 'on_final_state',
    completed: true,
    faulted: true,
    cancelled: false,
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

function validateInvocationMode(policy: ClientToolPolicy, requested?: string): void {
  const mode = normalizeInvocationModePolicy(policy.invocationModePolicy);
  if (mode === 'SynchronousOnly' && requested === 'Background') {
    throw new Error('This operation only supports synchronous invocation.');
  }
  if (mode === 'BackgroundOnly' && requested !== 'Background') {
    throw new Error('This operation requires background invocation.');
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
  return keys.every(key => expected[key] === current[key]);
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

  return undefined;
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
