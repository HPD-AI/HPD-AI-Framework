<script lang="ts">
  import ShellLayout from "./svelte/shell/ShellLayout.svelte";
  import WindowChrome from "./svelte/shell/WindowChrome.svelte";
  import { createShellLayoutController } from "./svelte/shell/controller";

  const shell = createShellLayoutController();
</script>

<main class="hpd-app">
  <WindowChrome {shell} />
  <ShellLayout {shell} />
</main>
