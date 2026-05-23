import type { AgentClient, EventSubscription } from './client.js';
import { ConversationState } from './conversation.js';
import { EventTypes } from './types/events.js';
import type { RunConfig } from './types/run-config.js';
import type { CreateSessionRequest, SearchSessionsRequest, Session } from './types/session.js';

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
 * ChatSession wires runtime events into ConversationState for transcript rendering,
 * while the underlying AgentClient remains the place to handle non-transcript
 * protocol events with on/onAny.
 */
export class ChatSession {
  readonly conversation = new ConversationState();
  private readonly subscriptions: EventSubscription[] = [];

  readonly agentId: string;
  sessionId: string;
  branchId: string;

  constructor(private readonly client: AgentClient, options: ChatSessionOptions) {
    this.agentId = options.agentId;
    this.sessionId = options.sessionId;
    this.branchId = options.branchId ?? 'main';

    this.subscriptions.push(
      this.client.onAny((event) => {
        this.conversation.applyEvent(event);
      }),
      this.client.onError((error) => {
        this.conversation.applyEvent({
          type: EventTypes.MESSAGE_TURN_ERROR,
          message: error.message,
        });
      }),
    );
  }

  dispose(): void {
    for (const subscription of this.subscriptions) subscription.dispose();
    this.subscriptions.length = 0;
  }

  async loadHistory(): Promise<void> {
    this.conversation.reset();
    const messages = await this.client.getBranchMessages(this.sessionId, this.branchId);
    this.conversation.applyBranchMessages(messages);
  }

  async sendText(text: string, options: SendTextOptions = {}): Promise<void> {
    if (options.optimisticUserMessage !== false) this.conversation.addUserText(text);
    await this.client.run({
      type: EventTypes.USER_TEXT_INPUT,
      agentId: this.agentId,
      sessionId: this.sessionId,
      branchId: this.branchId,
      text,
      runConfig: options.runConfig,
    }, { signal: options.signal });
  }

  async refreshSession(): Promise<Session | null> {
    return this.client.getSession(this.sessionId);
  }

  switchSession(sessionId: string, branchId = this.branchId): void {
    this.sessionId = sessionId;
    this.branchId = branchId;
    this.conversation.reset();
  }
}
