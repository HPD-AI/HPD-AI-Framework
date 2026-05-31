<script lang="ts">
  import ChatWorkspace from "../../chat/ChatWorkspace.svelte";
  import type { ChatLayoutController } from "../../chat/controller";
  import type { ChatRuntimeController } from "../../chat/runtime/chatRuntime.svelte";
  import type { ShellController } from "../controller";

  type Props = {
    shell: ShellController;
    chat: ChatLayoutController;
    chatRuntime: ChatRuntimeController;
  };

  let { shell, chat, chatRuntime }: Props = $props();
</script>

<ChatWorkspace {shell} {chat} {chatRuntime} />
