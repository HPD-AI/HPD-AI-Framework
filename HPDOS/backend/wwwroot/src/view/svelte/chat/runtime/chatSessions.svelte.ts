import type { AgentClient, Session } from "@hpd/hpd-agent-client";
import {
  createSessionMetadata,
  createSessionSearch,
  type HpdosSessionProviderModel,
  type HpdosWorkspaceDescriptor
} from "./workspaceContext";

export type ChatSessionsStateOptions = {
  client: AgentClient;
  workspace: HpdosWorkspaceDescriptor;
};

export class ChatSessionsState {
  readonly client: AgentClient;
  readonly workspace: HpdosWorkspaceDescriptor;

  sessions = $state<Session[]>([]);
  activeSessionId = $state<string | null>(null);
  loading = $state(false);
  error = $state<string | null>(null);

  constructor(options: ChatSessionsStateOptions) {
    this.client = options.client;
    this.workspace = options.workspace;
  }

  async load(): Promise<void> {
    this.loading = true;
    this.error = null;

    try {
      this.sessions = orderSessions(await this.client.searchSessions(createSessionSearch(this.workspace)));
      this.activeSessionId ??= this.sessions[0]?.id ?? null;
    } catch (error) {
      this.error = error instanceof Error ? error.message : "Failed to load chat sessions.";
      throw error;
    } finally {
      this.loading = false;
    }
  }

  async create(providerModel?: HpdosSessionProviderModel): Promise<Session> {
    this.loading = true;
    this.error = null;

    try {
      const session = await this.client.createSession({
        metadata: createSessionMetadata(this.workspace, providerModel)
      });
      this.sessions = orderSessions([session, ...this.sessions.filter((item) => item.id !== session.id)]);
      this.activeSessionId = session.id;
      return session;
    } catch (error) {
      this.error = error instanceof Error ? error.message : "Failed to create chat session.";
      throw error;
    } finally {
      this.loading = false;
    }
  }

  select(sessionId: string): void {
    this.activeSessionId = sessionId;
  }
}

function orderSessions(sessions: readonly Session[]): Session[] {
  return [...sessions].sort((left, right) => {
    const pinDelta = Number(right.metadata?.pinned === true) - Number(left.metadata?.pinned === true);
    if (pinDelta !== 0) return pinDelta;

    return new Date(right.lastActivity).getTime() - new Date(left.lastActivity).getTime();
  });
}
