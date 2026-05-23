<script lang="ts">
  import type { ArtifactView } from "../../core/hpdosArtifacts.js";
  import type { HpdosState } from "../../core/hpdosState.js";
  import { sessionTitle } from "../../core/hpdosSessions.js";
  import { tick } from "svelte";
  import ConversationView from "./ConversationView.svelte";
  import type { ViewActions } from "./types.js";

  let {
    appState,
    actions,
    artifactViews,
    showWorkspaceSessions
  }: {
    appState: HpdosState;
    actions: ViewActions;
    artifactViews: ReadonlyMap<string, ArtifactView>;
    showWorkspaceSessions(): void;
  } = $props();

  let rail: HTMLDivElement | null = $state(null);
  let itemCount = $derived(appState.conversationItems.length);
  let activeSession = $derived(
    [...appState.workspaceSessions, ...appState.recentSessions].find((session) => session.id === appState.activeSessionId)
  );

  $effect(() => {
    itemCount;
    if (!rail) return;
    void tick().then(() => rail?.scrollTo({ top: rail.scrollHeight, behavior: "smooth" }));
  });
</script>

<div class="hpd-sidebar-route hpd-conversation-rail" data-route="conversation">
  <div class="hpd-conversation-rail-header">
    <button class="hpd-button hpd-button-secondary hpd-conversation-back" onclick={showWorkspaceSessions} type="button" aria-label="Show sessions">&lt;- Sessions</button>
    <div class="min-w-0">
      <div class="hpd-title-sm">{activeSession ? sessionTitle(activeSession) : "Conversation"}</div>
      <div class="hpd-meta">{appState.conversationItems.length} events</div>
    </div>
    <button
      class="hpd-button hpd-button-primary hpd-conversation-new"
      disabled={appState.busy}
      onclick={() => actions.newSession()}
      type="button"
      aria-label="New session">
      +
    </button>
  </div>
  <div class="hpd-conversation-rail-scroll" bind:this={rail}>
    {#if !appState.activeSessionId && !appState.conversationItems.length}
      <div class="hpd-empty">Start a session from Workspace Sessions.</div>
    {:else}
      <ConversationView {appState} {actions} {artifactViews} showArtifacts={false} />
    {/if}
  </div>
</div>
