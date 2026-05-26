<script lang="ts">
  import type { ShellLayoutController } from "./controller";

  type Props = {
    shell: ShellLayoutController;
  };

  let { shell }: Props = $props();
  const shellState = $derived(shell.state);
</script>

<div class="hpd-window-chrome" aria-label="Window controls">
  <button
    class="hpd-window-sidebar-button"
    type="button"
    aria-label="Toggle sidebar"
    aria-controls="hpd-shell-sidebar"
    aria-expanded={!$shellState.sidebarCollapsed}
    title={$shellState.sidebarCollapsed ? "Show sidebar" : "Hide sidebar"}
    onclick={() => shell.toggleSidebar()}
  >
    <svg class="hpd-window-sidebar-icon" aria-hidden="true" viewBox="0 0 24 24" fill="none">
      <rect x="4" y="5" width="16" height="14" rx="2" />
      <path d="M9 5V19" />
    </svg>
  </button>
</div>
