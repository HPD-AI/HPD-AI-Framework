import {
  AgentClient,
  createErrorResponse,
  normalizeClientToolName
} from "@hpd/hpd-agent-client";
import type {
  ChatSession,
  ConversationChange,
  ConversationItem,
  EventSubscription,
  Session
} from "@hpd/hpd-agent-client";
import { artifactSystemInstructions, HpdosArtifacts } from "./hpdosArtifacts.js";
import type {
  HpdosDesktopBridge,
  HpdosRuntimeApi,
  HpdosState,
  HpdosStateListener,
  HpdosStorage,
  SendTextCommand
} from "./hpdosState.js";
import {
  activeWorkspace,
  addWorkspaceRoots,
  agentWorkspaceContext,
  createWorkspace,
  deleteWorkspace,
  initializeWorkspaceStore,
  parseSavedWorkspaceStore,
  removeWorkspaceRoot,
  serializeWorkspaceStore,
  sessionMetadata,
  sessionScope,
  sessionWorkspaceId,
  switchWorkspace,
  workspaceSystemInstructions
} from "./hpdosWorkspace.js";
import type { HpdosRuntime, HpdosWorkspace, HpdosWorkspaceStore } from "./hpdosWorkspace.js";
import { selectPreferredSession, titleFromPrompt } from "./hpdosSessions.js";

export interface HpdosAppOptions {
  client: AgentClient;
  runtimeApi: HpdosRuntimeApi;
  storage: HpdosStorage;
  desktopBridge: HpdosDesktopBridge;
  agentId?: string;
  branchId?: string;
  workspaceStorageKey?: string;
  activeSessionStorageKey?: string;
  providerKey?: string;
  modelId?: string;
}

export class HpdosApp {
  readonly artifacts = new HpdosArtifacts();

  private readonly listeners = new Set<HpdosStateListener>();
  private readonly agentId: string;
  private readonly branchId: string;
  private readonly workspaceStorageKey: string;
  private readonly activeSessionStorageKey: string;
  private activeChat: ChatSession | null = null;
  private conversationSubscription: EventSubscription | null = null;

  private stateValue: HpdosState;

  constructor(private readonly options: HpdosAppOptions) {
    this.agentId = options.agentId || "hpdos-agent";
    this.branchId = options.branchId || "main";
    this.workspaceStorageKey = options.workspaceStorageKey || "hpdos.workspaces.v1";
    this.activeSessionStorageKey = options.activeSessionStorageKey || "hpdos.activeSessionsByWorkspace.v1";
    this.stateValue = {
      busy: false,
      runtime: null,
      workspaceStore: null,
      activeWorkspace: null,
      workspaceSessions: [],
      recentSessions: [],
      activeSessionId: "",
      conversationItems: [],
      artifacts: [],
      openArtifactId: null,
      providerKey: options.providerKey || "openrouter",
      modelId: options.modelId || "google/gemini-3.1-flash-lite",
      error: null
    };

    options.client.tools.register("get_active_view", () => this.currentClientContext());
    options.client.tools.registerHarness(this.artifacts.harness, (request) => {
      const response = this.artifacts.handleToolRequest(request)
        || createErrorResponse(request.requestId, `Unknown client tool: ${request.toolName}`);
      this.syncArtifacts();
      return response;
    });
  }

  get state() {
    return this.stateValue;
  }

  subscribe(listener: HpdosStateListener) {
    this.listeners.add(listener);
    listener(this.stateValue);
    return { dispose: () => this.listeners.delete(listener) };
  }

  async initialize() {
    await this.withBusy(async () => {
      await this.ensureRuntimeAndWorkspace();
      const sessions = await this.loadSessionsInternal();
      const selected = selectPreferredSession(sessions.workspaceSessions, this.stateValue.activeSessionId || this.readActiveSessionId());
      this.selectSession(selected);
      if (selected) await this.hydrateSession();
    });
  }

  async refreshSessions() {
    await this.withBusy(async () => {
      const sessions = await this.loadSessionsInternal();
      const selected = selectPreferredSession(sessions.workspaceSessions, this.stateValue.activeSessionId);
      if (selected !== this.stateValue.activeSessionId) this.selectSession(selected);
      if (selected) await this.hydrateSession();
    });
  }

  async newSession() {
    await this.withBusy(async () => {
      const session = await this.createProjectSession();
      this.selectSession(session.id);
      await this.loadSessionsInternal();
    });
  }

