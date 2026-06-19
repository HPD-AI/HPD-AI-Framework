import {
  createSuccessResponse,
  EventTypes,
  type AIContent,
  type AgentClient,
  type AgentEvent,
  type AgentRunInputEvent,
  type ClientToolInvokeResponse,
  type EventSubscription,
  type PermissionChoice,
  type SubmitInputResult,
  type ToolResultContent,
} from '@hpd-research/hpd-agent-client';
import { createThreadProjection } from './thread-projection.js';
import { loadThreadSnapshot } from './load-thread-snapshot.js';
import { getTextSubmissionState } from './selectors.js';
import { eventBelongsToScope, withThreadScope } from './scope.js';
import type {
  ThreadController,
  ThreadControllerOptions,
  ConnectOptions,
  ClientToolResponseInput,
  ClientToolRuntimeRequest,
  ClarificationRuntimeRequest,
  InterruptOptions,
  PermissionRuntimeRequest,
  RespondToClientToolOptions,
  RehydrateOptions,
  SendMessageInput,
  SendMessageOptions,
} from './types.js';

export function createThreadController(options: ThreadControllerOptions): ThreadController {
  return new ThreadControllerImpl(options);
}

class ThreadControllerImpl implements ThreadController {
  readonly scope;
  readonly projection;
  private readonly client: AgentClient;
  private readonly autoConnectOnSend: boolean;
  private readonly stopClientOnDisconnect: boolean;
  private readonly allowScopeLessEvents: boolean;
  private eventSubscription: EventSubscription | null = null;
  private errorSubscription: EventSubscription | null = null;
  private disposed = false;
  private _connected = false;
  private _loading = false;
  private _error: string | null = null;
  private optimisticInputIndex = 0;

  constructor(options: ThreadControllerOptions) {
    this.client = options.client;
    this.scope = {
      agentId: options.agentId,
      sessionId: options.sessionId,
      threadId: options.threadId,
    };
    this.projection = options.projection ?? createThreadProjection();
    this.autoConnectOnSend = options.autoConnectOnSend ?? true;
    this.stopClientOnDisconnect = options.stopClientOnDisconnect ?? true;
    this.allowScopeLessEvents = options.allowScopeLessEvents ?? false;
  }

  get connected(): boolean {
    return this._connected && this.client.connected;
  }

  get loading(): boolean {
    return this._loading;
  }

  get error(): string | null {
    return this._error;
  }

  clearError(): void {
    this._error = null;
    this.projection.clearError();
  }

  async start(options: RehydrateOptions & ConnectOptions = {}): Promise<void> {
    await this.rehydrate(options);
    await this.connect(options);
  }

  async rehydrate(options: RehydrateOptions = {}): Promise<void> {
    this.throwIfDisposed();
    this._loading = true;
    this._error = null;
    try {
      const snapshot = await loadThreadSnapshot({
        client: this.client,
        ...this.scope,
      }, options);
      this.projection.rehydrate(snapshot);
    } catch (error) {
      this._error = error instanceof Error ? error.message : String(error);
      throw error;
    } finally {
      this._loading = false;
    }
  }

  async connect(options: ConnectOptions = {}): Promise<void> {
    this.throwIfDisposed();
    if (this.connected) return;

    this.eventSubscription ??= this.client.onAny((event) => {
      this.handleEvent(event);
    });
    this.errorSubscription ??= this.client.onError((error) => {
      this._error = error.message;
    });

    try {
      await this.client.start({
        ...this.scope,
        signal: options.signal,
      });
      this._connected = true;
    } catch (error) {
      this.detachListeners();
      this._error = error instanceof Error ? error.message : String(error);
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    if (this.stopClientOnDisconnect) {
      await this.client.stop();
    }
    this.detachListeners();
    this._connected = false;
  }

  async dispose(): Promise<void> {
    if (this.disposed) return;
    this.disposed = true;
    await this.disconnect();
  }

  async sendMessage(input: SendMessageInput, options: SendMessageOptions = {}): Promise<void> {
    this.throwIfDisposed();
    const contents = [...input.contents];
    if (contents.length === 0) {
      throw new Error('Thread message submission requires at least one content item.');
    }

    const submission = getTextSubmissionState(this.projection.getSnapshot());
    if (!submission.canSubmit) {
      throw new Error(`Thread message submission is blocked: ${submission.reason ?? 'unknown'}.`);
    }

    if (!this.connected) {
      if (!this.autoConnectOnSend) {
        throw new Error('Thread controller is not connected.');
      }
      await this.connect({ signal: options.signal });
    }

    const clientInputId = `client:user:${Date.now()}:${this.optimisticInputIndex++}`;
    this.projectOptimisticUserMessage(contents, clientInputId, input.additionalProperties);

    await this.run({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: this.scope.agentId,
      sessionId: this.scope.sessionId,
      threadId: this.scope.threadId,
      messages: [{
        role: 'user',
        contents,
        additionalProperties: input.additionalProperties,
      }],
      runConfig: options.runConfig,
      clientInputId,
    });
  }

  async run(input: AgentRunInputEvent): Promise<SubmitInputResult> {
    this.throwIfDisposed();
    return this.client.run(stampInputScope(input, this.scope));
  }

  async respond(input: AgentRunInputEvent): Promise<SubmitInputResult> {
    return this.run(input);
  }

  async interrupt(options: InterruptOptions = {}): Promise<void> {
    this.throwIfDisposed();
    await this.client.submitInput({
      type: EventTypes.INTERRUPTION_REQUEST,
      agentId: this.scope.agentId,
      sessionId: this.scope.sessionId,
      threadId: this.scope.threadId,
      reason: options.reason ?? 'Interrupted by client.',
      source: 'User',
      eventFlowId: options.eventFlowId ?? undefined,
    }, { signal: options.signal });
  }

  async approve(permissionId: string, choice: PermissionChoice = 'ask'): Promise<SubmitInputResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is PermissionRuntimeRequest =>
        request.kind === 'permission' && request.id === permissionId);
    if (!pending) return;

