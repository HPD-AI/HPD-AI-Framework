import type { AgentClient } from './client.js';
import { EventTypes } from './types/events.js';
import type { RunConfig } from './types/run-config.js';
import type { InputSubmissionResult, InterruptionResult } from './types/transport.js';
import type {
  AIContent,
  ContentReference,
  ThreadEvent,
  CreateSessionRequest,
  SearchSessionsRequest,
  Session,
} from './types/session.js';
import type { ThreadRun, ThreadRuntimeState } from './types/thread-run.js';

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
 * rendering is intentionally left to applications via AgentClient.on/onAny and
 * getThreadEvents().
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

  async getThreadEvents(): Promise<ThreadEvent[]> {
    return this.client.getThreadEvents(this.sessionId, this.threadId);
  }

  async getRuns(): Promise<ThreadRun[]> {
    return this.client.getThreadRuns(this.agentId, this.sessionId, this.threadId);
  }

  async getState(): Promise<ThreadRuntimeState | null> {
    return this.client.getThreadState(this.agentId, this.sessionId, this.threadId);
  }

  async getRun(runtimeRunId: string): Promise<ThreadRun | null> {
    return this.client.getThreadRun(this.agentId, this.sessionId, this.threadId, runtimeRunId);
  }

  async subscribeLive(options: { signal?: AbortSignal } = {}): Promise<ThreadRuntimeState> {
    const state = await this.getState();
    if (!state) {
      throw new Error(`Thread '${this.threadId}' was not found in session '${this.sessionId}'.`);
    }

    await this.client.start({
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      afterSequenceNumber: state.latestSequenceNumber,
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

    const result = await this.client.submitInput({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      messages: [{
        role: 'user',
        contents,
        additionalProperties: input.additionalProperties,
      }],
      runConfig: options.runConfig,
    }, { signal: options.signal });
    if (!('runtimeRunId' in result) || !('startedAt' in result)) {
      throw new Error('Backend returned a non-submission result for a user message.');
    }

    return result;
  }

  async cancelActiveTurn(options: CancelActiveTurnOptions = {}): Promise<InterruptionResult> {
    const state = await this.getState();
    const activeRun = state?.activeRun;
    if (!activeRun) {
      return { status: 'no_active_run', activeRun: null };
    }

    const result = await this.client.submitInput({
      type: EventTypes.INTERRUPTION_REQUEST,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      expectedRuntimeRunId: activeRun.runtimeRunId,
      eventFlowId: options.eventFlowId ?? undefined,
      reason: options.reason ?? 'Interrupted by client.',
      source: 'User',
    }, { signal: options.signal });
    if (!('status' in result) || !isInterruptionStatus(result.status)) {
      throw new Error('Backend returned a non-interruption result for cancellation.');
    }

    return {
      status: result.status,
      activeRun: 'activeRun' in result ? result.activeRun : null,
    };
  }

  async refreshSession(): Promise<Session | null> {
    return this.client.getSession(this.sessionId);
  }

  switchSession(sessionId: string, threadId = this.threadId): void {
    this.sessionId = sessionId;
    this.threadId = threadId;
  }
}

function isInterruptionStatus(value: unknown): value is InterruptionResult['status'] {
  return value === 'accepted' ||
    value === 'already_terminal' ||
    value === 'no_active_run' ||
    value === 'active_run_mismatch';
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
