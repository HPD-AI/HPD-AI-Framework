<script lang="ts">
  import type { HpdosState } from "../../core/hpdosState.js";
  import { labelFromPath } from "../../core/hpdosWorkspace.js";
  import type { ViewActions } from "./types.js";

  let { appState, actions }: { appState: HpdosState; actions: ViewActions } = $props();
  let workspaceName = $state("");

  function createWorkspace(event: SubmitEvent) {
    event.preventDefault();
    const name = workspaceName.trim();
    if (!name) return;
    workspaceName = "";
    actions.createWorkspace(name);
  }
</script>

<div class="hpd-sidebar-section" id="projectSummary">
  <div class="hpd-workspace-heading">
    <span class="hpd-section-label">Current Workspace</span>
    <button class="hpd-workspace-manager-trigger" popovertarget="workspace-manager" type="button" aria-label="Manage workspaces">...</button>
  </div>
  {#if !appState.activeWorkspace}
    <div class="hpd-empty">Loading workspace...</div>
  {:else}
    <div class="hpd-workspace-card">
      <div class="hpd-workspace-name">
        <div class="hpd-title-sm">{appState.activeWorkspace.name}</div>
        <div class="hpd-meta">{appState.activeWorkspace.roots.length} {appState.activeWorkspace.roots.length === 1 ? "directory" : "directories"}</div>
      </div>
      <div class="hpd-workspace-roots-header">
        <span class="hpd-section-label">Directories</span>
      </div>
      <div class="hpd-workspace-roots">
        {#each appState.activeWorkspace.roots as root (root.id)}
          <div class="hpd-workspace-root" data-workspace-root={root.id}>
            <div class="min-w-0">
              <div class="hpd-title-sm">{root.label || labelFromPath(root.path)}</div>
              <div class="hpd-path" title={root.path}>{root.path}</div>
            </div>
            <button
              class="hpd-button hpd-icon-button"
              disabled={appState.activeWorkspace.roots.length === 1 || appState.busy}
              aria-label={`Remove ${root.label || labelFromPath(root.path)}`}
              onclick={() => actions.removeWorkspaceRoot(root.id)}
              type="button">
              x
            </button>
          </div>
        {/each}
      </div>
      <button class="hpd-button hpd-button-secondary hpd-workspace-picker" disabled={appState.busy} onclick={() => actions.pickWorkspaceRoots()} type="button">Add Directory</button>
    </div>
  {/if}
  <div class="hpd-workspace-manager" id="workspace-manager" popover>
    <div class="hpd-workspace-manager-header">
      <div>
        <h2 class="hpd-title-sm">Workspaces</h2>
        <p class="hpd-meta">{appState.workspaceStore?.workspaces.length || 0} saved</p>
      </div>
      <button class="hpd-button hpd-icon-button" popovertarget="workspace-manager" popovertargetaction="hide" type="button" aria-label="Close workspace manager">x</button>
    </div>
    <div class="hpd-workspace-manager-list">
      {#each appState.workspaceStore?.workspaces || [] as workspace (workspace.id)}
        <div class="hpd-workspace-manager-row" aria-current={workspace.id === appState.activeWorkspace?.id ? "page" : undefined}>
          <button
            class="hpd-workspace-manager-item"
            disabled={appState.busy}
            onclick={() => actions.switchWorkspace(workspace.id)}
            type="button">
            <span class="hpd-title-sm">{workspace.name}</span>
            <span class="hpd-path" title={workspace.roots[0]?.path || ""}>{workspace.roots[0]?.path || "No directory"}</span>
            <span class="hpd-meta">{workspace.roots.length} {workspace.roots.length === 1 ? "directory" : "directories"}</span>
          </button>
          <button
            class="hpd-workspace-delete"
            disabled={appState.busy || (appState.workspaceStore?.workspaces.length || 0) <= 1}
            aria-label={`Delete ${workspace.name}`}
            onclick={() => actions.deleteWorkspace(workspace.id)}
            type="button">
            x
          </button>
        </div>
      {/each}
    </div>
    <form class="hpd-workspace-manager-form" onsubmit={createWorkspace}>
      <input class="hpd-input" bind:value={workspaceName} autocomplete="off" placeholder="New workspace name" />
      <button class="hpd-button hpd-button-primary" disabled={appState.busy || !workspaceName.trim()} type="submit">Create + Pick Directory</button>
    </form>
  </div>
</div>
