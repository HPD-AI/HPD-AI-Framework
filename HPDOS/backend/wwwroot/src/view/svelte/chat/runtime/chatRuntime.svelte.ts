import type { AgentClient, Session } from "@hpd/hpd-agent-client";
import { createHpdAgentClient } from "./agentClient";
import { ChatSessionState } from "./chatSession.svelte";
import { ChatSessionsState } from "./chatSessions.svelte";
import { ProviderModelState } from "./providerModelState.svelte";
import {
  activeWorkspaceFromStore,
  addRootsToWorkspace,
  createWorkspaceFromPaths,
  loadWorkspaceStore,
  pickWorkspaceDirectories,
  removeRootFromWorkspace,
  saveWorkspaceStore,
  setWorkspaceDefaultRoot,
  toWorkspaceDescriptor,
  type HpdosWorkspaceDto,
  type HpdosWorkspaceStoreDto
} from "./workspaceStore";
import {
  createSessionSearch,
  createUnscopedSessionMetadata,
  createUnscopedSessionSearch,
  isUnscopedSessionMetadata,
  readSessionProviderModel,
  type HpdosWorkspaceDescriptor
} from "./workspaceContext";
import { chatErrorMessage } from "./errors";

export type ChatRuntimeControllerOptions = {
  client?: AgentClient;
};

export class ChatRuntimeController {
  readonly client: AgentClient;

  #agentIdPromise: Promise<string> | null = null;
  #providerModelsHydrationPromise: Promise<void> | null = null;
  #selectionVersion = 0;

  workspace = $state<HpdosWorkspaceDescriptor | null>(null);
  agentId = $state<string | null>(null);
  providerModels = $state<ProviderModelState>(new ProviderModelState());
  workspaceStore = $state<HpdosWorkspaceStoreDto | null>(null);
  workspaces = $state<HpdosWorkspaceDescriptor[]>([]);
  workspaceSessions = $state<Record<string, Session[]>>({});
  unscopedSessions = $state<Session[]>([]);
  sessions = $state<ChatSessionsState | null>(null);
  activeSessionId = $state<string | null>(null);
  activeSession = $state<ChatSessionState | null>(null);
  activeSessionLoading = $state(false);
  loading = $state(false);
  error = $state<string | null>(null);

  constructor(options: ChatRuntimeControllerOptions = {}) {
    this.client = options.client ?? createHpdAgentClient();
  }

  async initialize(): Promise<void> {
    this.loading = true;
    this.error = null;
    this.hydrateProviderModelsInBackground();
    void this.ensureAgentId().catch((error) => {
      this.error = chatErrorMessage(error, "Failed to resolve HPD-Agent.");
    });

    try {
      const store = await loadWorkspaceStore();
      this.applyWorkspaceStore(store);
      await this.loadActiveWorkspaceSessions();
      void this.loadSidebarSessions();

      const session = this.sessions?.activeSessionId
        ? this.sessions.sessions.find((item) => item.id === this.sessions?.activeSessionId)
        : null;

      if (session) {
        void this.selectSession(session.id, this.workspace?.id);
      }
    } catch (error) {
      this.error = chatErrorMessage(error, "Failed to load chat sessions.");
    } finally {
      this.loading = false;
    }
  }

  async createSession(): Promise<void> {
    if (!this.workspace || !this.sessions) {
      this.error = "Choose a workspace before starting a coding session.";
      return;
    }

    await this.ensureProviderModelsHydrated();
    const session = await this.sessions.create(this.providerModels.selected);
    this.workspaceSessions = {
      ...this.workspaceSessions,
      [this.sessions.workspace.id]: this.sessions.sessions
    };
    void this.loadUnscopedSessions();
    await this.selectSession(session.id, this.workspace.id);
  }

  async createUnscopedSession(): Promise<void> {
    await this.ensureProviderModelsHydrated();
    const session = await this.client.createSession({
      metadata: createUnscopedSessionMetadata(this.providerModels.selected)
    });

    this.unscopedSessions = orderSessions([
      session,
      ...this.unscopedSessions.filter((item) => item.id !== session.id)
    ]).slice(0, 10);
    await this.selectSession(session.id);
  }

