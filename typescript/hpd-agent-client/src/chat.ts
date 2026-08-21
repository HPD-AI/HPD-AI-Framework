import type { AgentClient } from './client.js';
import { EventTypes } from './types/events.js';
import type { RunConfig, ThreadCompactionRequest } from './types/run-config.js';
import type { InputSubmissionResult } from './types/transport.js';
import type {
  AIContent,
  ContentReference,
  CreateSessionRequest,
  SearchSessionsRequest,
  Session,
} from './types/session.js';
import type { ThreadJournalCursor, ThreadExecution, ThreadRuntimeState } from './types/thread-execution.js';

export interface OpenChatOptions {
  agentId: string;
  threadId?: string;
  session?: {
    id?: string;
    search?: SearchSessionsRequest;
    create?: CreateSessionRequest;
    select?: (sessions: Session[]) => Session | null | undefined;
  };
}

export interface ChatSessionOptions {
  agentId: string;
  sessionId: string;
  threadId?: string;
}

export interface SendMessageInput {
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
}

export interface SendMessageOptions {
  runConfig?: RunConfig;
  signal?: AbortSignal;
  optimisticUserMessage?: boolean;
  delivery?: 'Queue' | 'Steer';
  threadExecutionId?: string;
}

export interface CancelActiveTurnOptions {
  reason?: string;
  eventFlowId?: string | null;
  signal?: AbortSignal;
}

export class ChatManager {
  constructor(private readonly client: AgentClient) {
  }

  session(options: ChatSessionOptions): ChatSession {
    return new ChatSession(this.client, options);
  }

  async open(options: OpenChatOptions): Promise<ChatSession> {
    const threadId = options.threadId ?? 'main';
    let sessionId = options.session?.id;

    if (!sessionId && options.session?.search) {
      const sessions = await this.client.searchSessions(options.session.search);
      const selected = options.session.select?.(sessions) ?? sessions[0];
      sessionId = selected?.id;
    }

    if (!sessionId) {
      const created = await this.client.createSession(options.session?.create);
      sessionId = created.id;
    }

    return new ChatSession(this.client, {
      agentId: options.agentId,
      threadId,
      sessionId,
    });
  }
}

/**
 * Convenience wrapper for a single agent/session/thread chat.
 *
 * ChatSession scopes common chat operations to one agent/session/thread. Transcript
 * rendering is intentionally left to applications via the acknowledged committed
 * event observer and AgentClient.on/onAny.
 */
export class ChatSession {
  readonly agentId: string;
  sessionId: string;
  threadId: string;

  constructor(private readonly client: AgentClient, options: ChatSessionOptions) {
    this.agentId = options.agentId;
    this.sessionId = options.sessionId;
    this.threadId = options.threadId ?? 'main';
  }

  dispose(): void {
  }

  async getExecutions(): Promise<ThreadExecution[]> {
    return this.client.getThreadExecutions(this.agentId, this.sessionId, this.threadId);
  }

  async getState(): Promise<ThreadRuntimeState | null> {
    return this.client.getThreadState(this.agentId, this.sessionId, this.threadId);
  }

  async getExecution(threadExecutionId: string): Promise<ThreadExecution | null> {
    return this.client.getThreadExecution(this.agentId, this.sessionId, this.threadId, threadExecutionId);
  }

  async subscribeLive(options: { after?: ThreadJournalCursor; signal?: AbortSignal } = {}): Promise<ThreadRuntimeState> {
    const state = await this.getState();
    if (!state) {
      throw new Error(`Thread '${this.threadId}' was not found in session '${this.sessionId}'.`);
    }

    await this.client.start({
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      after: options.after ?? {
        generation: state.observedCursor.generation,
        sequenceNumber: 0,
      },
      signal: options.signal,
    });
    return state;
  }

  async disconnectLive(): Promise<void> {
    await this.client.stop();
  }

  async submitMessage(
    input: SendMessageInput,
    options: SendMessageOptions = {},
  ): Promise<InputSubmissionResult> {
    const contents = [...input.contents];
    if (contents.length === 0) {
      throw new Error('submitMessage() requires at least one content item.');
    }
    const delivery = options.delivery ?? 'Queue';
    if (delivery === 'Steer' && !options.threadExecutionId) {
      throw new Error('submitMessage() requires threadExecutionId when delivery is Steer.');
    }

    const result = await this.client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      delivery,
      threadExecutionId: options.threadExecutionId,
      messages: [{
        role: 'user',
        contents,
        additionalProperties: input.additionalProperties,
      }],
      runConfig: options.runConfig,
    }, { signal: options.signal });
    if (!('disposition' in result) ||
        (delivery === 'Queue' && result.disposition !== 'queued')) {
      throw new Error('Backend returned a non-submission result for a user message.');
    }

    return result;
  }

  async compactThread(
    request: ThreadCompactionRequest,
    options: SendMessageOptions = {},
  ): Promise<InputSubmissionResult> {
    const result = await this.client.submitInput({
      type: EventTypes.COMPACT_THREAD_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      request,
      runConfig: options.runConfig,
    }, { signal: options.signal });
    if (!('disposition' in result) || result.disposition !== 'queued') {
      throw new Error('Backend returned a non-submission result for compaction.');
    }
    return result;
  }

  async steer(text: string, options: SendMessageOptions = {}): Promise<InputSubmissionResult> {
    if (!text.trim()) throw new Error('steer() requires non-empty text.');
    const state = await this.getState();
    const activeExecution = state?.activeExecution;
    if (!activeExecution) {
      return { disposition: 'no_active_execution', activeExecution: null };
    }
    const result = await this.client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      threadExecutionId: activeExecution.threadExecutionId,
      delivery: 'Steer',
      messages: [{ role: 'user', contents: [createTextContent(text)] }],
    }, { signal: options.signal });
    if (!('disposition' in result)) throw new Error('Backend returned a non-input result for steering.');
    return result;
  }

  async cancelActiveTurn(options: CancelActiveTurnOptions = {}): Promise<InputSubmissionResult> {
    const state = await this.getState();
    const activeExecution = state?.activeExecution;
    if (!activeExecution) {
      return { disposition: 'no_active_execution', activeExecution: null };
    }

    const result = await this.client.cancelThreadExecution(
      this.agentId,
      this.sessionId,
      this.threadId,
      activeExecution.threadExecutionId,
      { signal: options.signal },
    );
    return {
      disposition: result.cancellationApplied ? 'accepted' : 'no_active_execution',
      threadExecutionId: result.threadExecutionId,
      activeExecution: null,
    };
  }

  async startQueuedWork(): Promise<ThreadExecution> {
    return this.client.startQueuedWork(this.agentId, this.sessionId, this.threadId);
  }

  async refreshSession(): Promise<Session | null> {
    return this.client.getSession(this.sessionId);
  }

  switchSession(sessionId: string, threadId = this.threadId): void {
    this.sessionId = sessionId;
    this.threadId = threadId;
  }
}

export function createTextContent(text: string): AIContent {
  return { $type: 'text', text };
}

export function contentReferenceToUriContent(reference: ContentReference): AIContent {
  return {
    $type: 'uri',
    uri: `hpd-content://${reference.contentId}`,
    mediaType: reference.contentType,
    additionalProperties: {
      contentId: reference.contentId,
      version: reference.version,
      name: reference.name,
      sizeBytes: reference.sizeBytes,
    },
  };
}
