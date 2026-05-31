<script lang="ts">
  import type { ShellController } from "../shell/controller";
  import type { ChatLayoutController } from "./controller";
  import type { ChatRuntimeController } from "./runtime/chatRuntime.svelte";
  import { chatLayoutMode } from "./layout";
  import ChatAppPane from "./components/ChatAppPane.svelte";
  import ChatResizeHandle from "./components/ChatResizeHandle.svelte";
  import ChatWorkspacePane from "./components/ChatWorkspacePane.svelte";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
    chatRuntime: ChatRuntimeController;
  };

  let { shell, chat, chatRuntime }: Props = $props();

  const shellState = $derived(shell.state);
  const chatState = $derived(chat.state);
  const mode = $derived(chatLayoutMode($shellState.sidebarCollapsed));
</script>

<section
  class="hpd-chat-route"
  id="chatShell"
  data-layout-mode={mode}
  data-chat-collapsed={$chatState.chatSectionCollapsed ? "true" : "false"}
  aria-label="Chat workspace"
>
  <ChatWorkspacePane runtime={chatRuntime} />
  <section class="hpd-app-slot" aria-label="App section">
    <ChatResizeHandle {chat} {mode} />
    <ChatAppPane runtime={chatRuntime} {chat} />
  </section>
</section>
