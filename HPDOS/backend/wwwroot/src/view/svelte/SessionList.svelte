<script lang="ts">
  import type { HpdosState } from "../../core/hpdosState.js";
  import { sessionTitle } from "../../core/hpdosSessions.js";
  import { formatDate } from "../../shared/format.js";
  import type { ViewActions } from "./types.js";

  let { appState, actions }: { appState: HpdosState; actions: ViewActions } = $props();
</script>

<div class="hpd-session-scroll">
  <div class="hpd-session-sections" id="sessionList">
    <section class="hpd-session-section">
      <span class="hpd-section-label">Workspace Sessions</span>
      <div class="hpd-session-list">
        {#if !appState.workspaceSessions.length}
          <div class="hpd-empty">No sessions in this workspace.</div>
        {:else}
          {#each appState.workspaceSessions as session (session.id)}
            <div class="hpd-session-row" aria-current={session.id === appState.activeSessionId ? "page" : undefined}>
              <button
                class="hpd-session"
                disabled={appState.busy}
                onclick={() => actions.switchSession(session.id)}
                type="button">
                <span class="hpd-title-sm">{sessionTitle(session)}</span>
                <span class="hpd-meta">{formatDate(session.lastActivity)}</span>
              </button>
              <button
                class="hpd-session-delete"
                disabled={appState.busy}
                aria-label={`Delete ${sessionTitle(session)}`}
                onclick={() => actions.deleteSession(session.id)}
                type="button">
                x
              </button>
            </div>
          {/each}
        {/if}
      </div>
    </section>

    {#if appState.recentSessions.length}
      <section class="hpd-session-section">
        <span class="hpd-section-label">Recent Sessions</span>
        <div class="hpd-session-list">
          {#each appState.recentSessions as session (session.id)}
            <div class="hpd-session-row" aria-current={session.id === appState.activeSessionId ? "page" : undefined}>
              <button
                class="hpd-session"
                disabled={appState.busy}
                onclick={() => actions.switchSession(session.id)}
                type="button">
                <span class="hpd-title-sm">{sessionTitle(session)}</span>
                <span class="hpd-meta">{formatDate(session.lastActivity)}</span>
              </button>
              <button
                class="hpd-session-delete"
                disabled={appState.busy}
                aria-label={`Delete ${sessionTitle(session)}`}
                onclick={() => actions.deleteSession(session.id)}
                type="button">
                x
              </button>
            </div>
          {/each}
        </div>
      </section>
    {/if}
    {#if !appState.workspaceSessions.length && !appState.recentSessions.length}
      <div class="hpd-empty">No sessions yet.</div>
    {/if}
  </div>
</div>
