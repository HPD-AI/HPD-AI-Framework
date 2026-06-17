import {
  EventTypes,
  type AgentClient,
  type AgentEvent,
  type AgentRunInputEvent,
  type EventSubscription,
  type PermissionChoice,
  type RespondResult,
} from '@hpd-research/hpd-agent-client';
import { createThreadProjection } from './thread-projection.js';
import { loadThreadSnapshot } from './load-thread-snapshot.js';
import { eventBelongsToScope, withThreadScope } from './scope.js';
import type {
  ThreadController,
  ThreadControllerOptions,
  ConnectOptions,
  InterruptOptions,
  RehydrateOptions,
  SendTextOptions,
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

  async sendText(text: string, options: SendTextOptions = {}): Promise<void> {
    this.throwIfDisposed();
    if (!this.connected) {
      if (!this.autoConnectOnSend) {
        throw new Error('Thread controller is not connected.');
      }
      await this.connect({ signal: options.signal });
    }

    await this.run({
      type: EventTypes.USER_TEXT_INPUT,
      agentId: this.scope.agentId,
      sessionId: this.scope.sessionId,
      threadId: this.scope.threadId,
      text,
      runConfig: options.runConfig,
    });
  }

  async run(input: AgentRunInputEvent): Promise<RespondResult | undefined> {
    this.throwIfDisposed();
    return this.client.run(stampInputScope(input, this.scope));
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

  async approve(permissionId: string, choice: PermissionChoice = 'ask'): Promise<RespondResult | undefined> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingPermissions
      .find((request) => request.permissionId === permissionId);
    if (!pending) return;

    return this.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.sourceName,
      approved: true,
      choice,
    });
  }

  async deny(permissionId: string, reason?: string): Promise<RespondResult | undefined> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingPermissions
      .find((request) => request.permissionId === permissionId);
    if (!pending) return;

    return this.run({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.sourceName,
      approved: false,
      reason,
    });
  }

  async clarify(requestId: string, answer: string): Promise<RespondResult | undefined> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingClarifications
      .find((request) => request.requestId === requestId);
    if (!pending) return;

    return this.run({
      type: EventTypes.CLARIFICATION_RESPONSE,
      requestId,
      sourceName: pending.sourceName,
      question: pending.question,
      answer,
    });
  }

  private handleEvent(event: AgentEvent): void {
    if (!eventBelongsToScope(event, this.scope, { allowScopeLess: this.allowScopeLessEvents })) {
      return;
    }
    this.projection.project(event);
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
