<script lang="ts">
  import type { ChatLayoutController } from "../chat/controller";
  import type { ChatRuntimeController } from "../chat/runtime/chatRuntime.svelte";
  import type { ShellController } from "./controller";
  import ShellRouteHost from "./ShellRouteHost.svelte";
  import ShellSidebar from "./ShellSidebar.svelte";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
    chatRuntime: ChatRuntimeController;
  };

  let { shell, chat, chatRuntime }: Props = $props();

  const shellState = $derived(shell.state);
</script>

<div class="hpd-view" id="view">
  <section
    class="hpd-shell-frame"
    data-sidebar-collapsed={$shellState.sidebarCollapsed ? "true" : "false"}
    data-hydrated={$shellState.hydrated ? "true" : "false"}
  >
    <ShellSidebar {shell} {chatRuntime} />
    <ShellRouteHost {shell} {chat} {chatRuntime} />
  </section>
</div>