  async createWorkspace(name: string) {
    const paths = await this.options.desktopBridge.pickWorkspaceFolders();
    if (!paths?.length) return;
    await this.withBusy(async () => {
      const runtime = await this.ensureRuntime();
      const store = this.requireWorkspaceStore();
      this.setWorkspaceStore(createWorkspace(store, name, paths, runtime));
      await this.loadSessionsInternal();
    });
  }

  async switchWorkspace(workspaceId: string) {
    if (!workspaceId || workspaceId === this.stateValue.activeWorkspace?.id) return;
    await this.withBusy(async () => {
      const store = this.requireWorkspaceStore();
      this.setWorkspaceStore(switchWorkspace(store, workspaceId));
      const sessions = await this.loadSessionsInternal();
      const selected = selectPreferredSession(sessions.workspaceSessions, this.stateValue.activeSessionId);
      this.selectSession(selected);
      if (selected) await this.hydrateSession();
    });
  }

  async deleteWorkspace(workspaceId: string) {
    await this.withBusy(async () => {
      const runtime = await this.ensureRuntime();
      const store = this.requireWorkspaceStore();
      const activeId = this.stateValue.activeWorkspace?.id || "";
      this.setWorkspaceStore(deleteWorkspace(store, workspaceId, runtime));
      const sessions = await this.loadSessionsInternal();
      if (workspaceId === activeId) {
        const selected = selectPreferredSession(sessions.workspaceSessions, this.stateValue.activeSessionId);
        this.selectSession(selected);
        if (selected) await this.hydrateSession();
      }
    });
  }

  async switchSession(sessionId: string) {
    if (!sessionId || sessionId === this.stateValue.activeSessionId) return;
    await this.withBusy(async () => {
      const session = [...this.stateValue.workspaceSessions, ...this.stateValue.recentSessions]
        .find((item) => item.id === sessionId);
      const workspaceId = session ? sessionWorkspaceId(session) : "";
      if (workspaceId && workspaceId !== this.stateValue.activeWorkspace?.id && this.stateValue.workspaceStore) {
        this.setWorkspaceStore(switchWorkspace(this.stateValue.workspaceStore, workspaceId));
      }
      this.selectSession(sessionId);
      await this.hydrateSession();
      await this.loadSessionsInternal();
    });
  }

  async deleteSession(sessionId: string) {
    if (!sessionId) return;
    await this.withBusy(async () => {
      const wasActive = sessionId === this.stateValue.activeSessionId;
      if (wasActive) this.selectSession("");
      await this.options.client.deleteSession(sessionId);
      this.removeActiveSessionReference(sessionId);
      const sessions = await this.loadSessionsInternal();
      if (wasActive) {
        const selected = selectPreferredSession(sessions.workspaceSessions, this.readActiveSessionId());
        this.selectSession(selected);
        if (selected) await this.hydrateSession();
      }
    });
  }

  async addWorkspaceRoot(path: string) {
    await this.addWorkspaceRoots([path]);
  }

  async pickWorkspaceRoots() {
    const paths = await this.options.desktopBridge.pickWorkspaceFolders();
    if (!paths?.length) return;
    await this.addWorkspaceRoots(paths);
  }

  async addWorkspaceRoots(paths: string[]) {
    await this.withBusy(async () => {
      const runtime = await this.ensureRuntime();
      const store = this.requireWorkspaceStore();
      const next = addWorkspaceRoots(store, paths, runtime);
      this.setWorkspaceStore(next);
      await this.loadSessionsInternal();
    });
  }

  async removeWorkspaceRoot(rootId: string) {
    const runtime = await this.ensureRuntime();
    const store = this.requireWorkspaceStore();
    const next = removeWorkspaceRoot(store, rootId, runtime);
    this.setWorkspaceStore(next);
    await this.loadSessionsInternal();
  }

  setRuntimeOptions(options: { providerKey?: string; modelId?: string }) {
    this.patchState({
      providerKey: options.providerKey ?? this.stateValue.providerKey,
      modelId: options.modelId ?? this.stateValue.modelId
    });
  }

  openArtifact(id: string) {
    this.artifacts.open(id);
    this.syncArtifacts();
  }

  closeArtifact() {
    this.artifacts.close();
    this.syncArtifacts();
  }

