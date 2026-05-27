<script lang="ts">
  import type { ChatLayoutController } from "../controller";
  import type { ChatLayoutMode } from "../layout";
  import { attachChatResize } from "../resize.svelte";

  type Props = {
    chat: ChatLayoutController;
    mode: ChatLayoutMode;
  };

  let { chat, mode }: Props = $props();
</script>

<!-- svelte-ignore a11y_no_noninteractive_tabindex - focusable separator is the ARIA splitter pattern. -->
<div
  {@attach attachChatResize(chat, () => mode)}
  class="hpd-app-resize-handle"
  role="separator"
  tabindex="0"
  aria-label="Resize app section"
  aria-orientation="vertical"
  aria-controls="mainFrame hpd-app-host"
></div>
