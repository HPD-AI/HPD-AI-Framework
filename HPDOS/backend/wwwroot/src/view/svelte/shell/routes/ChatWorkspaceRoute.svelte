<script lang="ts">
  import ChatWorkspace from "../../chat/ChatWorkspace.svelte";
  import type { ChatLayoutController } from "../../chat/controller";
  import type { ShellController } from "../controller";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
  };

  let { shell, chat }: Props = $props();
</script>

<ChatWorkspace {shell} {chat} />
