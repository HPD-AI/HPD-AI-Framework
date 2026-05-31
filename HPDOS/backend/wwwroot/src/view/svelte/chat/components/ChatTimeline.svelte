<script lang="ts">
  import { tick } from "svelte";
  import type { ChatTimelineItem } from "../runtime/chatTypes";
  import MarkdownText from "./MarkdownText.svelte";
  import ReasoningDisclosure from "./ReasoningDisclosure.svelte";
  import ToolCard from "./cards/ToolCard.svelte";
  import {
    groupTimelineTurns,
    groupWorkedItems,
    summarizeTurnDetails,
    type ChatTimelineTurn,
    type ChatWorkedSegment
  } from "../runtime/chatTurns";

  type Props = {
    items: ChatTimelineItem[];
    streaming?: boolean;
    emptyText?: string;
  };

  let { items, streaming = false, emptyText = "Start a new run from this workspace." }: Props = $props();
  let timelineElement: HTMLElement | undefined = $state();
  let stickToBottom = $state(true);

  const segments = $derived(groupTimelineTurns(items));
  const timelineVersion = $derived(items.map(itemVersion).join("|"));

  $effect(() => {
    timelineVersion;
    if (!timelineElement || !stickToBottom) return;

    void tick().then(() => {
      if (!timelineElement || !stickToBottom) return;
      timelineElement.scrollTop = timelineElement.scrollHeight;
    });
  });

  function handleTimelineScroll(): void {
    if (!timelineElement) return;

    const distanceFromBottom =
      timelineElement.scrollHeight - timelineElement.clientHeight - timelineElement.scrollTop;
    stickToBottom = distanceFromBottom < 48;
  }

  function itemVersion(item: ChatTimelineItem): string {
    if (item.kind === "assistant-text" || item.kind === "reasoning") {
      return `${item.id}:${item.kind}:${item.text.length}:${item.complete}`;
    }

    if (item.kind === "user-message") {
      return `${item.id}:${item.kind}:${item.text.length}`;
    }

    if (item.kind === "error") {
      return `${item.id}:${item.kind}:${item.message.length}`;
    }

    if (item.kind === "tool-call") {
      return [
        item.id,
        item.kind,
        item.status,
        item.result ? JSON.stringify(item.result).length : 0,
        item.command?.liveOutput?.length ?? 0
      ].join(":");
    }

    if (item.kind === "permission") {
      return `${item.id}:${item.kind}:${item.pending}`;
    }

    if (item.kind === "clarification") {
      return `${item.id}:${item.kind}:${item.pending}:${item.answer?.length ?? 0}`;
    }

    return `${item.id}:${item.kind}`;
  }
</script>

<section bind:this={timelineElement} onscroll={handleTimelineScroll} class="hpd-chat-timeline" aria-label="Conversation" aria-live="polite">
  <div class="hpd-chat-stack">
    {#if items.length === 0}
      <div class="hpd-chat-empty">
        <p>{emptyText}</p>
      </div>
    {:else}
      {#each segments as segment (segment.kind === "turn" ? segment.id : segment.item.id)}
        {#if segment.kind === "turn"}
          {@render Turn(segment)}
        {:else}
          {@render Item(segment.item)}
        {/if}
      {/each}
      {#if streaming}
        <div class="hpd-chat-streaming-indicator" role="status" aria-label="Agent is working">
          <span>Working</span>
          <span class="hpd-chat-loading-dots" aria-hidden="true">
            <i></i>
            <i></i>
            <i></i>
          </span>
        </div>
      {/if}
    {/if}
  </div>
</section>

{#snippet Turn(turn: ChatTimelineTurn)}
  <section class="hpd-chat-turn" data-complete={turn.complete}>
    {@render Item(turn.user)}

    {#if turn.worked.length > 0}
      <details class="hpd-chat-turn-details" open={!turn.complete} data-live={!turn.complete}>
        <summary>
          <span>{turn.complete ? "Worked" : "Working"}</span>
          <small>{summarizeTurnDetails(turn.worked)}</small>
        </summary>
        <div class="hpd-chat-turn-detail-stack" data-live={!turn.complete}>
          {#each groupWorkedItems(turn.worked) as segment (segment.kind === "tool-group" ? segment.id : segment.item.id)}
            {@render WorkedSegment(segment)}
          {/each}
        </div>
      </details>
    {/if}

    {#if turn.final}
      {@render Item(turn.final)}
    {/if}
  </section>
{/snippet}

{#snippet WorkedSegment(segment: ChatWorkedSegment)}
  {#if segment.kind === "tool-group"}
    <details class="hpd-chat-tool-group">
      <summary>
        <span>{segment.summary}</span>
        <small>{segment.tools.length} {segment.tools.length === 1 ? "call" : "calls"}</small>
      </summary>
      <div class="hpd-chat-tool-group-stack">
        {#each segment.tools as item (item.id)}
          <ToolCard {item} />
        {/each}
      </div>
    </details>
  {:else}
    {@render Item(segment.item)}
  {/if}
{/snippet}

{#snippet Item(item: ChatTimelineItem)}
  {#if item.kind === "assistant-text"}
    <div class="hpd-chat-row" data-role="assistant">
      <article class="hpd-chat-message" data-role="assistant">
        <MarkdownText text={item.text} />
      </article>
    </div>
  {:else if item.kind === "user-message"}
    <div class="hpd-chat-row" data-role="user">
      <article class="hpd-chat-message" data-role="user">
        <p>{item.text}</p>
      </article>
    </div>
  {:else if item.kind === "reasoning"}
    <ReasoningDisclosure text={item.text} complete={item.complete} />
  {:else if item.kind === "error"}
    <article class="hpd-chat-error-event" role="alert">
      <strong>{item.source ?? "Error"}</strong>
      <span>{item.message}</span>
    </article>
  {:else if item.kind === "tool-call"}
    <ToolCard {item} />
  {:else if item.kind === "permission"}
    <article class="hpd-chat-card" data-kind="permission">
      <h3>Permission</h3>
      <p>{item.description ?? item.functionName}</p>
    </article>
  {:else if item.kind === "clarification"}
    <article class="hpd-chat-card" data-kind="clarification">
      <h3>Question</h3>
      <p>{item.question}</p>
    </article>
  {:else if item.kind === "branch-event"}
    <article class="hpd-chat-card hpd-chat-debug-card">
      <h3>{item.label}</h3>
    </article>
  {:else}
    <article class="hpd-chat-card hpd-chat-debug-card">
      <h3>{item.type}</h3>
    </article>
  {/if}
{/snippet}
