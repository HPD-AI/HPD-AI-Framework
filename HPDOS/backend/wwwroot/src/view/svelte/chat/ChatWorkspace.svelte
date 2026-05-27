<script lang="ts">
  import type { ShellController } from "../shell/controller";
  import type { ChatLayoutController } from "./controller";
  import { chatLayoutMode } from "./layout";
  import ChatAppPane from "./components/ChatAppPane.svelte";
  import ChatResizeHandle from "./components/ChatResizeHandle.svelte";
  import ChatWorkspacePane from "./components/ChatWorkspacePane.svelte";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
  };

  let { shell, chat }: Props = $props();

  const shellState = $derived(shell.state);
  const mode = $derived(chatLayoutMode($shellState.sidebarCollapsed));
</script>

<section
  class="hpd-chat-route"
  id="chatShell"
  data-layout-mode={mode}
  aria-label="Chat workspace"
>
  <ChatWorkspacePane />
  <section class="hpd-app-slot" aria-label="App section">
    <ChatResizeHandle {chat} {mode} />
    <ChatAppPane />
  </section>
</section>
