<script lang="ts">
  import type { ChatLayoutController } from "../chat/controller";
  import type { ShellController } from "./controller";
  import ShellRouteHost from "./ShellRouteHost.svelte";
  import ShellSidebar from "./ShellSidebar.svelte";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
  };

  let { shell, chat }: Props = $props();

  const shellState = $derived(shell.state);
</script>

<div class="hpd-view" id="view">
  <section
    class="hpd-shell-frame"
    data-sidebar-collapsed={$shellState.sidebarCollapsed ? "true" : "false"}
    data-hydrated={$shellState.hydrated ? "true" : "false"}
  >
    <ShellSidebar {shell} />
    <ShellRouteHost {shell} {chat} />
  </section>
</div>
