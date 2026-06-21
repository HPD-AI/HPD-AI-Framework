<svelte:options runes={true} />

<script lang="ts">
  import ThreadTimelineViewport from '../../src/thread-timeline-viewport/thread-timeline-viewport.svelte';
  import type { ThreadTimelineItem } from '@hpd-research/hpd-agent-headless-ui';

  let {
    timeline,
  }: {
    timeline: ThreadTimelineItem[];
  } = $props();
</script>

<ThreadTimelineViewport {timeline}>
  {#snippet children({ timeline: items, viewport, props })}
    <section data-testid="custom-viewport-inner" data-root-role={props.role}>
      <button type="button" data-testid="jump" onclick={viewport.scrollToBottom}>
        {viewport.isAtBottom ? 'bottom' : 'away'}
      </button>

      {#each items as item (item.id)}
        <article
          data-testid="custom-item"
          data-timeline-item-id={item.id}
          {@attach (node) => {
            viewport.registerItem(item.id, node);
            return () => viewport.unregisterItem(item.id);
          }}
        >
          {item.type}:{item.id}
        </article>
      {/each}
    </section>
  {/snippet}
</ThreadTimelineViewport>
