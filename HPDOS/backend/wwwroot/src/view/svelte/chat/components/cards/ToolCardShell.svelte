<script lang="ts">
  import type { Snippet } from "svelte";
  import type { ToolCallItem } from "../../runtime/chatTypes";

  type Props = {
    item: ToolCallItem;
    title: string;
    subtitle?: string;
    badge?: string;
    children?: Snippet;
  };

  let { item, title, subtitle, badge, children }: Props = $props();
  let expanded = $state(false);
</script>

<article class="hpd-chat-card hpd-chat-tool-card" data-status={item.status} data-expanded={expanded}>
  <button
    class="hpd-chat-tool-trigger"
    type="button"
    aria-expanded={expanded}
    onclick={() => expanded = !expanded}
  >
    <span class="hpd-chat-tool-title">
      <strong>{title}</strong>
      <small>{subtitle ?? item.status}</small>
    </span>
    <span class="hpd-chat-tool-meta">
      {#if badge}
        <span class="hpd-chat-diff-stat">{badge}</span>
      {/if}
      <small>{item.status}</small>
      <span class="hpd-chat-tool-chevron">{expanded ? "−" : "+"}</span>
    </span>
  </button>

  {#if expanded}
    <div class="hpd-chat-tool-body">
      {@render children?.()}

      <details class="hpd-chat-tool-payload">
        <summary>Event payload</summary>
        <pre class="hpd-chat-raw"><code>{JSON.stringify(item, null, 2)}</code></pre>
      </details>
    </div>
  {/if}
</article>