  async sendText(command: SendTextCommand) {
    const text = command.text.trim();
    if (!text) return;
    const providerKey = (command.providerKey || this.stateValue.providerKey).trim();
    const modelId = (command.modelId || this.stateValue.modelId).trim();
    if (!providerKey || !modelId) throw new Error("Provider and model are required.");

    this.setRuntimeOptions({ providerKey, modelId });
    await this.withBusy(async () => {
      const runtime = await this.ensureRuntime();
      const workspace = this.requireWorkspace();
      const needsTitle = !this.stateValue.activeSessionId;
      await this.ensureActiveSession();
      if (needsTitle) {
        await this.options.client.updateSession(this.stateValue.activeSessionId, {
          metadata: { "hpdos.title": titleFromPrompt(text) }
        });
      }

      const chat = this.setActiveChat(this.stateValue.activeSessionId);
      await chat.sendText(text, {
        runConfig: {
          providerKey,
          modelId,
          additionalSystemInstructions: [
            workspaceSystemInstructions(workspace),
            artifactSystemInstructions
          ].join("\n\n"),
          contextOverrides: {
            workspace: agentWorkspaceContext(workspace)
          },
          clientToolInput: {
            resetClientState: true,
            clientHarnesses: this.options.client.tools.clientHarnesses,
            context: [{
              key: "hpdos.activeView",
              description: "The current HPD-OS shell view.",
              value: this.currentClientContext(runtime, workspace)
            }]
          }
        }
      });
      this.patchState({ conversationItems: chat.conversation.items.slice() });
      await this.loadSessionsInternal();
    });
  }

  private async ensureActiveSession() {
    if (this.stateValue.activeSessionId) return;
    const session = await this.createProjectSession();
    this.selectSession(session.id);
  }

  private async createProjectSession(title = "New session") {
    const runtime = await this.ensureRuntime();
    const workspace = this.requireWorkspace();
    return this.options.client.createSession({ metadata: sessionMetadata(runtime, workspace, title) });
  }

  private async hydrateSession() {
    if (!this.stateValue.activeSessionId) return;
    this.artifacts.clear();
    this.syncArtifacts(false);
    const chat = this.setActiveChat(this.stateValue.activeSessionId);
    await chat.loadHistory();
    this.rebuildArtifactsFromHistory(chat.conversation.items);
    this.patchState({
      conversationItems: chat.conversation.items.slice(),
      artifacts: this.artifacts.all,
      openArtifactId: this.artifacts.openArtifactId
    });
  }

  private setActiveChat(sessionId: string) {
    if (this.activeChat?.sessionId === sessionId) return this.activeChat;
    this.conversationSubscription?.dispose();
    this.activeChat?.dispose();
    this.activeChat = this.options.client.chat.session({ agentId: this.agentId, sessionId, branchId: this.branchId });
    this.conversationSubscription = this.activeChat.conversation.onChange((changes) => this.handleConversationChanges(changes));
    return this.activeChat;
  }

  private handleConversationChanges(changes: ConversationChange[]) {
    for (const change of changes) {
      if (change.type !== "added" && change.type !== "updated") continue;
      this.applyArtifactHistoryItem(change.item);
    }
    this.patchState({
      conversationItems: this.activeChat?.conversation.items.slice() || [],
      artifacts: this.artifacts.all,
      openArtifactId: this.artifacts.openArtifactId
    });
  }

  private rebuildArtifactsFromHistory(items: readonly ConversationItem[]) {
    this.artifacts.clear();
    for (const item of items) this.applyArtifactHistoryItem(item);
  }

  private applyArtifactHistoryItem(item: ConversationItem) {
    if (item.kind !== "tool" || item.source !== "history" || !item.args) return;
    this.artifacts.applyHistoryToolCall(
      normalizeClientToolName(item.name),
      item.args,
      item.timestamp || ""
    );
  }

  private async loadSessionsInternal() {
    const runtime = await this.ensureRuntime();
    const active = this.requireWorkspace();
    const allSessions = await this.options.client.searchSessions({
      metadata: sessionScope(runtime),
      offset: 0,
      limit: 100
    });
    const workspaceSessions = allSessions.filter((session) => sessionWorkspaceId(session) === active.id);
    const recentSessions = allSessions.filter((session) => sessionWorkspaceId(session) !== active.id);
    this.patchState({ workspaceSessions, recentSessions });
    return { workspaceSessions, recentSessions, allSessions };
  }

