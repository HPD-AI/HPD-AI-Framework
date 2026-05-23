<script lang="ts">
  import type { HpdosState } from "../../core/hpdosState.js";
  import { sessionTitle } from "../../core/hpdosSessions.js";
  import { formatDate } from "../../shared/format.js";
  import type { ViewActions } from "./types.js";

  let { appState, actions }: { appState: HpdosState; actions: ViewActions } = $props();

  let workspaceName = $derived(appState.activeWorkspace?.name || "Workspace");
  let directoryCount = $derived(appState.activeWorkspace?.roots.length || 0);
  let workspaceCount = $derived(appState.workspaceStore?.workspaces.length || 0);
  let sessionCount = $derived(appState.workspaceSessions.length);
  let recentSessions = $derived(appState.recentSessions.slice(0, 3));
  let primaryRoot = $derived(appState.activeWorkspace?.roots[0]);
</script>

<section class="hpd-dashboard" aria-label="Workspace dashboard">
  <div class="hpd-dashboard-hero">
    <div class="hpd-dashboard-title">
      <span class="hpd-section-label">Dashboard</span>
      <h1>{workspaceName}</h1>
      {#if primaryRoot}
        <p class="hpd-path">{primaryRoot.path}</p>
      {/if}
    </div>
    <div class="hpd-dashboard-actions">
      <button class="hpd-button hpd-button-primary" disabled={appState.busy} onclick={actions.newSession} type="button">
        New Session
      </button>
      <button class="hpd-button hpd-button-secondary" disabled={appState.busy} onclick={actions.pickWorkspaceRoots} type="button">
        Add Directory
      </button>
    </div>
  </div>

  <div class="hpd-dashboard-grid">
    <article class="hpd-dashboard-stat">
      <span class="hpd-meta">Workspace sessions</span>
      <strong>{sessionCount}</strong>
    </article>
    <article class="hpd-dashboard-stat">
      <span class="hpd-meta">Directories</span>
      <strong>{directoryCount}</strong>
    </article>
    <article class="hpd-dashboard-stat">
      <span class="hpd-meta">Saved workspaces</span>
      <strong>{workspaceCount}</strong>
    </article>
    <article class="hpd-dashboard-stat">
      <span class="hpd-meta">Runtime</span>
      <strong>{appState.runtime?.service || "Local"}</strong>
    </article>
  </div>

  <div class="hpd-dashboard-columns">
    <section class="hpd-dashboard-panel">
      <div class="hpd-dashboard-panel-header">
        <h2 class="hpd-title-sm">Directories</h2>
        <span class="hpd-meta">{directoryCount} active</span>
      </div>
      <div class="hpd-dashboard-list">
        {#each appState.activeWorkspace?.roots || [] as root (root.id)}
          <div class="hpd-dashboard-list-item">
            <span class="hpd-title-sm">{root.label}</span>
            <span class="hpd-path">{root.path}</span>
          </div>
        {:else}
          <div class="hpd-empty">No directories yet.</div>
        {/each}
      </div>
    </section>

    <section class="hpd-dashboard-panel">
      <div class="hpd-dashboard-panel-header">
        <h2 class="hpd-title-sm">Recent</h2>
        <span class="hpd-meta">{recentSessions.length} sessions</span>
      </div>
      <div class="hpd-dashboard-list">
        {#each recentSessions as session (session.id)}
          <button class="hpd-dashboard-list-item" disabled={appState.busy} onclick={() => actions.switchSession(session.id)} type="button">
            <span class="hpd-title-sm">{sessionTitle(session)}</span>
            <span class="hpd-meta">{formatDate(session.lastActivity)}</span>
          </button>
        {:else}
          <div class="hpd-empty">No recent sessions.</div>
        {/each}
      </div>
    </section>
  </div>

  <section class="hpd-dashboard-panel">
    <div class="hpd-dashboard-panel-header">
      <h2 class="hpd-title-sm">Activity</h2>
      <span class="hpd-meta">Stub</span>
    </div>
    <div class="hpd-dashboard-timeline">
      <div class="hpd-dashboard-timeline-item">
        <span class="hpd-dashboard-dot"></span>
        <span>Workspace indexed</span>
      </div>
      <div class="hpd-dashboard-timeline-item">
        <span class="hpd-dashboard-dot"></span>
        <span>3 files touched recently</span>
      </div>
      <div class="hpd-dashboard-timeline-item">
        <span class="hpd-dashboard-dot"></span>
        <span>Agent runtime ready</span>
      </div>
    </div>
  </section>
</section>
