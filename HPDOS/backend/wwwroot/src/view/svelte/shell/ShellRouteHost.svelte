<script lang="ts">
  import type { ChatLayoutController } from "../chat/controller";
  import type { ShellController } from "./controller";
  import AutomationsRoute from "./routes/AutomationsRoute.svelte";
  import ChatWorkspaceRoute from "./routes/ChatWorkspaceRoute.svelte";
  import SettingsRoute from "./routes/SettingsRoute.svelte";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
  };

  let { shell, chat }: Props = $props();

  const shellState = $derived(shell.state);
</script>

<section class="hpd-shell-route-host" aria-label="Main route">
  {#if $shellState.activeRoute === "chat"}
    <ChatWorkspaceRoute {shell} {chat} />
  {:else if $shellState.activeRoute === "automations"}
    <AutomationsRoute />
  {:else}
    <SettingsRoute />
  {/if}
</section>
