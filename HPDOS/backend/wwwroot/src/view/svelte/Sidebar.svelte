<script lang="ts">
  import type { ArtifactView } from "../../core/hpdosArtifacts.js";
  import type { HpdosState } from "../../core/hpdosState.js";
  import ConversationRailView from "./ConversationRailView.svelte";
  import type { SidebarView, ViewActions } from "./types.js";
  import WorkspaceSessionsView from "./WorkspaceSessionsView.svelte";

  let {
    appState,
    actions,
    artifactViews,
    sidebarView,
    setSidebarView
  }: {
    appState: HpdosState;
    actions: ViewActions;
    artifactViews: ReadonlyMap<string, ArtifactView>;
    sidebarView: SidebarView;
    setSidebarView(view: SidebarView): void;
  } = $props();
</script>

<aside class="hpd-panel hpd-sidebar">
  <div class="hpd-sidebar-header">
    <div class="hpd-sidebar-title">
      <span class="hpd-section-label">HPD-OS</span>
      <h1 class="hpd-title">Workspace Chat</h1>
    </div>
  </div>
  {#if sidebarView === "conversation"}
    <ConversationRailView {appState} {actions} {artifactViews} showWorkspaceSessions={() => setSidebarView("workspaceSessions")} />
  {:else}
    <WorkspaceSessionsView {appState} {actions} />
  {/if}
</aside>
