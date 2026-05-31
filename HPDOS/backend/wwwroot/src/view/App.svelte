<script lang="ts">
  import ShellFrame from "./svelte/shell/ShellFrame.svelte";
  import WindowChrome from "./svelte/shell/WindowChrome.svelte";
  import { createChatLayoutController } from "./svelte/chat/controller";
  import { createChatRuntimeController } from "./svelte/chat/runtime/chatRuntime.svelte";
  import { createShellController } from "./svelte/shell/controller";

  const shell = createShellController();
  const chat = createChatLayoutController();
  const chatRuntime = createChatRuntimeController();
</script>

<main class="hpd-app">
  <WindowChrome {shell} {chat} />
  <ShellFrame {shell} {chat} {chatRuntime} />
</main>
