<script lang="ts">
  import ShellFrame from "./svelte/shell/ShellFrame.svelte";
  import WindowChrome from "./svelte/shell/WindowChrome.svelte";
  import { createChatLayoutController } from "./svelte/chat/controller";
  import { createShellController } from "./svelte/shell/controller";

  const shell = createShellController();
  const chat = createChatLayoutController();
</script>

<main class="hpd-app">
  <WindowChrome {shell} />
  <ShellFrame {shell} {chat} />
</main>
