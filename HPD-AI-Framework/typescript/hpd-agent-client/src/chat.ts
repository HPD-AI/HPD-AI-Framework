import type { AgentClient } from './client.js';
import { EventTypes } from './types/events.js';
import type { RunConfig } from './types/run-config.js';
import type { ThreadEvent, CreateSessionRequest, SearchSessionsRequest, Session } from './types/session.js';
import type { ThreadRun } from './types/thread-run.js';

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

export interface SendTextOptions {
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

  async getActiveRun(): Promise<ThreadRun | null> {
    return this.client.getActiveThreadRun(this.agentId, this.sessionId, this.threadId);
  }

  async getRun(runtimeRunId: string): Promise<ThreadRun | null> {
    return this.client.getThreadRun(this.agentId, this.sessionId, this.threadId, runtimeRunId);
  }

  async subscribeLive(options: { signal?: AbortSignal } = {}): Promise<void> {
    await this.client.start({
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      signal: options.signal,
    });
  }

  async disconnectLive(): Promise<void> {
    await this.client.stop();
  }

  async submitText(text: string, options: SendTextOptions = {}): Promise<void> {
    await this.client.submitInput({
      type: EventTypes.USER_TEXT_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      text,
      runConfig: options.runConfig,
    }, { signal: options.signal });
  }

  async sendText(text: string, options: SendTextOptions = {}): Promise<void> {
    await this.submitText(text, options);
  }

  async cancelActiveTurn(options: CancelActiveTurnOptions = {}): Promise<void> {
    await this.client.submitInput({
      type: EventTypes.INTERRUPTION_REQUEST,
      agentId: this.agentId,
      sessionId: this.sessionId,
      threadId: this.threadId,
      eventFlowId: options.eventFlowId ?? undefined,
      reason: options.reason ?? 'Interrupted by client.',
      source: 'User',
    }, { signal: options.signal });
  }

  async refreshSession(): Promise<Session | null> {
    return this.client.getSession(this.sessionId);
  }

  switchSession(sessionId: string, threadId = this.threadId): void {
    this.sessionId = sessionId;
    this.threadId = threadId;
  }
}