  private selectSession(sessionId: string) {
    this.conversationSubscription?.dispose();
    this.conversationSubscription = null;
    this.activeChat?.dispose();
    this.activeChat = null;
    this.artifacts.clear();
    this.writeActiveSessionId(sessionId);
    this.patchState({
      activeSessionId: sessionId,
      conversationItems: [],
      artifacts: [],
      openArtifactId: null
    });
  }

  private async ensureRuntimeAndWorkspace() {
    const runtime = await this.ensureRuntime();
    if (!this.stateValue.workspaceStore) {
      const store = initializeWorkspaceStore(runtime, parseSavedWorkspaceStore(this.options.storage.get(this.workspaceStorageKey)));
      this.setWorkspaceStore(store);
    }
  }

  private async ensureRuntime() {
    if (this.stateValue.runtime) return this.stateValue.runtime;
    const runtime = await this.options.runtimeApi.loadRuntime();
    this.patchState({ runtime });
    if (!this.stateValue.workspaceStore) {
      const store = initializeWorkspaceStore(runtime, parseSavedWorkspaceStore(this.options.storage.get(this.workspaceStorageKey)));
      this.setWorkspaceStore(store);
    }
    return runtime;
  }

  private requireWorkspace() {
    const workspace = this.stateValue.activeWorkspace;
    if (!workspace) throw new Error("HPDOS workspace is not initialized.");
    return workspace;
  }

  private requireWorkspaceStore() {
    const store = this.stateValue.workspaceStore;
    if (!store) throw new Error("HPDOS workspace store is not initialized.");
    return store;
  }

  private setWorkspaceStore(workspaceStore: HpdosWorkspaceStore) {
    const workspace = activeWorkspace(workspaceStore);
    this.options.storage.set(this.workspaceStorageKey, serializeWorkspaceStore(workspaceStore));
    this.patchState({
      workspaceStore,
      activeWorkspace: workspace,
      activeSessionId: workspace ? this.readActiveSessionId(workspace.id) : ""
    });
  }

  private currentClientContext(runtime = this.stateValue.runtime, workspace: HpdosWorkspace | null = this.stateValue.activeWorkspace) {
    return {
      activeView: "chat",
      sessionId: this.stateValue.activeSessionId || undefined,
      project: runtime?.project,
      workspace,
      ...this.artifacts.context
    };
  }

  private syncArtifacts(shouldNotify = true) {
    this.stateValue = {
      ...this.stateValue,
      artifacts: this.artifacts.all,
      openArtifactId: this.artifacts.openArtifactId
    };
    if (shouldNotify) this.notify();
  }

  private async withBusy(work: () => Promise<void>) {
    this.patchState({ busy: true, error: null });
    try {
      await work();
    } catch (error) {
      this.patchState({ error: messageOf(error) });
      throw error;
    } finally {
      this.patchState({ busy: false });
    }
  }

  private patchState(patch: Partial<HpdosState>) {
    this.stateValue = { ...this.stateValue, ...patch };
    this.notify();
  }

  private notify() {
    for (const listener of this.listeners) listener(this.stateValue);
  }

  private readActiveSessionId(workspaceId = this.stateValue.activeWorkspace?.id || "") {
    if (!workspaceId) return "";
    return this.readActiveSessionsByWorkspace()[workspaceId] || "";
  }

  private writeActiveSessionId(sessionId: string) {
    const workspaceId = this.stateValue.activeWorkspace?.id;
    if (!workspaceId) return;
    const sessionsByWorkspace = this.readActiveSessionsByWorkspace();
    if (sessionId) sessionsByWorkspace[workspaceId] = sessionId;
    else delete sessionsByWorkspace[workspaceId];
    this.options.storage.set(this.activeSessionStorageKey, JSON.stringify(sessionsByWorkspace));
  }

  private readActiveSessionsByWorkspace() {
    try {
      const parsed = JSON.parse(this.options.storage.get(this.activeSessionStorageKey) || "{}") as unknown;
      return parsed && typeof parsed === "object" ? parsed as Record<string, string> : {};
    } catch {
      return {};
    }
  }

  private removeActiveSessionReference(sessionId: string) {
    const sessionsByWorkspace = this.readActiveSessionsByWorkspace();
    let changed = false;
    for (const [workspaceId, activeSessionId] of Object.entries(sessionsByWorkspace)) {
      if (activeSessionId !== sessionId) continue;
      delete sessionsByWorkspace[workspaceId];
      changed = true;
    }
    if (changed) this.options.storage.set(this.activeSessionStorageKey, JSON.stringify(sessionsByWorkspace));
  }
}

function messageOf(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}
