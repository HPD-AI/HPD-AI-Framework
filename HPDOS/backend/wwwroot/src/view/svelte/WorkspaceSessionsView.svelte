<script lang="ts">
  import type { HpdosState } from "../../core/hpdosState.js";
  import SessionList from "./SessionList.svelte";
  import type { ViewActions } from "./types.js";
  import WorkspacePanel from "./WorkspacePanel.svelte";

  let { appState, actions }: { appState: HpdosState; actions: ViewActions } = $props();
</script>

<div class="hpd-sidebar-route" data-route="workspace-sessions">
  <WorkspacePanel {appState} {actions} />
  <div class="hpd-sidebar-actions">
    <button class="hpd-button hpd-button-primary" disabled={appState.busy} onclick={() => actions.newSession()} type="button">New Session</button>
  </div>
  <SessionList {appState} {actions} />
</div>