    return this.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.request.sourceName,
      approved: true,
      choice,
    });
  }

  async deny(permissionId: string, reason?: string): Promise<SubmitInputResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is PermissionRuntimeRequest =>
        request.kind === 'permission' && request.id === permissionId);
    if (!pending) return;

    return this.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.request.sourceName,
      approved: false,
      reason,
    });
  }

  async clarify(requestId: string, answer: string): Promise<SubmitInputResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is ClarificationRuntimeRequest =>
        request.kind === 'clarification' && request.id === requestId);
    if (!pending) return;

    return this.run({
      type: EventTypes.CLARIFICATION_RESPONSE,
      requestId,
      sourceName: pending.request.sourceName,
      question: pending.request.question,
      answer,
    });
  }

  async respondToClientTool(
    requestId: string,
    response: ClientToolResponseInput,
    options: RespondToClientToolOptions = {},
  ): Promise<SubmitInputResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is ClientToolRuntimeRequest =>
        request.kind === 'client-tool' && request.id === requestId);
    if (!pending) return;

    const normalized = normalizeClientToolResponse(requestId, response);

    return this.run({
      type: EventTypes.CLIENT_TOOL_INVOKE_RESPONSE,
      requestId,
      content: normalized.content,
      success: normalized.success,
      errorMessage: normalized.errorMessage,
      augmentation: normalized.augmentation ?? options.augmentation,
      responderId: options.responderId,
      responderGroup: options.responderGroup,
      capabilities: options.capabilities,
    });
  }

  private handleEvent(event: AgentEvent): void {
    if (!eventBelongsToScope(event, this.scope, { allowScopeLess: this.allowScopeLessEvents })) {
      return;
    }
    this.projection.project(event);
  }

  private projectOptimisticUserMessage(
    contents: readonly AIContent[],
    clientInputId: string,
    additionalProperties?: Record<string, unknown>,
  ): void {
    const messageId = `optimistic:user:${clientInputId}`;

    this.projection.project(withThreadScope({
      type: EventTypes.MESSAGE_STARTED,
      messageId,
      role: 'user',
      clientInputId,
      optimistic: true,
      additionalProperties,
    }, this.scope));
    for (const content of contents) {
      if (content.$type === 'text' && typeof content.text === 'string') {
        this.projection.project(withThreadScope({
          type: EventTypes.TEXT_DELTA,
          messageId,
          text: content.text,
        }, this.scope));
      } else {
        this.projection.project(withThreadScope({
          type: EventTypes.CONTENT_ADDED,
          messageId,
          content,
        }, this.scope));
      }
    }
    this.projection.project(withThreadScope({
      type: EventTypes.TEXT_MESSAGE_END,
      messageId,
    }, this.scope));
  }

  private throwIfDisposed(): void {
    if (this.disposed) {
      throw new Error('Thread controller has been disposed.');
    }
  }

  private detachListeners(): void {
    this.eventSubscription?.dispose();
    this.errorSubscription?.dispose();
    this.eventSubscription = null;
    this.errorSubscription = null;
  }
}

function stampInputScope(input: AgentRunInputEvent, scope: ThreadController['scope']): AgentRunInputEvent {
  return withThreadScope(input, scope) as AgentRunInputEvent;
}

function normalizeClientToolResponse(
  requestId: string,
  response: ClientToolResponseInput,
): ClientToolInvokeResponse {
  if (typeof response === 'string' || Array.isArray(response)) {
    return createSuccessResponse(requestId, response as string | ToolResultContent[]);
  }

  if (response.requestId !== requestId) {
    throw new Error('Client tool response requestId must match the pending request.');
  }

  return response;
}
