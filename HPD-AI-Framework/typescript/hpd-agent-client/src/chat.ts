import type { AgentClient } from './client.js';
import { EventTypes } from './types/events.js';
import type { RunConfig } from './types/run-config.js';
import type { BranchEvent, CreateSessionRequest, SearchSessionsRequest, Session } from './types/session.js';
import type { BranchRun } from './types/branch-run.js';

export interface OpenChatOptions {
  agentId: string;
  branchId?: string;
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
  branchId?: string;
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
    const branchId = options.branchId ?? 'main';
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
      branchId,
      sessionId,
    });
  }
}

/**
 * Convenience wrapper for a single agent/session/branch chat.
 *
 * ChatSession scopes common chat operations to one agent/session/branch. Transcript
 * rendering is intentionally left to applications via AgentClient.on/onAny and
 * getBranchEvents().
 */
export class ChatSession {
  readonly agentId: string;
  sessionId: string;
  branchId: string;

  constructor(private readonly client: AgentClient, options: ChatSessionOptions) {
    this.agentId = options.agentId;
    this.sessionId = options.sessionId;
    this.branchId = options.branchId ?? 'main';
  }

  dispose(): void {
  }

  async getBranchEvents(): Promise<BranchEvent[]> {
    return this.client.getBranchEvents(this.sessionId, this.branchId);
  }

  async getRuns(): Promise<BranchRun[]> {
    return this.client.getBranchRuns(this.agentId, this.sessionId, this.branchId);
  }

  async getActiveRun(): Promise<BranchRun | null> {
    return this.client.getActiveBranchRun(this.agentId, this.sessionId, this.branchId);
  }

  async getRun(runtimeRunId: string): Promise<BranchRun | null> {
    return this.client.getBranchRun(this.agentId, this.sessionId, this.branchId, runtimeRunId);
  }

  async subscribeLive(options: { signal?: AbortSignal } = {}): Promise<void> {
    await this.client.start({
      agentId: this.agentId,
      sessionId: this.sessionId,
      branchId: this.branchId,
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
      branchId: this.branchId,
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
      branchId: this.branchId,
      eventFlowId: options.eventFlowId ?? undefined,
      reason: options.reason ?? 'Interrupted by client.',
      source: 'User',
    }, { signal: options.signal });
  }

  async refreshSession(): Promise<Session | null> {
    return this.client.getSession(this.sessionId);
  }

  switchSession(sessionId: string, branchId = this.branchId): void {
    this.sessionId = sessionId;
    this.branchId = branchId;
  }
}
