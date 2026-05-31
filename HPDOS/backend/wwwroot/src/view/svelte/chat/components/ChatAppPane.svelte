<script lang="ts">
  import type { ChatLayoutController } from "../controller";
  import type { ChatRuntimeController } from "../runtime/chatRuntime.svelte";
  import { latestAgentPreview } from "../runtime/chatPreview";
  import ChatComposer from "./ChatComposer.svelte";

  type Props = {
    runtime: ChatRuntimeController;
    chat: ChatLayoutController;
  };

  let { runtime, chat }: Props = $props();
  const chatState = $derived(chat.state);

  const preview = $derived(latestAgentPreview(runtime.activeSession?.timeline ?? []));
</script>

<section class="hpd-app-pane" id="hpd-app-host" aria-label="App host">
  <div class="hpd-app-pane-strip"></div>
  <section class="hpd-app-pane-body">
    <div class="hpd-app-pane-scroll">
      <div class="hpd-app-pane-content"></div>
    </div>
    <ChatComposer
      session={runtime.activeSession}
      providerModels={runtime.providerModels}
      placeholder={runtime.activeSession && !runtime.activeSession.workspace
        ? "Choose a workspace session to enable coding tools."
        : runtime.workspace ? "Ask HPD-Agent..." : "Choose a workspace to enable coding tools."}
      previewLabel={preview?.label}
      previewText={preview?.text}
      previewExpandedText={preview?.expandedText}
      streaming={runtime.activeSession?.branchRunning ?? false}
      previewCollapsed={$chatState.chatSectionCollapsed}
      onTogglePreview={() => chat.toggleChatSection()}
    />
  </section>
</section>