  async selectSession(sessionId: string, workspaceId?: string): Promise<void> {
    if (workspaceId && workspaceId !== this.workspace?.id) {
      await this.switchWorkspace(workspaceId, { selectFirst: false });
    }

    if (workspaceId && (!this.workspace || !this.sessions)) return;

    const version = ++this.#selectionVersion;
    this.activeSessionId = sessionId;
    if (workspaceId) {
      this.sessions?.select(sessionId);
    }
    this.activeSession?.dispose();
    this.activeSession = null;
    this.activeSessionLoading = true;
    this.error = null;

    try {
      const session = this.findKnownSession(sessionId);
      const sessionProviderModel = readSessionProviderModel(session?.metadata);
      if (sessionProviderModel) {
        this.providerModels.useSessionSelection(sessionProviderModel);
      }

      const agentId = await this.ensureAgentId();
      if (version !== this.#selectionVersion) return;

      await this.ensureMainBranch(sessionId);
      if (version !== this.#selectionVersion) return;

      const next = new ChatSessionState({
        client: this.client,
        agentId,
        sessionId,
        branchId: "main",
        workspace: workspaceId ? this.workspace : null
      });

      this.activeSession = next;
      next.attachLiveStream();
      await next.hydrate();
    } catch (error) {
      if (version === this.#selectionVersion) {
        this.error = chatErrorMessage(error, "Failed to load chat session.");
      }
    } finally {
      if (version === this.#selectionVersion) {
        this.activeSessionLoading = false;
      }
    }
  }

  async toggleSessionPinned(session: Session): Promise<void> {
    const pinned = session.metadata?.pinned !== true;
    await this.client.updateSession(session.id, {
      metadata: {
        pinned
      }
    });

    await this.refreshSessionLists();
  }

  async deleteSession(sessionId: string): Promise<void> {
    await this.client.deleteSession(sessionId);

    if (this.activeSession?.chat.sessionId === sessionId) {
      this.activeSession.dispose();
      this.activeSession = null;
    }

    if (this.sessions?.activeSessionId === sessionId) {
      this.sessions.activeSessionId = null;
    }
    if (this.activeSessionId === sessionId) {
      this.activeSessionId = null;
    }

    await this.refreshSessionLists();
  }

  async switchWorkspace(workspaceId: string, options: { selectFirst?: boolean } = {}): Promise<void> {
    if (!this.workspaceStore || this.workspace?.id === workspaceId) return;

    const nextStore = {
      ...this.workspaceStore,
      activeWorkspaceId: workspaceId
    };
    const saved = await saveWorkspaceStore(nextStore);
    this.applyWorkspaceStore(saved);
    await this.loadActiveWorkspaceSessions();
    void this.loadSidebarSessions();

    if (options.selectFirst === false) return;

    const session = this.sessions?.activeSessionId
      ? this.sessions.sessions.find((item) => item.id === this.sessions?.activeSessionId)
      : null;
    if (session) {
      await this.selectSession(session.id, this.workspace?.id);
    } else {
      this.activeSession?.dispose();
      this.activeSession = null;
      this.activeSessionId = null;
    }
  }

  async createWorkspaceFromPicker(): Promise<void> {
    if (!this.workspaceStore) return;

    const workspace = createWorkspaceFromPaths(await pickWorkspaceDirectories());
    if (!workspace) return;

    const nextStore = {
      ...this.workspaceStore,
      activeWorkspaceId: workspace.id,
      workspaces: [workspace, ...this.workspaceStore.workspaces]
    };

    const saved = await saveWorkspaceStore(nextStore);
    this.applyWorkspaceStore(saved);
    await this.loadActiveWorkspaceSessions();
    void this.loadSidebarSessions();
  }

  async addRootsToActiveWorkspaceFromPicker(): Promise<void> {
    if (!this.workspaceStore || !this.workspace) return;

    const paths = await pickWorkspaceDirectories();
    if (paths.length === 0) return;

    await this.updateWorkspace(this.workspace.id, (workspace) => addRootsToWorkspace(workspace, paths));
  }

  async removeRootFromActiveWorkspace(rootId: string): Promise<void> {
    if (!this.workspace) return;
    await this.updateWorkspace(this.workspace.id, (workspace) => removeRootFromWorkspace(workspace, rootId));
  }

  async setActiveWorkspaceDefaultRoot(rootId: string): Promise<void> {
    if (!this.workspace) return;
    await this.updateWorkspace(this.workspace.id, (workspace) => setWorkspaceDefaultRoot(workspace, rootId));
  }

  dispose(): void {
    this.activeSession?.dispose();
  }

  async resolveAgentId(): Promise<string> {
    const agents = await this.client.listAgents();
    const agent = agents[0];
    if (!agent) {
      throw new Error("No HPD-Agent agents are configured.");
    }

    return agent.id;
  }

  async ensureMainBranch(sessionId: string): Promise<void> {
    if (!this.agentId) return;

    const branches = await this.client.listBranches(sessionId);
    if (branches.some((branch) => branch.id === "main")) return;

    await this.client.createBranch(sessionId, {
      agentId: this.agentId,
      branchId: "main",
      name: "Main"
    });
  }

  private async ensureAgentId(): Promise<string> {
    if (this.agentId) return this.agentId;
    this.#agentIdPromise ??= this.resolveAgentId();
    this.agentId = await this.#agentIdPromise;
    return this.agentId;
  }

  private applyWorkspaceStore(store: HpdosWorkspaceStoreDto): void {
    this.workspaceStore = store;
    this.workspaces = store.workspaces.map(toWorkspaceDescriptor);
    this.workspace = activeWorkspaceFromStore(store);
    if (!this.workspace) {
      this.sessions = null;
      this.activeSession?.dispose();
      this.activeSession = null;
      this.activeSessionId = null;
    }
  }

  private async loadActiveWorkspaceSessions(): Promise<void> {
    if (!this.workspace) {
      this.sessions = null;
      return;
    }

    const sessions = new ChatSessionsState({
      client: this.client,
      workspace: this.workspace
    });
    this.sessions = sessions;
    await sessions.load();
    this.workspaceSessions = {
      ...this.workspaceSessions,
      [this.workspace.id]: orderSessions(sessions.sessions)
    };
  }

  private async loadSidebarSessions(): Promise<void> {
    await Promise.all([
      this.loadWorkspaceSessionGroups(),
      this.loadUnscopedSessions()
    ]);
  }

  private async refreshSessionLists(): Promise<void> {
    await this.loadActiveWorkspaceSessions();
    await this.loadSidebarSessions();
  }

  private async loadWorkspaceSessionGroups(): Promise<void> {
    const entries = await Promise.all(this.workspaces.map(async (workspace) => {
      if (workspace.id === this.workspace?.id && this.sessions) {
        return [workspace.id, orderSessions(this.sessions.sessions)] as const;
      }

      const sessions = await this.client.searchSessions(createSessionSearch(workspace, 5));
      return [workspace.id, orderSessions(sessions)] as const;
    }));

    this.workspaceSessions = Object.fromEntries(entries);
  }

  private async loadUnscopedSessions(): Promise<void> {
    const sessions = await this.client.searchSessions(createUnscopedSessionSearch(200));
    this.unscopedSessions = sessions
      .filter((session) => isUnscopedSessionMetadata(session.metadata))
      .sort(compareSessions)
      .slice(0, 10);
  }

  private async updateWorkspace(
    workspaceId: string,
    update: (workspace: HpdosWorkspaceDto) => HpdosWorkspaceDto
  ): Promise<void> {
    if (!this.workspaceStore) return;

    const nextStore = {
      ...this.workspaceStore,
      workspaces: this.workspaceStore.workspaces.map((workspace) => (
        workspace.id === workspaceId ? update(workspace) : workspace
      ))
    };

    const saved = await saveWorkspaceStore(nextStore);
    this.applyWorkspaceStore(saved);
    await this.loadActiveWorkspaceSessions();
    void this.loadSidebarSessions();
  }

  private hydrateProviderModelsInBackground(): void {
    this.#providerModelsHydrationPromise ??= this.providerModels.hydrate().catch((error) => {
      this.providerModels.error = chatErrorMessage(error, "Failed to load provider model preferences.");
    });
    void this.#providerModelsHydrationPromise;
  }

  private async ensureProviderModelsHydrated(): Promise<void> {
    this.hydrateProviderModelsInBackground();
    await this.#providerModelsHydrationPromise;
  }

  private findKnownSession(sessionId: string): Session | undefined {
    if (this.sessions?.sessions) {
      const session = this.sessions.sessions.find((item) => item.id === sessionId);
      if (session) return session;
    }

    for (const sessions of Object.values(this.workspaceSessions)) {
      const session = sessions.find((item) => item.id === sessionId);
      if (session) return session;
    }

    return this.unscopedSessions.find((item) => item.id === sessionId);
  }
}

export function createChatRuntimeController(options: ChatRuntimeControllerOptions = {}): ChatRuntimeController {
  const runtime = new ChatRuntimeController(options);
  void runtime.initialize();
  return runtime;
}

function orderSessions(sessions: readonly Session[]): Session[] {
  return [...sessions].sort(compareSessions);
}

function compareSessions(left: Session, right: Session): number {
  const pinDelta = Number(right.metadata?.pinned === true) - Number(left.metadata?.pinned === true);
  if (pinDelta !== 0) return pinDelta;

  return new Date(right.lastActivity).getTime() - new Date(left.lastActivity).getTime();
}
