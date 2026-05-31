<script lang="ts">
  import ChatTimeline from "./ChatTimeline.svelte";
  import type { ChatRuntimeController } from "../runtime/chatRuntime.svelte";

  type Props = {
    runtime: ChatRuntimeController;
  };

  let { runtime }: Props = $props();
</script>

<section class="hpd-workspace-pane" id="mainFrame" aria-label="Chat">
  <div class="hpd-workspace-strip" aria-label="Chat status">
  </div>
  <section class="hpd-workspace-chat-surface">
    {#if runtime.error}
      <p class="hpd-chat-pane-error">{runtime.error}</p>
    {:else}
      <ChatTimeline
        items={runtime.activeSession?.timeline ?? []}
        streaming={runtime.activeSession?.branchRunning ?? false}
        emptyText={runtime.workspace ? "Start a new run from this workspace." : "Choose a workspace to enable coding tools."}
      />
    {/if}
  </section>
</section>
