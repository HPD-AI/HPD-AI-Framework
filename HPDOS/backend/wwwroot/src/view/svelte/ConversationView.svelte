<script lang="ts">
  import { normalizeClientToolName } from "@hpd/hpd-agent-client";
  import type {
    ConversationItem,
    ConversationMessageItem,
    ConversationReasoningItem,
    ConversationToolItem
  } from "@hpd/hpd-agent-client";
  import type { ArtifactView } from "../../core/hpdosArtifacts.js";
  import { isArtifactToolName } from "../../core/hpdosArtifacts.js";
  import type { HpdosState } from "../../core/hpdosState.js";
  import { markdownHtml } from "../markdown.js";
  import ArtifactCard from "./ArtifactCard.svelte";
  import type { ViewActions } from "./types.js";

  let {
    appState,
    actions,
    artifactViews,
    showArtifacts = true
  }: {
    appState: HpdosState;
    actions: ViewActions;
    artifactViews: ReadonlyMap<string, ArtifactView>;
    showArtifacts?: boolean;
  } = $props();

  function messageRole(item: ConversationMessageItem) {
    return item.role === "user" ? "user" : "assistant";
  }

  function shouldSkipMessage(item: ConversationMessageItem) {
    return !item.text.trim() && item.role === "assistant" && item.source === "event";
  }

  function shouldSkipReasoning(item: ConversationReasoningItem) {
    return !item.text.trim() && item.source === "event";
  }

  function shouldSkipTool(item: ConversationToolItem) {
    const name = normalizeClientToolName(item.name);
    return (item.source === "history" && item.args && isArtifactToolName(name))
      || (name === "tool" && !item.args && !item.argsJson && !item.result);
  }

  function cleanName(value: unknown) {
    return String(value || "unknown").split(".").pop()?.replace(/^tool_/, "").replace(/_[A-Za-z0-9-]{8,}$/, "") || "tool";
  }

  function toolResultText(result: unknown) {
    if (result && typeof result === "object" && "text" in result) return String((result as { text?: unknown }).text || "");
    return JSON.stringify(result || {}, null, 2);
  }

  function toolBlockText(label: string, value: unknown) {
    return `${label}\n${String(value || "").slice(0, 12000)}`;
  }
</script>

<div class="hpd-chat-stack" id="chatStack">
  {#each appState.conversationItems as item (item.id)}
    {#if item.kind === "message" && !shouldSkipMessage(item)}
      {@const role = messageRole(item)}
      <article class="hpd-row" data-role={role}>
        <div class={`hpd-message ${role === "assistant" ? "message-markdown" : ""}`} data-role={role}>
          {#if role === "user"}
            {item.text}
          {:else}
            {@html markdownHtml(item.text)}
          {/if}
        </div>
      </article>
    {:else if item.kind === "reasoning" && !shouldSkipReasoning(item)}
      <details class="hpd-reasoning" data-status={item.status}>
        <summary class="hpd-disclosure-summary">Reasoning {item.status}</summary>
        <pre class="hpd-code-box max-h-72 rounded-none border-t border-hpd-line">{item.text}</pre>
      </details>
    {:else if item.kind === "tool" && !shouldSkipTool(item)}
      {@const name = normalizeClientToolName(item.name)}
      <details class="hpd-tool" data-status={item.status} data-tool={name}>
        <summary class="hpd-disclosure-summary">{cleanName(item.name)} {item.status}</summary>
        <div class="hpd-tool-body">
          {#if item.argsJson || item.args}
            <pre class="hpd-code-box max-h-56">{toolBlockText("Args", item.argsJson || JSON.stringify(item.args, null, 2))}</pre>
          {/if}
          {#if item.result}
            <pre class="hpd-code-box max-h-56">{toolBlockText("Result", toolResultText(item.result))}</pre>
          {/if}
        </div>
      </details>
    {:else if item.kind === "error"}
      <article class="hpd-row" data-kind="error">
        <div class="hpd-error-message">
          {item.message}
        </div>
      </article>
    {/if}
  {/each}

  {#if showArtifacts}
    {#each appState.artifacts as artifact (artifact.id)}
      <article class="hpd-row" data-kind="artifact">
        <ArtifactCard
          {artifact}
          view={artifactViews.get(artifact.id) || "preview"}
          open={appState.openArtifactId === artifact.id}
          {actions} />
      </article>
    {/each}
  {/if}
</div>
