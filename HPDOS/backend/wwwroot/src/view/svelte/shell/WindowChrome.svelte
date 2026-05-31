<script lang="ts">
  import type { ChatLayoutController } from "../chat/controller";
  import type { ShellController } from "./controller";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
  };

  let { shell, chat }: Props = $props();
  const shellState = $derived(shell.state);
  const chatState = $derived(chat.state);
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

  {#if $shellState.activeRoute === "chat"}
    <button
      class="hpd-window-sidebar-button"
      type="button"
      aria-label={$chatState.chatSectionCollapsed ? "Expand chat section" : "Collapse chat section"}
      aria-controls="chatShell"
      aria-expanded={!$chatState.chatSectionCollapsed}
      title={$chatState.chatSectionCollapsed ? "Expand chat" : "Collapse chat"}
      onclick={() => chat.toggleChatSection()}
    >
      <svg
        class="hpd-window-sidebar-icon hpd-window-chat-collapse-icon"
        aria-hidden="true"
        viewBox="0 0 24 24"
        fill="none"
      >
        <rect x="4" y="5" width="16" height="14" rx="2" />
        <path d="M9 5V19" />
        {#if $chatState.chatSectionCollapsed}
          <path d="M14 9L17 12L14 15" />
        {:else}
          <path d="M16 9L13 12L16 15" />
        {/if}
      </svg>
    </button>
  {/if}
</div>
