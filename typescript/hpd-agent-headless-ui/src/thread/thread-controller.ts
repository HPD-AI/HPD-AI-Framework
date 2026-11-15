import { clientToolText, EventTypes, type AgentClient, type ThreadObservation } from '@hpd-research/hpd-agent-client';
import type {
  AIContent, AgentEvent, AgentResponseInput, AgentRunInputEvent, ClientToolInvokeOutcome,
  InputSubmissionResult, RespondResult, ThreadJournalCursor,
} from './types.js';
import { createThreadProjection } from './thread-projection.js';
import { loadThreadSnapshot } from './load-thread-snapshot.js';
import { getTextSubmissionState } from './selectors.js';
import { eventBelongsToScope, withThreadScope } from './scope.js';
import type {
  ThreadController,
  ThreadControllerOptions,
  ConnectOptions,
  ClientToolOutcomeInput,
  ClientToolRuntimeRequest,
  ClarificationRuntimeRequest,
  InterruptOptions,
  PermissionRuntimeRequest,
  AnswerClientToolRequestOptions,
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
  private readonly allowScopeLessEvents: boolean;
  private observation?: ThreadObservation;
  private disposed = false;
  private _connected = false;
  private _loading = false;
  private _error: string | null = null;
  private appliedCursor: ThreadJournalCursor = { generation: 1, sequenceNumber: 0 };
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
    this.allowScopeLessEvents = options.allowScopeLessEvents ?? false;
  }

  get connected(): boolean {
    return this._connected;
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
      this.appliedCursor = {
        generation: snapshot.observedCursor.generation,
        sequenceNumber: 0,
      };
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

    try {
      const { sessionId, threadId, agentId } = this.scope;
      const thread = await (await this.client.sessions.get(sessionId)).threads.get(threadId);
      const observation = await thread.observe({
        agentId,
        after: { generation: this.appliedCursor.generation, sequence: this.appliedCursor.sequenceNumber },
        signal: options.signal,
        onEvent: ({ event }) => this.handleEvent(event),
        onRebase: async ({ generation }) => {
          const snapshot = await loadThreadSnapshot({ client: this.client, ...this.scope });
          this.projection.rehydrate(snapshot);
          this.appliedCursor = { generation, sequenceNumber: 0 };
        },
      });
      this.observation = observation;
      this._connected = true;
      void observation.done.catch(error => {
        this._error = error instanceof Error ? error.message : String(error);
      }).finally(() => {
        if (this.observation === observation) this._connected = false;
      });
    } catch (error) {
      this._error = error instanceof Error ? error.message : String(error);
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    const observation = this.observation;
    this.observation = undefined;
    this._connected = false;
    await observation?.close();
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
      type: 'USER_MESSAGES_INPUT',
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

  async run(input: AgentRunInputEvent): Promise<InputSubmissionResult> {
    this.throwIfDisposed();
    return this.client.api.agentRuntime.submitAgentInput({ ...this.scope, input: { ...input, version: '1.0' } });
  }

  async respond(input: AgentResponseInput): Promise<RespondResult> {
    this.throwIfDisposed();
    return this.client.api.agentInteractions.respondToAgentRequest({ ...this.scope, input: { ...input, version: '1.0' } });
  }

  async steer(text: string, options: SendMessageOptions = {}): Promise<InputSubmissionResult> {
    this.throwIfDisposed();
    if (!text.trim()) throw new Error('steer() requires non-empty text.');
    options.signal?.throwIfAborted();
    const state = await this.client.api.agentRuntime.getAgentThreadRuntimeState(this.scope);
    if (!state.activeExecution) return { disposition: 'no_active_execution', activeExecution: null };
    return this.run({
      type: 'USER_MESSAGES_INPUT',
      threadExecutionId: state.activeExecution.threadExecutionId,
      delivery: 'Steer',
      messages: [{ role: 'user', contents: [{ $type: 'text', text }] }],
    });
  }

  async interrupt(options: InterruptOptions = {}): Promise<InputSubmissionResult> {
    this.throwIfDisposed();
    options.signal?.throwIfAborted();
    const state = await this.client.api.agentRuntime.getAgentThreadRuntimeState(this.scope);
    if (!state.activeExecution) return { disposition: 'no_active_execution', activeExecution: null };
    const result = await this.client.api.executions.cancelThreadExecution({
      ...this.scope,
      threadExecutionId: state.activeExecution.threadExecutionId,
    });
    return {
      disposition: result.cancellationApplied ? 'accepted' : 'no_active_execution',
      threadExecutionId: result.threadExecutionId,
      activeExecution: null,
    };
  }

  async approve(permissionId: string, choiceId?: string): Promise<RespondResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is PermissionRuntimeRequest =>
        request.kind === 'permission' && request.id === permissionId);
    if (!pending) return missingRequest(permissionId);

    return this.respond({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.request.sourceName,
      choiceId: (() => {
        const choice = pending.request.choices.find(candidate => candidate.kind === 'Allow' &&
          (choiceId === undefined || candidate.id === choiceId));
        if (!choice) throw new Error('No matching declared Allow choice');
        return choice.id;
      })(),
    });
  }

  async deny(permissionId: string, reason?: string): Promise<RespondResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is PermissionRuntimeRequest =>
        request.kind === 'permission' && request.id === permissionId);
    if (!pending) return missingRequest(permissionId);

    return this.respond({
      type: EventTypes.PERMISSION_RESPONSE,
      permissionId,
      sourceName: pending.request.sourceName,
      choiceId: (() => {
        const choice = pending.request.choices.find(candidate => candidate.kind === 'Deny');
        if (!choice) throw new Error('No declared Deny choice');
        return choice.id;
      })(),
      feedback: reason,
    });
  }

  async clarify(requestId: string, answer: string): Promise<RespondResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is ClarificationRuntimeRequest =>
        request.kind === 'clarification' && request.id === requestId);
    if (!pending) return missingRequest(requestId);

    return this.respond({
      type: 'QUESTION_RESPONSE',
      requestId,
      sourceName: pending.request.sourceName,
      outcome: 'Answered',
      answers: [{ questionId: pending.request.questionId, selectedOptionIds: [], customText: answer }],
    });
  }

  async answerClientToolRequest(
    requestId: string,
    outcome: ClientToolOutcomeInput,
    options: AnswerClientToolRequestOptions = {},
  ): Promise<RespondResult> {
    this.throwIfDisposed();
    const pending = this.projection.getSnapshot().pendingRuntimeRequests
      .find((request): request is ClientToolRuntimeRequest =>
        request.kind === 'client-tool' && request.id === requestId);
    if (!pending) return missingRequest(requestId);

    const normalized = normalizeClientToolOutcome(requestId, outcome);

    return this.respond({
      type: EventTypes.CLIENT_TOOL_INVOKE_OUTCOME,
      requestId,
      outcome: normalized.outcome,
      content: normalized.content,
      errorMessage: normalized.errorMessage,
      clientOperationId: normalized.clientOperationId,
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
    if (event.threadSequenceNumber && event.threadSequenceNumber > this.appliedCursor.sequenceNumber) {
      this.appliedCursor = {
        generation: this.appliedCursor.generation,
        sequenceNumber: event.threadSequenceNumber,
      };
    }
  }

  private projectOptimisticUserMessage(
    contents: readonly AIContent[],
    clientInputId: string,
    additionalProperties?: Record<string, unknown>,
  ): void {
    const messageId = `optimistic:user:${clientInputId}`;

    this.projection.project(withThreadScope({
      type: EventTypes.TEXT_MESSAGE_START,
      messageId,
      role: 'user',
      source: 'UserInput',
      visibility: 'Transcript',
      persistence: 'ThreadHistory',
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


}

function missingRequest(requestId: string): RespondResult {
  return {
    status: 1,
    requestId,
    message: 'The request is not pending in this thread projection.',
    accepted: false,
  };
}

function normalizeClientToolOutcome(
  requestId: string,
  outcome: ClientToolOutcomeInput,
): ClientToolInvokeOutcome {
  if (typeof outcome === 'string' || Array.isArray(outcome)) {
    return typeof outcome === 'string'
      ? {
          requestId,
          ...clientToolText(outcome),
          augmentation: undefined as ClientToolInvokeOutcome['augmentation'],
        }
      : { requestId, outcome: 'Completed', content: outcome };
  }

  if (outcome.requestId !== requestId) {
    throw new Error('Client tool outcome requestId must match the pending request.');
  }

  return outcome;
}
