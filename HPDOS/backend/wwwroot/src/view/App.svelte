<script lang="ts">
  import { AgentClient } from "@hpd/hpd-agent-client";
  import type { ArtifactView } from "../core/hpdosArtifacts.js";
  import { HpdosApp } from "../core/hpdosApp.js";
  import { FetchHpdosRuntimeApi } from "../core/hpdosRuntime.js";
  import type { HpdosState } from "../core/hpdosState.js";
  import { onMount } from "svelte";
  import { BrowserDesktopBridge } from "./browserDesktopBridge.js";
  import { BrowserStorage } from "./browserStorage.js";
  import { initializeMarkdown } from "./markdown.js";
  import AppShell from "./svelte/AppShell.svelte";
  import type { SidebarView, ViewActions } from "./svelte/types.js";

  initializeMarkdown();

  const app = new HpdosApp({
    client: new AgentClient({
      baseUrl: "/api/hpd-agent",
      credentials: "include"
    }),
    runtimeApi: new FetchHpdosRuntimeApi(),
    storage: new BrowserStorage(),
    desktopBridge: new BrowserDesktopBridge()
  });

  let appState: HpdosState = $state(app.state);
  let draft = $state("");
  let artifactViews = $state(new Map<string, ArtifactView>());
  let sidebarView: SidebarView = $state(readSidebarView());

  function readSidebarView(): SidebarView {
    return window.sessionStorage.getItem("hpdos.sidebarView.v1") === "conversation"
      ? "conversation"
      : "workspaceSessions";
  }

  function setSidebarView(nextView: SidebarView) {
    sidebarView = nextView;
    window.sessionStorage.setItem("hpdos.sidebarView.v1", nextView);
  }

  const actions: ViewActions = {
    newSession: () => {
      setSidebarView("conversation");
      void app.newSession().catch(() => undefined);
    },
    createWorkspace: (name) => void app.createWorkspace(name).catch(() => undefined),
    deleteWorkspace: (workspaceId) => void app.deleteWorkspace(workspaceId).catch(() => undefined),
    switchWorkspace: (workspaceId) => void app.switchWorkspace(workspaceId).catch(() => undefined),
    switchSession: (sessionId) => {
      setSidebarView("conversation");
      void app.switchSession(sessionId).catch(() => undefined);
    },
    deleteSession: (sessionId) => void app.deleteSession(sessionId).catch(() => undefined),
    pickWorkspaceRoots: () => void app.pickWorkspaceRoots().catch(() => undefined),
    removeWorkspaceRoot: (rootId) => void app.removeWorkspaceRoot(rootId).catch(() => undefined),
    sendText: (command) => {
      setSidebarView("conversation");
      void app.sendText(command).catch(() => undefined);
    },
    setRuntimeOptions: (options) => app.setRuntimeOptions(options),
    openArtifact: (id) => app.openArtifact(id),
    closeArtifact: () => app.closeArtifact(),
    setArtifactView: (id, view) => {
      const next = new Map(artifactViews);
      next.set(id, view);
      artifactViews = next;
    },
    setDraft: (value) => {
      draft = value;
    }
  };

  onMount(() => {
    const subscription = app.subscribe((nextState) => {
      appState = nextState;
      if (
        sidebarView === "conversation" &&
        nextState.runtime &&
        nextState.workspaceStore &&
        !nextState.activeSessionId &&
        !nextState.busy
      ) {
        setSidebarView("workspaceSessions");
      }
    });
    void app.initialize();
    return () => subscription.dispose();
  });
</script>

<AppShell {appState} {actions} {draft} {artifactViews} {sidebarView} {setSidebarView} />
