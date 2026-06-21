<svelte:options runes={true} />

<script lang="ts">
  import type { ThreadTimelineItem } from '@hpd-research/hpd-agent-headless-ui';
  import ThreadScrollToBottom from '../../src/thread-timeline-viewport/thread-scroll-to-bottom.svelte';
  import ThreadTimelineViewport from '../../src/thread-timeline-viewport/thread-timeline-viewport.svelte';
  import ThreadTimelineViewportFooter from '../../src/thread-timeline-viewport/thread-timeline-viewport-footer.svelte';

  let {
    timeline,
  }: {
    timeline: ThreadTimelineItem[];
  } = $props();
</script>

<ThreadTimelineViewport {timeline}>
  {#snippet children({ timeline: items })}
    {#each items as item (item.id)}
      <article data-timeline-item-id={item.id}>
        {item.type}:{item.id}
      </article>
    {/each}

    <ThreadScrollToBottom>Jump</ThreadScrollToBottom>

    <ThreadTimelineViewportFooter>
      Footer
    </ThreadTimelineViewportFooter>
  {/snippet}
</ThreadTimelineViewport>
